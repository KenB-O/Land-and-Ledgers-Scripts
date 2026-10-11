// Shared data contracts for the Autonomous Labor pass (Phase A–G).
// Integrator-owned. All labor services code against these shapes so the four
// implementation workers can build in parallel without cross-worker type drift.
// Dependency-free: System + System.Collections.Generic only. No domain types.
using System;
using System.Collections.Generic;

namespace LandLedgers.Economy.Labor
{
    // ---- Employment negotiation (Worker B) ----

    /// <summary>
    /// Lifecycle of a negotiated employment offer. Terminal states: Accepted,
    /// Refused, Withdrawn, Expired.
    /// </summary>
    public enum OfferState
    {
        Draft = 0,
        Offered = 1,
        Countered = 2,
        Accepted = 3,
        Refused = 4,
        Withdrawn = 5,
        Expired = 6
    }

    /// <summary>
    /// A negotiated employment offer. Employment NEVER grants ownership or
    /// residential rights; housing/board is a linked agreement note only.
    /// </summary>
    [Serializable]
    public sealed class EmploymentOffer
    {
        public string OfferId;
        public string BusinessInstanceId;
        public int CandidatePersonId;
        public int OfferingPersonId; // person extending the offer (owner or delegated manager), -1 if unknown
        public int WageCentsPerWeek;
        public string Duties;
        public string ScheduleText;
        public int StartDayIndex;
        public int TrialDays;
        public string WorkLocation;
        public int DurationDays; // 0 = indefinite
        public string HousingNote; // linked-agreement note only; never a property transfer
        public string AuthorityScope; // duties/authority the role carries
        public string Conditions;
        public int TerminationNoticeDays;
        public OfferState State;
        public int CreatedDayIndex;
        public int ExpiresDayIndex;
        public string DeliveryChannel; // InPerson, Letter, Handbill, Newspaper, WordOfMouth
        public string HowHeard; // relationship provenance chain, e.g. "spouse-of:P3>referred-by:P7"
        public int CounterWageCentsPerWeek; // latest counter terms, 0 when none
        public string LastNote;
    }

    // ---- Person job decisions (Worker C) ----

    public enum JobDecisionKind
    {
        Accept = 0,
        Refuse = 1,
        Counter = 2,
        Delay = 3
    }

    /// <summary>
    /// Everything a person legitimately knows when evaluating an offer.
    /// Callers must only fill fields from legitimately-known information
    /// (visible profile, contact graph, public notices) — never hidden traits.
    /// </summary>
    [Serializable]
    public sealed class JobDecisionInput
    {
        public int PersonId;
        public int HouseholdId;
        public int ReservesCents; // household reserves the person can draw on
        public int Dependents;
        public int CurrentWageCentsPerWeek; // 0 when unemployed
        public bool CurrentlyEmployed;
        public int OfferWageCentsPerWeek;
        public int CommuteMinutesOneWay;
        public float ConditionsScore01; // 0 = harsh/dangerous, 1 = fair/safe
        public float EmployerReliability01; // 0 = unknown/unreliable, 1 = proven reliable
        public bool HousingOffered;
        public int KnownAlternativeCount; // other opportunities the person actually knows about
        public int ReservesTrendCentsPerDay; // negative = declining
        public int DayIndex;
    }

    [Serializable]
    public sealed class JobDecision
    {
        public JobDecisionKind Kind;
        public int CounterWageCentsPerWeek; // set when Kind == Counter
        public string Reason; // human-readable, e.g. "commute too long for wage"
    }

    // ---- Labor demand (Worker A) ----

    /// <summary>
    /// Caller-supplied, read-only snapshot of real workload. No headcount
    /// targets, no multipliers — raw minutes and counts only.
    /// </summary>
    [Serializable]
    public sealed class LaborDemandInputs
    {
        public string BusinessInstanceId;
        public int DayIndex;
        public Dictionary<string, int> OpenTaskMinutesByFamily = new Dictionary<string, int>();
        public int WaitingCustomers;
        public int RecentBalkCount;
        public int RequiresPersonPhaseMinutes; // production phases needing a person
        public int ActiveEmployeeCount;
        public int AvailableEmployeeMinutes; // summed remaining person-time budget
    }

    [Serializable]
    public sealed class LaborDemandLine
    {
        public string TaskFamily;
        public int OpenMinutes;
        public int WaitingCustomers;
        public int BalkedSales;
    }

    [Serializable]
    public sealed class LaborDemandReport
    {
        public string BusinessInstanceId;
        public int DayIndex;
        public List<LaborDemandLine> Lines = new List<LaborDemandLine>();
        public int TotalUnmetMinutes;
        public bool CoverageGap; // work exists that no available person can cover in time
        public bool HiringPressure; // derived evidence signal (proposal 34), never a quota
        public string EvidenceSummary;
    }

    // ---- Work scheduling (Worker C) ----

    [Serializable]
    public sealed class SchedulerOpenWorkItem
    {
        public string TaskId;
        public string Family;
        public int MinutesRequired;
        public string RequiredEquipment; // may be null/empty
        public string LocationId;
    }

    [Serializable]
    public sealed class SchedulerAvailableWorker
    {
        public int PersonId;
        public int AvailableMinutes;
        public Dictionary<string, float> SkillEvidence = new Dictionary<string, float>(); // family -> 0..1, descriptive only
        public bool Fatigued;
        public string ContractRestrictions; // e.g. "no night work", may be empty
    }

    [Serializable]
    public sealed class SchedulerInputs
    {
        public string BusinessInstanceId;
        public int DayIndex;
        public List<SchedulerOpenWorkItem> OpenWork = new List<SchedulerOpenWorkItem>();
        public List<SchedulerAvailableWorker> Workers = new List<SchedulerAvailableWorker>();
    }

    [Serializable]
    public sealed class WorkAssignment
    {
        public string TaskId;
        public int PersonId; // -1 when unassigned
        public int MinutesCommitted;
        public string Note; // reason when unassigned or adjusted
    }

    [Serializable]
    public sealed class WorkAssignmentReport
    {
        public string BusinessInstanceId;
        public int DayIndex;
        public List<WorkAssignment> Assignments = new List<WorkAssignment>();
        public List<string> UnassignedTaskIds = new List<string>();
    }
}
