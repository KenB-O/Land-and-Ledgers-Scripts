using EntityId = LandLedgers.Primitives.EntityId;

using System;
using System.Collections.Generic;
using LandLedgers.Economy.Businesses.Sawmill;
using LandLedgers.Economy.Equipment;
using LandLedgers.Economy.Farming.Timber;
using LandLedgers.Primitives;
using LandLedgers.Tasks;
using LandLedgers.Time;
using TimberSawmill = LandLedgers.Economy.Farming.Timber.Sawmill;

namespace LandLedgers.Economy.Businesses.Logging
{
    /// <summary>
    /// F6 Tier 1 (2026-11-13): sawmill production from real logs. A tested
    /// adapter, not a second sawmill runtime. It drives one Delivered lot
    /// from the shared mill LogStock through real RunSawing runs into one
    /// lumber lot plus one slab offcut fuel lot per run, with provenance
    /// intact, blade wear per profile, and honest SawMinutes credited
    /// exactly once per verified run.
    ///
    /// The driver never sets a lot state directly. It calls only mill
    /// ReceiveLogLot, mill RunSawing, counters RecordCreditedSawMinutes,
    /// counters RecordLumberProduced, and TaskAuthority task calls. The
    /// saw-logs task is minted and worked per run by the driver (forced
    /// by the RunSawing contract: HasRequiredSawingWork requires an
    /// InProgress saw-logs task with all planned minutes worked, and a
    /// second run cannot reuse a Complete task).
    ///
    /// Delivery proof in Tier 1 is yard presence plus FIFO, not a ledger
    /// state: the only production path into mill LogStock is
    /// LogHaul.DeliverToMill or a direct ReceiveLogLot call, so a lot
    /// present in the yard is a delivered lot. Partial runs re-identify
    /// the remainder: TryTakeOldestLot gives the remaining logs a fresh
    /// LotId (the consumed portion keeps the original id), so the driver
    /// follows the remainder's new id after a partial run; the job id
    /// (\"sawmill:\" plus the original lot id) never changes. The
    /// custody ledger is ported in Tier 2 (LogLotCustodyLedger); the
    /// driver records a fully consumed lot as Processed through its
    /// optional ledger reference at the completion step, with the
    /// original lot id derived from the job id. The driver's job record
    /// plus the yard stays the production state authority; registration
    /// of lots into the ledger from the live path is a follow-up slice,
    /// so a lot that was never registered is diagnosed, never invented.
    ///
    /// The driver is unbound in Tier 1: no hub list entry, no save
    /// section, no production call site. Wiring is instructions only
    /// (plan 20-plan-f6 §6). Tier 2 keeps the driver unbound (plan
    /// 22-plan-f6-tier2 §6): the ledger is an optional constructor
    /// reference only, and no production caller supplies one yet.
    /// </summary>
    [Serializable]
    public sealed class SawmillProductionJob
    {
        public string JobId = string.Empty;
        public string LotId = string.Empty;
        public int SawyerKind;
        public int SawyerNumber;
        public string MillBusinessId = string.Empty;
        public int LogsToSaw;
        public int LogsSawnTotal;
        public int LumberUnitsTotal;
        public int FuelWoodUnitsTotal;
        public int SawMinutesTotal;
        public List<string> SourceLumberLotIds = new List<string>();
        public List<string> SourceByproductLotIds = new List<string>();
        public bool LotFullyConsumed;
        public EntityId TaskId = EntityId.Invalid;
        public SawmillProductionJobState State = SawmillProductionJobState.Planned;
        public int CreatedDayIndex = -1;
        public int SawingDayIndex = -1;
        public int CompletedDayIndex = -1;
        public SawmillProductionJob() { }
    }

    public enum SawmillProductionJobState
    {
        Planned = 0,
        Sawing = 1,
        Complete = 2,
        Failed = 3,
    }

    public sealed class SawmillProductionDriver
    {
        private readonly SawmillShopRuntime mill;
        private readonly TaskAuthority taskAuthority;
        private readonly WorkTimeBudgetStore budgetStore;
        private readonly EquipmentTaskGate gate;
        private readonly SawmillLabourCounters labourCounters;
        private readonly List<string> diagnostics;
        private readonly LogLotCustodyLedger ledger;
        private readonly Dictionary<string, SawmillProductionJob> jobs =
            new Dictionary<string, SawmillProductionJob>(StringComparer.Ordinal);

        public SawmillProductionDriver(
            SawmillShopRuntime mill,
            TaskAuthority taskAuthority,
            WorkTimeBudgetStore budgetStore,
            EquipmentTaskGate gate,
            SawmillLabourCounters labourCounters,
            List<string> diagnostics,
            LogLotCustodyLedger ledger = null)
        {
            this.mill = mill;
            this.taskAuthority = taskAuthority;
            this.budgetStore = budgetStore;
            this.gate = gate;
            this.labourCounters = labourCounters;
            this.diagnostics = diagnostics ?? new List<string>();
            this.ledger = ledger;
            if (ledger == null)
            {
                this.diagnostics.Add("SawmillProductionDriver: no custody ledger — produced lots are not recorded as Processed; production proceeds.");
            }
            if (mill == null)
            {
                this.diagnostics.Add("SawmillProductionDriver: no sawmill runtime — production refused.");
            }
            if (taskAuthority == null)
            {
                this.diagnostics.Add("SawmillProductionDriver: no task authority — production refused.");
            }
            if (budgetStore == null)
            {
                this.diagnostics.Add("SawmillProductionDriver: no work-time budget store — production refused.");
            }
            if (gate == null)
            {
                this.diagnostics.Add("SawmillProductionDriver: no equipment/workstation gate — runs will refuse at RunSawing.");
            }
            if (labourCounters == null)
            {
                this.diagnostics.Add("SawmillProductionDriver: no labour counters — production refused.");
            }
        }

        /// <summary>
        /// The business entity id that owns a business's work tasks. Mirrors
        /// SimulationDrivers.StableBusinessRuntimeId (the production task
        /// ownership convention) so tasks scheduled by the labor drivers are
        /// found here unchanged. (MineProductionDriver.cs precedent.)
        /// </summary>
        public static EntityId BusinessEntityIdFor(string businessInstanceId)
        {
            unchecked
            {
                int hash = 17;
                foreach (char character in businessInstanceId ?? string.Empty)
                    hash = hash * 31 + character;
                return EntityId.For(EntityKind.Business, hash & 0x7fffffff);
            }
        }

        public SawmillShopRuntime Mill => mill;
        public TaskAuthority TaskAuthority => taskAuthority;
        public WorkTimeBudgetStore BudgetStore => budgetStore;
        public EquipmentTaskGate Gate => gate;
        public SawmillLabourCounters LabourCounters => labourCounters;
        public LogLotCustodyLedger Ledger => ledger;
        public IReadOnlyList<string> Diagnostics => diagnostics;
        public int ProductionJobCount => jobs.Count;
        public IReadOnlyDictionary<string, SawmillProductionJob> ProductionJobs => jobs;

        /// <summary>
        /// Intake for a lot that is not already in the mill yard, used only
        /// when the lot did not arrive through the native haul into the
        /// shared stock. Guarded by the stock-presence check so it can
        /// never duplicate a lot (the stock ReceiveLogLot has no duplicate
        /// check of its own). Creates no job and touches no ledger.
        /// </summary>
        public bool ReceiveLogLot(LogLot lot, int dayIndex, out string refusal)
        {
            refusal = null;
            if (mill == null)
            {
                refusal = "SawmillProductionDriver.ReceiveLogLot: no sawmill runtime — no lot can be received.";
                diagnostics.Add(refusal);
                return false;
            }
            if (lot == null)
            {
                refusal = "SawmillProductionDriver.ReceiveLogLot: no lot offered — nothing received.";
                diagnostics.Add(refusal);
                return false;
            }
            if (FindLotInYard(lot.LotId.ToString()) != null)
            {
                diagnostics.Add($"SawmillProductionDriver.ReceiveLogLot: lot {lot.LotId} is already in the mill yard — repeated intake ignored, no second copy added.");
                return true;
            }
            string stockRefusal = mill.ReceiveLogLot(lot, diagnostics);
            if (stockRefusal != null)
            {
                refusal = "SawmillProductionDriver.ReceiveLogLot: " + stockRefusal;
                diagnostics.Add(refusal);
                return false;
            }
            diagnostics.Add($"SawmillProductionDriver.ReceiveLogLot: lot {lot.LotId} from stand '{lot.StandId}' received into the mill yard.");
            return true;
        }

        /// <summary>
        /// Begins a production job for a lot already in the mill yard.
        /// Never mints a task, never calls RunSawing, never applies wear,
        /// never credits counters. A refused begin leaves the yard untouched.
        /// </summary>
        public SawmillProductionJob BeginProduction(string lotId, EntityId sawyerId, int logsToSaw, int dayIndex, out string refusal)
        {
            refusal = null;
            if (mill == null)
            {
                refusal = "SawmillProductionDriver.BeginProduction: no sawmill runtime — production refused.";
                diagnostics.Add(refusal);
                return null;
            }
            if (taskAuthority == null)
            {
                refusal = "SawmillProductionDriver.BeginProduction: no task authority — a job that can never run is a trap.";
                diagnostics.Add(refusal);
                return null;
            }
            if (budgetStore == null)
            {
                refusal = "SawmillProductionDriver.BeginProduction: no work-time budget store — production refused.";
                diagnostics.Add(refusal);
                return null;
            }
            if (labourCounters == null)
            {
                refusal = "SawmillProductionDriver.BeginProduction: no labour counters — production refused.";
                diagnostics.Add(refusal);
                return null;
            }
            if (string.IsNullOrWhiteSpace(lotId))
            {
                refusal = "SawmillProductionDriver.BeginProduction: no lot id supplied — production refused.";
                diagnostics.Add(refusal);
                return null;
            }
            if (logsToSaw < 1)
            {
                refusal = $"SawmillProductionDriver.BeginProduction: job for lot '{lotId}' requests {logsToSaw} whole logs — below 1, refused.";
                diagnostics.Add(refusal);
                return null;
            }
            if (!sawyerId.IsValid || sawyerId.Kind != EntityKind.Person)
            {
                refusal = $"SawmillProductionDriver.BeginProduction: job for lot '{lotId}' names no valid Person sawyer ('{sawyerId}') — refused.";
                diagnostics.Add(refusal);
                return null;
            }
            if (FindLotInYard(lotId) == null)
            {
                refusal = $"SawmillProductionDriver.BeginProduction: lot '{lotId}' is absent from the mill yard — intake must happen first.";
                diagnostics.Add(refusal);
                return null;
            }
            LogLot oldest = mill.LogStock.PeekOldestLot(logsToSaw, diagnostics);
            if (oldest == null)
            {
                refusal = $"SawmillProductionDriver.BeginProduction: the oldest lot in the mill yard holds fewer than {logsToSaw} logs — run size refused.";
                diagnostics.Add(refusal);
                return null;
            }
            if (!string.Equals(oldest.LotId.ToString(), lotId, StringComparison.Ordinal))
            {
                refusal = $"SawmillProductionDriver.BeginProduction: lot '{lotId}' is not the oldest lot in the mill yard — FIFO order names oldest lot '{oldest.LotId}'.";
                diagnostics.Add(refusal);
                return null;
            }
            string jobId = "sawmill:" + lotId;
            if (jobs.TryGetValue(jobId, out SawmillProductionJob open)
                && (open.State == SawmillProductionJobState.Planned || open.State == SawmillProductionJobState.Sawing))
            {
                refusal = $"SawmillProductionDriver.BeginProduction: job '{jobId}' is already open ({open.State}) — a retried begin never creates a second job.";
                diagnostics.Add(refusal);
                return null;
            }
            var job = new SawmillProductionJob
            {
                JobId = jobId,
                LotId = lotId,
                SawyerKind = (int)sawyerId.Kind,
                SawyerNumber = sawyerId.Id,
                MillBusinessId = mill.BusinessInstanceId,
                LogsToSaw = logsToSaw,
                TaskId = EntityId.Invalid,
                State = SawmillProductionJobState.Planned,
                CreatedDayIndex = dayIndex,
            };
            jobs[jobId] = job;
            diagnostics.Add($"SawmillProductionDriver.BeginProduction: job '{jobId}' began for {logsToSaw} logs at mill '{job.MillBusinessId}'.");
            return job;
        }

        /// <summary>
        /// Records a fully consumed lot as Processed in the custody ledger.
        /// The original lot id is derived from the job id ("sawmill:" plus
        /// the original lot id, built once in BeginProduction and never
        /// rewritten); job.LotId is the remainder's fresh id after a partial
        /// run and must never be marked. A refusal is diagnostic only: the
        /// sawing already happened and the lumber already exists, so the job
        /// still completes with its counters credited.
        /// </summary>
        private void MarkLedgerProcessed(SawmillProductionJob job, int dayIndex)
        {
            if (ledger == null)
            {
                diagnostics.Add($"SawmillProductionDriver.MarkLedgerProcessed: job '{job.JobId}' — no custody ledger; the lot is not recorded as Processed.");
                return;
            }
            string originalLotId = job.JobId.Substring("sawmill:".Length);
            string refusal = ledger.MarkProcessed(originalLotId, job.MillBusinessId, dayIndex);
            if (refusal != null)
            {
                diagnostics.Add($"SawmillProductionDriver.MarkLedgerProcessed: job '{job.JobId}' — the ledger could not record lot '{originalLotId}' as Processed: {refusal}");
                return;
            }
            diagnostics.Add($"SawmillProductionDriver.MarkLedgerProcessed: job '{job.JobId}' — lot '{originalLotId}' recorded as Processed by '{job.MillBusinessId}' (day {dayIndex}).");
        }

        /// <summary>
        /// The one place RunSawing is called, the one place run tasks are
        /// minted and worked, and the one place counters are credited.
        /// </summary>
        public bool RunProduction(string jobId, int dayIndex, out string refusal)
        {
            refusal = null;
            if (mill == null)
            {
                refusal = "SawmillProductionDriver.RunProduction: no sawmill runtime — production refused.";
                diagnostics.Add(refusal);
                return false;
            }
            if (taskAuthority == null)
            {
                refusal = "SawmillProductionDriver.RunProduction: no task authority — production refused.";
                diagnostics.Add(refusal);
                return false;
            }
            if (budgetStore == null)
            {
                refusal = "SawmillProductionDriver.RunProduction: no work-time budget store — production refused.";
                diagnostics.Add(refusal);
                return false;
            }
            if (labourCounters == null)
            {
                refusal = "SawmillProductionDriver.RunProduction: no labour counters — production refused.";
                diagnostics.Add(refusal);
                return false;
            }
            if (string.IsNullOrWhiteSpace(jobId))
            {
                refusal = "SawmillProductionDriver.RunProduction: no job id supplied — production refused.";
                diagnostics.Add(refusal);
                return false;
            }
            if (!jobs.TryGetValue(jobId, out SawmillProductionJob job))
            {
                refusal = $"SawmillProductionDriver.RunProduction: unknown production job '{jobId}'.";
                diagnostics.Add(refusal);
                return false;
            }
            if (job.State == SawmillProductionJobState.Complete || job.State == SawmillProductionJobState.Failed)
            {
                refusal = $"SawmillProductionDriver.RunProduction: job '{jobId}' is {job.State} — a terminal job never runs again.";
                diagnostics.Add(refusal);
                return false;
            }
            if (FindLotInYard(job.LotId) == null)
            {
                refusal = $"SawmillProductionDriver.RunProduction: lot '{job.LotId}' for job '{jobId}' is absent from the mill yard.";
                diagnostics.Add(refusal);
                return false;
            }
            LogLot oldest = mill.LogStock.PeekOldestLot(job.LogsToSaw, diagnostics);
            if (oldest == null)
            {
                refusal = $"SawmillProductionDriver.RunProduction: the oldest lot in the mill yard holds fewer than {job.LogsToSaw} logs for job '{jobId}' — run size refused.";
                diagnostics.Add(refusal);
                return false;
            }
            if (!string.Equals(oldest.LotId.ToString(), job.LotId, StringComparison.Ordinal))
            {
                refusal = $"SawmillProductionDriver.RunProduction: lot '{job.LotId}' for job '{jobId}' is not the oldest lot in the mill yard — FIFO order names oldest lot '{oldest.LotId}'.";
                diagnostics.Add(refusal);
                return false;
            }

            // Labour sequence, mirroring the SawmillRuntimeTests helper step
            // for step: the task minutes are worked BEFORE RunSawing, which
            // completes the task itself on success.
            EntityId sawyer = new EntityId { Kind = (EntityKind)job.SawyerKind, Id = job.SawyerNumber };
            if (!sawyer.IsValid || sawyer.Kind != EntityKind.Person)
            {
                refusal = $"SawmillProductionDriver.RunProduction: job '{jobId}' names no valid Person sawyer ('{sawyer}') — refused.";
                diagnostics.Add(refusal);
                return false;
            }
            SawmillConversionProfile profile = SawmillConversionData.DefaultForSpecies(oldest.Species);
            int requiredMinutes = job.LogsToSaw * profile.SawMinutesPerLog;
            if (taskAuthority.GetDefinition(TimberSawmill.SawLogsTaskId) == null)
            {
                TimberSawmill.RegisterTaskDefinitions(taskAuthority);
            }
            if (taskAuthority.GetDefinition(TimberSawmill.SawLogsTaskId) == null)
            {
                refusal = $"SawmillProductionDriver.RunProduction: job '{jobId}' — the '{TimberSawmill.SawLogsTaskId}' task definition is unavailable, no task minted.";
                diagnostics.Add(refusal);
                return false;
            }
            WorkTask task = taskAuthority.CreateTaskWithPlannedMinutes(
                TimberSawmill.SawLogsTaskId, BusinessEntityIdFor(job.MillBusinessId), dayIndex, requiredMinutes);
            if (task == null)
            {
                refusal = $"SawmillProductionDriver.RunProduction: job '{jobId}' — the run task could not be minted.";
                diagnostics.Add(refusal);
                return false;
            }
            job.TaskId = task.TaskId;
            if (!taskAuthority.AssignTask(task.TaskId, sawyer, budgetStore, dayIndex, out string assignReason))
            {
                taskAuthority.CancelTask(task.TaskId, budgetStore);
                refusal = $"SawmillProductionDriver.RunProduction: job '{jobId}' — run task assignment refused: {assignReason}";
                diagnostics.Add(refusal);
                return false;
            }
            if (!taskAuthority.StartTask(task.TaskId, dayIndex, out string startReason))
            {
                taskAuthority.CancelTask(task.TaskId, budgetStore);
                refusal = $"SawmillProductionDriver.RunProduction: job '{jobId}' — run task start refused: {startReason}";
                diagnostics.Add(refusal);
                return false;
            }
            if (!taskAuthority.RecordWorkProgress(task.TaskId, requiredMinutes, budgetStore, dayIndex, out bool reached, out string workReason)
                || !reached)
            {
                taskAuthority.CancelTask(task.TaskId, budgetStore);
                refusal = $"SawmillProductionDriver.RunProduction: job '{jobId}' — run task labour refused: {workReason}";
                diagnostics.Add(refusal);
                return false;
            }

            // Sawing step. The yard total distinguishes the two nulls:
            // pre-deduction null is a refusal (the logs are still there);
            // post-deduction null is a loss (the logs are gone).
            // Snapshot the yard ids first: a partial run re-identifies
            // the remainder (see the completion step below).
            var yardIdsBefore = new HashSet<string>(StringComparer.Ordinal);
            foreach (LogLot yardLot in mill.LogStock.Lots)
            {
                if (yardLot != null)
                {
                    yardIdsBefore.Add(yardLot.LotId.ToString());
                }
            }
            int yardTotalBefore = mill.LogStock.TotalLogUnits;
            int diagMark = diagnostics.Count;
            SawmillSawingResult result = mill.RunSawing(
                job.LogsToSaw, sawyer, dayIndex, diagnostics, taskAuthority, task.TaskId, budgetStore, gate);
            if (result == null)
            {
                taskAuthority.CancelTask(task.TaskId, budgetStore);
                string runtimeReason = RuntimeReasonSince(diagMark);
                if (mill.LogStock.TotalLogUnits < yardTotalBefore)
                {
                    int lostLogs = yardTotalBefore - mill.LogStock.TotalLogUnits;
                    job.State = SawmillProductionJobState.Failed;
                    refusal = $"SawmillProductionDriver.RunProduction: job '{jobId}' — RunSawing returned null AFTER deducting {lostLogs} log(s); the job is Failed and the caller must reconcile the missing logs. Runtime reason: {runtimeReason}";
                    diagnostics.Add(refusal);
                    return false;
                }
                refusal = $"SawmillProductionDriver.RunProduction: job '{jobId}' — RunSawing refused before deduction; nothing consumed, a later retry mints a fresh task. Runtime reason: {runtimeReason}";
                diagnostics.Add(refusal);
                return false;
            }

            // Verification step: the result must be exactly this job's run.
            string mismatch = VerifyResult(job, result);
            if (mismatch != null)
            {
                job.State = SawmillProductionJobState.Sawing;
                refusal = $"SawmillProductionDriver.RunProduction: job '{jobId}' — RunSawing returned a result that does not match the job ({mismatch}); no counters credited, the output lots already exist in the runtime stock and the production caller must reconcile them.";
                diagnostics.Add(refusal);
                return false;
            }

            // Credit step: exactly once for this verified run.
            labourCounters.RecordCreditedSawMinutes(job.MillBusinessId, result.SawMinutes);
            labourCounters.RecordLumberProduced(job.MillBusinessId, result.LumberLot.LumberUnits);
            job.SawMinutesTotal += result.SawMinutes;
            job.LogsSawnTotal += result.LogsSawn;
            job.LumberUnitsTotal += result.LumberLot.LumberUnits;
            job.FuelWoodUnitsTotal += result.ByproductLot.FuelWoodUnits;
            job.SourceLumberLotIds.Add(result.LumberLot.LotId.ToString());
            job.SourceByproductLotIds.Add(result.ByproductLot.LotId.ToString());
            if (job.SawingDayIndex < 0)
            {
                job.SawingDayIndex = dayIndex;
            }
            job.State = SawmillProductionJobState.Sawing;
            if (mill.Blade != null && mill.Blade.IsDull)
            {
                diagnostics.Add($"SawmillProductionDriver.RunProduction: job '{jobId}' — the blade is now dull; the filer must work before the next run.");
            }

            // Completion step: the lot is fully consumed only when it is
            // absent from the mill yard. Partial-run re-identification:
            // SawmillLogStock.TryTakeOldestLot gives the REMAINDER a fresh
            // LotId (the consumed portion keeps the original id), so after
            // a partial run the job's lot id is gone from the yard while
            // the remainder lives on under exactly one new id. Follow the
            // remainder or a partial run would look complete.
            LogLot remainder = null;
            int newYardIds = 0;
            foreach (LogLot yardLot in mill.LogStock.Lots)
            {
                if (yardLot != null && !yardIdsBefore.Contains(yardLot.LotId.ToString()))
                {
                    newYardIds++;
                    remainder = yardLot;
                }
            }
            if (newYardIds == 1 && remainder != null)
            {
                diagnostics.Add($"SawmillProductionDriver.RunProduction: job '{jobId}' — lot partially consumed; the remainder ({remainder.LogUnits} logs) keeps its identity under the runtime's fresh lot id '{remainder.LotId}'.");
                job.LotId = remainder.LotId.ToString();
                return true;
            }
            if (newYardIds > 1)
            {
                diagnostics.Add($"SawmillProductionDriver.RunProduction: job '{jobId}' — unexpected: {newYardIds} new lot ids appeared in the yard after one run; the job stays Sawing and the production caller must reconcile.");
                return true;
            }
            if (FindLotInYard(job.LotId) != null)
            {
                return true;
            }
            job.LotFullyConsumed = true;
            job.CompletedDayIndex = dayIndex;
            job.State = SawmillProductionJobState.Complete;
            MarkLedgerProcessed(job, dayIndex);
            diagnostics.Add($"SawmillProductionDriver.RunProduction: job '{jobId}' complete — lot '{job.LotId}' fully consumed ({job.LogsSawnTotal} logs, {job.LumberUnitsTotal} lumber, {job.FuelWoodUnitsTotal} fuel wood, {job.SawMinutesTotal} saw minutes).");
            return true;
        }

        /// <summary>
        /// Abandons a job. The lot remainder stays in the mill yard, no
        /// counters are credited. A fresh BeginProduction for the same lot
        /// is allowed because no open job remains.
        /// </summary>
        public bool FailProduction(string jobId, int dayIndex, out string refusal)
        {
            refusal = null;
            if (string.IsNullOrWhiteSpace(jobId))
            {
                refusal = "SawmillProductionDriver.FailProduction: no job id supplied — nothing failed.";
                diagnostics.Add(refusal);
                return false;
            }
            if (!jobs.TryGetValue(jobId, out SawmillProductionJob job))
            {
                refusal = $"SawmillProductionDriver.FailProduction: unknown production job '{jobId}'.";
                diagnostics.Add(refusal);
                return false;
            }
            if (job.State == SawmillProductionJobState.Complete || job.State == SawmillProductionJobState.Failed)
            {
                refusal = $"SawmillProductionDriver.FailProduction: job '{jobId}' is {job.State} — a terminal job cannot fail again.";
                diagnostics.Add(refusal);
                return false;
            }
            job.State = SawmillProductionJobState.Failed;
            diagnostics.Add($"SawmillProductionDriver.FailProduction: job '{jobId}' failed on day {dayIndex}; the lot remainder stays in the mill yard.");
            return true;
        }

        public SawmillProductionJob GetJob(string jobId)
        {
            if (string.IsNullOrWhiteSpace(jobId)) return null;
            return jobs.TryGetValue(jobId, out SawmillProductionJob job) ? job : null;
        }

        public List<SawmillProductionJob> JobsInState(SawmillProductionJobState state)
        {
            var found = new List<SawmillProductionJob>();
            foreach (SawmillProductionJob job in jobs.Values)
            {
                if (job != null && job.State == state)
                {
                    found.Add(job);
                }
            }
            return found;
        }

        // ---- Save contract (nested DTO, F2 driver / F3 ledger pattern) ----

        [Serializable]
        public sealed class SawmillProductionDriverSaveDto
        {
            public List<SawmillProductionJobLink> ProductionJobLinks = new List<SawmillProductionJobLink>();

            [Serializable]
            public sealed class SawmillProductionJobLink
            {
                public string JobId = string.Empty;
                public string LotId = string.Empty;
                public int SawyerKind;
                public int SawyerNumber;
                public string MillBusinessId = string.Empty;
                public int LogsToSaw;
                public int LogsSawnTotal;
                public int LumberUnitsTotal;
                public int FuelWoodUnitsTotal;
                public int SawMinutesTotal;
                public List<string> SourceLumberLotIds = new List<string>();
                public List<string> SourceByproductLotIds = new List<string>();
                public bool LotFullyConsumed;
                public EntityId TaskId = EntityId.Invalid;
                public int State;
                public int CreatedDayIndex = -1;
                public int SawingDayIndex = -1;
                public int CompletedDayIndex = -1;
            }
        }

        /// <summary>
        /// Deep copies every open job, Planned and Sawing only. Complete
        /// and Failed jobs are terminal and are not persisted.
        /// </summary>
        public SawmillProductionDriverSaveDto ToSaveDto()
        {
            var dto = new SawmillProductionDriverSaveDto();
            foreach (SawmillProductionJob job in jobs.Values)
            {
                if (job == null) continue;
                if (job.State != SawmillProductionJobState.Planned && job.State != SawmillProductionJobState.Sawing) continue;
                dto.ProductionJobLinks.Add(new SawmillProductionDriverSaveDto.SawmillProductionJobLink
                {
                    JobId = job.JobId,
                    LotId = job.LotId,
                    SawyerKind = job.SawyerKind,
                    SawyerNumber = job.SawyerNumber,
                    MillBusinessId = job.MillBusinessId,
                    LogsToSaw = job.LogsToSaw,
                    LogsSawnTotal = job.LogsSawnTotal,
                    LumberUnitsTotal = job.LumberUnitsTotal,
                    FuelWoodUnitsTotal = job.FuelWoodUnitsTotal,
                    SawMinutesTotal = job.SawMinutesTotal,
                    SourceLumberLotIds = new List<string>(job.SourceLumberLotIds),
                    SourceByproductLotIds = new List<string>(job.SourceByproductLotIds),
                    LotFullyConsumed = job.LotFullyConsumed,
                    TaskId = job.TaskId,
                    State = (int)job.State,
                    CreatedDayIndex = job.CreatedDayIndex,
                    SawingDayIndex = job.SawingDayIndex,
                    CompletedDayIndex = job.CompletedDayIndex,
                });
            }
            return dto;
        }

        /// <summary>
        /// Clear-then-load. A null DTO clears to empty. Links re-attach
        /// against mill LogStock Lots (the lot must still be present) and
        /// against the task authority for the TaskId link only: a stale
        /// task link is dropped (TaskId reset to Invalid) and the job is
        /// kept, because a retry mints a fresh task. Re-attachment never
        /// re-credits counters and never re-runs RunSawing.
        /// </summary>
        public void LoadFromSaveDto(SawmillProductionDriverSaveDto dto)
        {
            jobs.Clear();
            if (dto == null || dto.ProductionJobLinks == null) return;
            var seenJobIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (SawmillProductionDriverSaveDto.SawmillProductionJobLink link in dto.ProductionJobLinks)
            {
                if (link == null) continue;
                if (string.IsNullOrWhiteSpace(link.JobId))
                {
                    diagnostics.Add("SawmillProductionDriver.LoadFromSaveDto: skipped a link with no job id.");
                    continue;
                }
                if (!seenJobIds.Add(link.JobId))
                {
                    diagnostics.Add($"SawmillProductionDriver.LoadFromSaveDto: skipped duplicate link for job '{link.JobId}' — the first link is kept.");
                    continue;
                }
                if (string.IsNullOrWhiteSpace(link.LotId))
                {
                    diagnostics.Add($"SawmillProductionDriver.LoadFromSaveDto: skipped link for job '{link.JobId}' — no lot id.");
                    continue;
                }
                if (link.State < 0 || link.State > 3)
                {
                    diagnostics.Add($"SawmillProductionDriver.LoadFromSaveDto: skipped link for job '{link.JobId}' — state value {link.State} is outside 0 to 3.");
                    continue;
                }
                if (link.LogsToSaw < 1)
                {
                    diagnostics.Add($"SawmillProductionDriver.LoadFromSaveDto: skipped link for job '{link.JobId}' — run size {link.LogsToSaw} is below 1.");
                    continue;
                }
                if (link.LogsSawnTotal < 0 || link.LumberUnitsTotal < 0 || link.FuelWoodUnitsTotal < 0 || link.SawMinutesTotal < 0)
                {
                    diagnostics.Add($"SawmillProductionDriver.LoadFromSaveDto: skipped link for job '{link.JobId}' — a total is below 0.");
                    continue;
                }
                if (string.IsNullOrWhiteSpace(link.MillBusinessId))
                {
                    diagnostics.Add($"SawmillProductionDriver.LoadFromSaveDto: skipped link for job '{link.JobId}' — no mill business id; output must always belong to a named mill.");
                    continue;
                }
                if (link.LotFullyConsumed)
                {
                    diagnostics.Add($"SawmillProductionDriver.LoadFromSaveDto: skipped link for job '{link.JobId}' — it claims full consumption, but Complete jobs are never persisted.");
                    continue;
                }
                if (mill == null || FindLotInYard(link.LotId) == null)
                {
                    diagnostics.Add($"SawmillProductionDriver.LoadFromSaveDto: dropped link for job '{link.JobId}' — lot '{link.LotId}' is missing from the mill yard.");
                    continue;
                }
                EntityId taskId = link.TaskId;
                if (taskId.IsValid)
                {
                    if (taskAuthority == null || taskAuthority.FindTask(taskId) == null)
                    {
                        diagnostics.Add($"SawmillProductionDriver.LoadFromSaveDto: job '{link.JobId}' — stale task link '{taskId}' dropped, job kept; a retry mints a fresh task.");
                        taskId = EntityId.Invalid;
                    }
                }
                jobs[link.JobId] = new SawmillProductionJob
                {
                    JobId = link.JobId,
                    LotId = link.LotId,
                    SawyerKind = link.SawyerKind,
                    SawyerNumber = link.SawyerNumber,
                    MillBusinessId = link.MillBusinessId,
                    LogsToSaw = link.LogsToSaw,
                    LogsSawnTotal = link.LogsSawnTotal,
                    LumberUnitsTotal = link.LumberUnitsTotal,
                    FuelWoodUnitsTotal = link.FuelWoodUnitsTotal,
                    SawMinutesTotal = link.SawMinutesTotal,
                    SourceLumberLotIds = link.SourceLumberLotIds != null ? new List<string>(link.SourceLumberLotIds) : new List<string>(),
                    SourceByproductLotIds = link.SourceByproductLotIds != null ? new List<string>(link.SourceByproductLotIds) : new List<string>(),
                    LotFullyConsumed = link.LotFullyConsumed,
                    TaskId = taskId,
                    State = (SawmillProductionJobState)link.State,
                    CreatedDayIndex = link.CreatedDayIndex,
                    SawingDayIndex = link.SawingDayIndex,
                    CompletedDayIndex = link.CompletedDayIndex,
                };
            }
        }

        private LogLot FindLotInYard(string lotId)
        {
            if (mill == null || mill.LogStock == null || string.IsNullOrWhiteSpace(lotId)) return null;
            foreach (LogLot lot in mill.LogStock.Lots)
            {
                if (lot != null && string.Equals(lot.LotId.ToString(), lotId, StringComparison.Ordinal))
                {
                    return lot;
                }
            }
            return null;
        }

        private string VerifyResult(SawmillProductionJob job, SawmillSawingResult result)
        {
            if (!string.Equals(result.SourceLogLotId, job.LotId, StringComparison.Ordinal))
            {
                return $"source log lot '{result.SourceLogLotId}' is not the job lot '{job.LotId}'";
            }
            if (result.LogsSawn != job.LogsToSaw)
            {
                return $"sawed {result.LogsSawn} logs, job requested {job.LogsToSaw}";
            }
            if (result.LumberLot == null)
            {
                return "no lumber lot was produced";
            }
            if (result.ByproductLot == null)
            {
                return "no byproduct lot was produced";
            }
            if (!string.Equals(result.LumberLot.MillBusinessId, job.MillBusinessId, StringComparison.Ordinal))
            {
                return $"lumber lot belongs to mill '{result.LumberLot.MillBusinessId}', job mill is '{job.MillBusinessId}'";
            }
            if (!string.Equals(result.LumberLot.SourceLogLotId, job.LotId, StringComparison.Ordinal))
            {
                return $"lumber lot traces to log lot '{result.LumberLot.SourceLogLotId}', job lot is '{job.LotId}'";
            }
            if (!string.Equals(result.ByproductLot.SourceLogLotId, job.LotId, StringComparison.Ordinal))
            {
                return $"byproduct lot traces to log lot '{result.ByproductLot.SourceLogLotId}', job lot is '{job.LotId}'";
            }
            return null;
        }

        private string RuntimeReasonSince(int diagMark)
        {
            if (diagMark < 0 || diagMark >= diagnostics.Count) return "no runtime reason recorded";
            var reasons = new List<string>();
            for (int i = diagMark; i < diagnostics.Count; i++)
            {
                reasons.Add(diagnostics[i]);
            }
            return string.Join(" ", reasons);
        }
    }
}
