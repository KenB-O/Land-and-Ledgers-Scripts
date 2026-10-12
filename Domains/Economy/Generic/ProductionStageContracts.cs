using System;
using System.Collections.Generic;
using UnityEngine;

namespace LandLedgers.Economy.Generic
{
    /// <summary>
    /// Pass IV (Living World Foundation): active and passive production stages.
    ///
    /// Production distinguishes:
    /// - Active labor: a person must physically perform work (a WorkTask).
    /// - Passive elapsed process: material/machine changes without continuous labor
    ///   (proofing, baking); the worker is released but the equipment/workspace/materials
    ///   stay occupied, and a due event recalls someone for tending/unload.
    /// - Periodic tending: a person must return at meaningful points.
    /// - Blocking dependency: work waits for an event, delivery, or resource.
    ///
    /// Missed deadlines have method-specific, configurable consequences (deterioration,
    /// waste, batch failure, corrective task) — never a single hard-coded "everything
    /// burns" rule. No magical unattended production: output appears only after the
    /// production event it belongs to.
    ///
    /// These contracts extend the existing ProductionMethodDefinition /
    /// ProductionPhaseDefinition / ProductionProcessState (GenericBusinessEconomy.cs);
    /// Worker 3 maps phases to stages. No second production engine.
    ///
    /// Canon: XXXI Part III §§3.1-3.7. Technical: XIV Part II §§2.1-2.8.
    /// </summary>
    public enum ProductionStageKind
    {
        Unspecified = 0,
        /// <summary>A person must physically perform work for the stage duration.</summary>
        ActiveLabor = 1,
        /// <summary>Elapsed time with no person; equipment/workspace/materials occupied.</summary>
        PassiveProcess = 2,
        /// <summary>Passive process with required check-ins at intervals.</summary>
        PeriodicTending = 3,
        /// <summary>Stage waits for an external event, delivery, or resource.</summary>
        BlockingDependency = 4,
    }

    /// <summary>
    /// Pass IV: what happens when a tending deadline is missed. Method-specific and
    /// configurable; the stage definition names the consequence, the runtime applies it.
    /// </summary>
    public enum MissedDeadlineConsequence
    {
        None = 0,
        /// <summary>Output quality degrades (waste fraction recorded).</summary>
        QualityDegraded = 1,
        /// <summary>Part of the batch is wasted (inputs partially lost).</summary>
        PartialWaste = 2,
        /// <summary>The batch fails; a corrective/rework task becomes necessary.</summary>
        BatchFailed = 3,
        /// <summary>Equipment needs maintenance before reuse.</summary>
        EquipmentMaintenanceRequired = 4,
    }

    /// <summary>Pass IV: one production stage. Plain data; authored per method.</summary>
    [Serializable]
    public sealed class ProductionStageDefinition
    {
        [SerializeField]
        private ProductionStageKind kind = ProductionStageKind.ActiveLabor;

        /// <summary>Stage duration in game seconds (precise).</summary>
        [SerializeField]
        private float durationGameSeconds = 60f;

        /// <summary>For PeriodicTending: interval between check-ins, in game seconds.</summary>
        [SerializeField]
        private float tendingIntervalGameSeconds;

        /// <summary>
        /// For PeriodicTending/PassiveProcess: grace period after the due time before
        /// the missed-deadline consequence applies, in game seconds.
        /// </summary>
        [SerializeField]
        private float tendingGraceGameSeconds;

        [SerializeField]
        private MissedDeadlineConsequence missedConsequence = MissedDeadlineConsequence.None;

        /// <summary>Waste fraction 0..1 applied on PartialWaste.</summary>
        [SerializeField]
        private float wasteFraction01;

        /// <summary>Task definition id for ActiveLabor stages (empty = generic labor).</summary>
        [SerializeField]
        private string laborTaskDefinitionId = string.Empty;

        /// <summary>Reservation keys held for the whole stage (oven, workspace, batch lot...).</summary>
        [SerializeField]
        private List<string> stageReservationKeys = new List<string>();

        public ProductionStageKind Kind => kind;
        public float DurationGameSeconds => Math.Max(0f, durationGameSeconds);
        public float TendingIntervalGameSeconds => Math.Max(0f, tendingIntervalGameSeconds);
        public float TendingGraceGameSeconds => Math.Max(0f, tendingGraceGameSeconds);
        public MissedDeadlineConsequence MissedConsequence => missedConsequence;
        public float WasteFraction01 => Mathf.Clamp01(wasteFraction01);
        public string LaborTaskDefinitionId => laborTaskDefinitionId;
        public List<string> StageReservationKeys => stageReservationKeys;

        public ProductionStageDefinition()
        {
        }

        public ProductionStageDefinition(ProductionStageKind kind, float durationGameSeconds)
        {
            this.kind = kind;
            this.durationGameSeconds = Math.Max(0f, durationGameSeconds);
        }

        /// <summary>
        /// Pass IV (Worker 3, additive): full authoring path for slice methods. The
        /// contract declares these fields "authored per method" but previously offered
        /// no code path to set them; this overload fills that gap without changing
        /// any existing constructor or caller.
        /// </summary>
        public ProductionStageDefinition(
            ProductionStageKind kind,
            float durationGameSeconds,
            float tendingIntervalGameSeconds,
            float tendingGraceGameSeconds,
            MissedDeadlineConsequence missedConsequence,
            float wasteFraction01 = 0f,
            string laborTaskDefinitionId = null,
            List<string> stageReservationKeys = null)
        {
            this.kind = kind;
            this.durationGameSeconds = Math.Max(0f, durationGameSeconds);
            this.tendingIntervalGameSeconds = Math.Max(0f, tendingIntervalGameSeconds);
            this.tendingGraceGameSeconds = Math.Max(0f, tendingGraceGameSeconds);
            this.missedConsequence = missedConsequence;
            this.wasteFraction01 = Mathf.Clamp01(wasteFraction01);
            this.laborTaskDefinitionId = laborTaskDefinitionId ?? string.Empty;
            if (stageReservationKeys != null)
                this.stageReservationKeys = new List<string>(stageReservationKeys);
        }
    }

    /// <summary>
    /// Pass IV: runtime state for one running stage. Plain data; persisted with the
    /// owning process. The worker is null (unassigned) during pure passive stages.
    /// </summary>
    [Serializable]
    public sealed class ProductionStageState
    {
        [SerializeField]
        private int stageIndex;

        [SerializeField]
        private ProductionStageKind kind = ProductionStageKind.ActiveLabor;

        /// <summary>Absolute game seconds the stage started (TimeManager timeline).</summary>
        [SerializeField]
        private float startGameSeconds;

        /// <summary>Absolute game seconds the stage completes (or the next tending is due).</summary>
        [SerializeField]
        private float dueGameSeconds;

        /// <summary>Absolute game seconds after which a missed tending applies consequences.</summary>
        [SerializeField]
        private float deadlineGameSeconds;

        [SerializeField]
        private bool tendingDone;

        [SerializeField]
        private bool consequenceApplied;

        /// <summary>WorkTask EntityId string for ActiveLabor stages (empty when none).</summary>
        [SerializeField]
        private string laborTaskId = string.Empty;

        /// <summary>Reservation id holding stage resources (empty when none).</summary>
        [SerializeField]
        private string reservationId = string.Empty;

        public int StageIndex => stageIndex;
        public ProductionStageKind Kind => kind;
        public float StartGameSeconds => startGameSeconds;
        public float DueGameSeconds => dueGameSeconds;
        public float DeadlineGameSeconds => deadlineGameSeconds;
        public bool TendingDone => tendingDone;
        public bool ConsequenceApplied => consequenceApplied;
        public string LaborTaskId => laborTaskId;
        public string ReservationId => reservationId;

        public ProductionStageState()
        {
        }

        public void Begin(int stageIndex, ProductionStageDefinition definition, float startGameSeconds)
        {
            this.stageIndex = stageIndex;
            kind = definition != null ? definition.Kind : ProductionStageKind.ActiveLabor;
            this.startGameSeconds = startGameSeconds;
            float duration = definition != null ? definition.DurationGameSeconds : 60f;
            dueGameSeconds = startGameSeconds + duration;
            float grace = definition != null ? definition.TendingGraceGameSeconds : 0f;
            deadlineGameSeconds = dueGameSeconds + grace;
            tendingDone = false;
            consequenceApplied = false;
        }

        public void MarkTendingDone() { tendingDone = true; }
        public void MarkConsequenceApplied() { consequenceApplied = true; }
        public void SetLaborTask(string taskId) { laborTaskId = taskId ?? string.Empty; }
        public void SetReservation(string reservationId) { this.reservationId = reservationId ?? string.Empty; }
    }
}
