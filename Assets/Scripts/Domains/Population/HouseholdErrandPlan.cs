using System;
using System.Collections.Generic;
using UnityEngine;

namespace LandLedgers.Population
{
    /// <summary>
    /// Pass V P2-1 contract: one coordinated household errand plan. The household
    /// decides what it needs; exactly one real Person is responsible for each plan;
    /// the plan reserves that person's time through the shared activity authority.
    /// Several members must never independently buy the same goods for the same need.
    /// </summary>
    [Serializable]
    public sealed class HouseholdErrandPlan
    {
        [SerializeField] private string planId = string.Empty;
        [SerializeField] private int householdId;
        [SerializeField] private int responsiblePersonId = -1;
        [SerializeField] private List<string> needIds = new List<string>();
        [SerializeField] private string reservationId = string.Empty;
        [SerializeField] private HouseholdErrandPlanStatus status;

        public string PlanId => planId;
        public int HouseholdId => householdId;
        public int ResponsiblePersonId => responsiblePersonId;
        public IReadOnlyList<string> NeedIds => needIds;
        public string ReservationId => reservationId;
        public HouseholdErrandPlanStatus Status => status;

        public HouseholdErrandPlan(string planId, int householdId)
        {
            this.planId = planId ?? string.Empty;
            this.householdId = householdId;
        }

        public void Assign(int personId, IEnumerable<string> needs, string reservation)
        {
            responsiblePersonId = personId;
            needIds = new List<string>(needs ?? Array.Empty<string>());
            reservationId = reservation ?? string.Empty;
            status = HouseholdErrandPlanStatus.Assigned;
        }

        /// <summary>
        /// Pass XII Worker F (copy-on-load): a faithful copy for the
        /// errand registry's load path. Assign cannot serve as the
        /// copier — it forces status to Assigned, which would corrupt
        /// Proposed, Completed, Failed and Cancelled plans on restore.
        /// Plans are mutated in place (Assign, MarkCompleted,
        /// MarkFailed, Cancel), so the registry must never store the
        /// DTO's plan objects directly.
        /// </summary>
        public HouseholdErrandPlan Copy()
        {
            var copy = new HouseholdErrandPlan(planId, householdId)
            {
                responsiblePersonId = responsiblePersonId,
                needIds = new List<string>(needIds ?? new List<string>()),
                reservationId = reservationId,
                status = status,
            };
            return copy;
        }

        public void MarkCompleted() { status = HouseholdErrandPlanStatus.Completed; }
        public void MarkFailed() { status = HouseholdErrandPlanStatus.Failed; }
        public void Cancel() { status = HouseholdErrandPlanStatus.Cancelled; }
    }

    /// <summary>APPEND-ONLY.</summary>
    public enum HouseholdErrandPlanStatus
    {
        Proposed = 0,
        Assigned = 1,
        Completed = 2,
        Failed = 3,
        Cancelled = 4,
    }
}
