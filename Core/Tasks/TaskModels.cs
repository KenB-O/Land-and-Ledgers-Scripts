using System;
using System.Collections.Generic;
using UnityEngine;
using LandLedgers.Primitives;

namespace LandLedgers.Tasks
{
    /// <summary>
    /// TTS-2: lifecycle of a unit of work. Queued -&gt; Assigned -&gt; InProgress -&gt;
    /// Complete. Interrupted returns a task to Queued (work preserved, assignment
    /// released). Failed is terminal; recoverable failures generate rework tasks via
    /// <see cref="WorkTask.FollowUpTaskIds"/> (Tech II §2.7).
    /// </summary>
    public enum TaskStatus
    {
        Queued = 0,
        Assigned = 1,
        InProgress = 2,
        Complete = 3,
        Interrupted = 4,
        Failed = 5,
        Cancelled = 6,
    }

    /// <summary>How a task finished. Quality variation and rework are domain concerns; the authority records the outcome.</summary>
    public enum TaskOutcome
    {
        None = 0,
        Success = 1,
        AcceptableWithVariation = 2,
        RecoverableFailure = 3,
        SeriousFailure = 4,
    }

    /// <summary>Queue ordering. Higher value = served first. Ties break by creation order (FIFO).</summary>
    public enum TaskPriority
    {
        Low = 0,
        Normal = 1,
        High = 2,
        Urgent = 3,
    }

    /// <summary>A material input or output of a task definition (item id + quantity). Data only.</summary>
    [Serializable]
    public sealed class TaskMaterial
    {
        [SerializeField]
        private string itemId;

        [SerializeField]
        private int quantity;

        public string ItemId => itemId;
        public int Quantity => quantity;

        public TaskMaterial()
        {
        }

        public TaskMaterial(string itemId, int quantity)
        {
            this.itemId = itemId ?? string.Empty;
            this.quantity = Math.Max(0, quantity);
        }
    }

    /// <summary>
    /// TTS-2: authored/configured data describing a reusable type of work
    /// (Tech II §2.2). Definitions are DATA — they never encode business-specific
    /// runtime truth (no live prices, no specific business state). Catalogs (TTS-5)
    /// register definitions; the authority executes them.
    /// </summary>
    [Serializable]
    public sealed class TaskDefinition
    {
        [SerializeField]
        private string definitionId;

        [SerializeField]
        private string displayName;

        [SerializeField]
        private List<string> domainTags = new List<string>();

        /// <summary>Base duration in whole minutes, before skill scaling. Minimum 1.</summary>
        [SerializeField]
        private int baseMinutes = 1;

        /// <summary>Skill that scales this task's duration (TTS-3). Null/empty = unskilled timing.</summary>
        [SerializeField]
        private string requiredSkillId;

        [SerializeField]
        private List<string> requiredCapabilityTags = new List<string>();

        [SerializeField]
        private List<string> compatibleLocations = new List<string>();

        [SerializeField]
        private List<string> equipmentClasses = new List<string>();

        [SerializeField]
        private List<TaskMaterial> inputs = new List<TaskMaterial>();

        [SerializeField]
        private List<TaskMaterial> outputs = new List<TaskMaterial>();

        [SerializeField]
        private bool interruptible = true;

        [SerializeField]
        private TaskPriority defaultPriority = TaskPriority.Normal;

        /// <summary>Learning/experience tags (Tech II §2.2) — which skills gain practice from this task (TTS-3).</summary>
        [SerializeField]
        private List<string> learningTags = new List<string>();

        public string DefinitionId => definitionId;
        public string DisplayName => displayName;
        public List<string> DomainTags => domainTags;
        public int BaseMinutes => Math.Max(1, baseMinutes);
        public string RequiredSkillId => requiredSkillId;
        public List<string> RequiredCapabilityTags => requiredCapabilityTags;
        public List<string> CompatibleLocations => compatibleLocations;
        public List<string> EquipmentClasses => equipmentClasses;
        public List<TaskMaterial> Inputs => inputs;
        public List<TaskMaterial> Outputs => outputs;
        public bool Interruptible => interruptible;
        public TaskPriority DefaultPriority => defaultPriority;
        public List<string> LearningTags => learningTags;

        public TaskDefinition()
        {
        }

        public TaskDefinition(string definitionId, string displayName, int baseMinutes)
        {
            this.definitionId = definitionId ?? throw new ArgumentNullException(nameof(definitionId));
            this.displayName = displayName ?? definitionId;
            this.baseMinutes = Math.Max(1, baseMinutes);
        }

        /// <summary>Authoring-time configuration: queue priority hint for tasks of this kind.</summary>
        public void SetDefaultPriority(TaskPriority priority)
        {
            defaultPriority = priority;
        }

        /// <summary>Authoring-time configuration: which skill scales this task's duration.</summary>
        public void SetRequiredSkill(string skillId, IEnumerable<string> learningTags = null)
        {
            requiredSkillId = skillId;
            learningTags.Clear();
            if (learningTags != null)
            {
                this.learningTags.AddRange(learningTags);
            }
        }
    }

    /// <summary>
    /// TTS-2: persistent actual work (Tech II §2.3 — the TaskInstance). This is the
    /// execution side of Tech X §6.4's WorkOrder: it carries the owning customer/
    /// business, target, required capability, location, urgency, materials, labor
    /// estimate, queue priority, assignee, status, terms and completion.
    ///
    /// Identity: an HF-1 <see cref="EntityId"/> of kind <see cref="EntityKind.WorkTask"/>.
    /// Progress is whole minutes against the work-time budget (TTS-1); the authority
    /// persists actuals across save/load per Tech II §2.4 (authoritative time).
    /// </summary>
    [Serializable]
    public sealed class WorkTask
    {
        [SerializeField]
        private EntityId taskId;

        [SerializeField]
        private string definitionId;

        /// <summary>Owning business, household, project or agreement (Tech II §2.3).</summary>
        [SerializeField]
        private EntityId ownerId;

        /// <summary>§6.4 customer on whose behalf the work is done. String ref to avoid domain coupling.</summary>
        [SerializeField]
        private string customerRef;

        [SerializeField]
        private string targetDescription;

        [SerializeField]
        private string requiredCapability;

        /// <summary>
        /// Where the work happens. String location id — placeholder until the
        /// location/journey model lands; travel time is then booked like any task.
        /// </summary>
        [SerializeField]
        private string locationId;

        [SerializeField]
        private TaskPriority priority = TaskPriority.Normal;

        [SerializeField]
        private List<TaskMaterial> reservedMaterials = new List<TaskMaterial>();

        /// <summary>Skill-scaled labor estimate in whole minutes (set at assignment).</summary>
        [SerializeField]
        private int plannedMinutes = 1;

        /// <summary>
        /// True when the planned minutes were set per-instance (e.g. quantity-driven
        /// freight time) rather than taken from the definition. Assignment still
        /// runs the value through the skill estimator.
        /// </summary>
        [SerializeField]
        private bool hasPlannedMinutesOverride;

        [SerializeField]
        private EntityId assigneeId;

        [SerializeField]
        private TaskStatus status = TaskStatus.Queued;

        [SerializeField]
        private TaskOutcome outcome = TaskOutcome.None;

        [SerializeField]
        private int minutesWorked;

        [SerializeField]
        private bool interruptible = true;

        [SerializeField]
        private List<EntityId> prerequisiteTaskIds = new List<EntityId>();

        [SerializeField]
        private List<EntityId> followUpTaskIds = new List<EntityId>();

        /// <summary>
        /// Agreement/payment terms reference (§6.4). Terms are recorded; money moves
        /// only through ledger authorities, never as a side effect of task completion.
        /// </summary>
        [SerializeField]
        private string termsRef;

        [SerializeField]
        private int createdDayIndex;

        [SerializeField]
        private int startedDayIndex = -1;

        [SerializeField]
        private int completedDayIndex = -1;

        [SerializeField]
        private int interruptionCount;

        public EntityId TaskId => taskId;
        public string DefinitionId => definitionId;
        public EntityId OwnerId => ownerId;
        public string CustomerRef => customerRef;
        public string TargetDescription => targetDescription;
        public string RequiredCapability => requiredCapability;
        public string LocationId => locationId;
        public TaskPriority Priority => priority;
        public List<TaskMaterial> ReservedMaterials => reservedMaterials;
        public int PlannedMinutes => Math.Max(1, plannedMinutes);
        public bool HasPlannedMinutesOverride => hasPlannedMinutesOverride;
        public EntityId AssigneeId => assigneeId;
        public bool HasAssignee => assigneeId.Kind != EntityKind.Unspecified;
        public TaskStatus Status => status;
        public TaskOutcome Outcome => outcome;
        public int MinutesWorked => minutesWorked;
        public int MinutesRemaining => Math.Max(0, PlannedMinutes - minutesWorked);
        public bool Interruptible => interruptible;
        public List<EntityId> PrerequisiteTaskIds => prerequisiteTaskIds;
        public List<EntityId> FollowUpTaskIds => followUpTaskIds;
        public string TermsRef => termsRef;
        public int CreatedDayIndex => createdDayIndex;
        public int StartedDayIndex => startedDayIndex;
        public int CompletedDayIndex => completedDayIndex;
        public int InterruptionCount => interruptionCount;
        public bool IsOpen => status == TaskStatus.Queued || status == TaskStatus.Assigned || status == TaskStatus.InProgress;

        public WorkTask()
        {
        }

        internal WorkTask(EntityId taskId, string definitionId, EntityId ownerId, int createdDayIndex)
        {
            if (taskId.Kind != EntityKind.WorkTask)
            {
                throw new ArgumentException("WorkTask requires a WorkTask-kind EntityId.", nameof(taskId));
            }

            this.taskId = taskId;
            this.definitionId = definitionId ?? string.Empty;
            this.ownerId = ownerId;
            this.createdDayIndex = createdDayIndex;
        }

        internal void ApplyDefinitionSnapshot(TaskDefinition definition)
        {
            if (definition == null)
            {
                return;
            }

            targetDescription = definition.DisplayName;
            requiredCapability = definition.RequiredSkillId ?? string.Empty;
            interruptible = definition.Interruptible;
            priority = definition.DefaultPriority;
            plannedMinutes = definition.BaseMinutes;
            foreach (TaskMaterial input in definition.Inputs)
            {
                reservedMaterials.Add(new TaskMaterial(input.ItemId, input.Quantity));
            }
        }

        internal void SetCustomer(string customerRef, string termsRef)
        {
            this.customerRef = customerRef ?? string.Empty;
            this.termsRef = termsRef ?? string.Empty;
        }

        internal void SetPlannedMinutes(int minutes)
        {
            plannedMinutes = Math.Max(1, minutes);
        }

        /// <summary>
        /// Sets an instance-specific planned-minutes base (e.g. freight unload time
        /// from shipment quantity). Survives definition snapshots; skill scaling
        /// still applies at assignment.
        /// </summary>
        internal void SetPlannedMinutesOverride(int minutes)
        {
            plannedMinutes = Math.Max(1, minutes);
            hasPlannedMinutesOverride = true;
        }

        internal void AssignTo(EntityId personId)
        {
            assigneeId = personId;
            status = TaskStatus.Assigned;
        }

        internal void MarkStarted(int dayIndex)
        {
            status = TaskStatus.InProgress;
            if (startedDayIndex < 0)
            {
                startedDayIndex = dayIndex;
            }
        }

        internal void AddWorkedMinutes(int minutes)
        {
            minutesWorked += Math.Max(0, minutes);
        }

        internal void MarkComplete(TaskOutcome outcome, int dayIndex)
        {
            status = TaskStatus.Complete;
            this.outcome = outcome;
            completedDayIndex = dayIndex;
        }

        internal void MarkFailed(TaskOutcome outcome, int dayIndex)
        {
            status = TaskStatus.Failed;
            this.outcome = outcome;
            completedDayIndex = dayIndex;
        }

        internal void MarkInterrupted()
        {
            interruptionCount++;
            status = TaskStatus.Interrupted;
        }

        internal void Requeue()
        {
            assigneeId = default(EntityId);
            status = TaskStatus.Queued;
        }

        internal void MarkCancelled()
        {
            status = TaskStatus.Cancelled;
        }
    }
}
