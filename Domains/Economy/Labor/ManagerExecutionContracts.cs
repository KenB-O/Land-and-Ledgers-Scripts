using System;
using System.Collections.Generic;

namespace LandLedgers.Economy.Labor
{
    /// <summary>
    /// Pass VII Workstream D: the outcome of executing one manager decision.
    /// Exactly one of Executed, Pending, or Failed must be true.
    /// </summary>
    [Serializable]
    public sealed class ManagerExecutionOutcome
    {
        public bool Executed;
        public bool Pending;
        public bool Failed;
        public string Reason = string.Empty;
        public string ResultingReference = string.Empty;
    }

    /// <summary>
    /// The authority context a manager decision executes under.
    /// </summary>
    [Serializable]
    public sealed class ManagerExecutionContext
    {
        public string BusinessInstanceId = string.Empty;
        public int ManagerPersonId = -1;
        public int DayIndex = -1;
        public int PurchaseLimitCents;
        public int MaxBorrowingCents;
        public List<string> DelegatedScopes = new List<string>();
    }

    /// <summary>
    /// Executes one ManagerBrain decision kind through real production
    /// authorities. A decision must execute, become a legitimate pending
    /// request, escalate to the owner, or fail with a reason. A decision
    /// silently disappearing is not acceptable. Retries must be idempotent:
    /// no duplicate procurement orders, no duplicate price changes.
    /// </summary>
    public interface IManagerDecisionExecutor
    {
        /// <summary>One of ManagerActKinds (OpenRecruitment, Schedule, Procure, ...).</summary>
        string DecisionKind { get; }

        ManagerExecutionOutcome Execute(
            ManagerDecisionResult decision,
            ManagerExecutionContext context,
            List<string> diagnostics);
    }
}
