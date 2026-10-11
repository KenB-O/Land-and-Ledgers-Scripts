using System;
using System.Collections.Generic;
using LandLedgers.Economy;
using LandLedgers.Economy.Labor;
using LandLedgers.Population;

namespace LandLedgers.Reporting
{
    /// <summary>
    /// Worker C (Pass III): builds the LivelihoodReadModel from ACTUAL state.
    ///
    /// Every field derives from a real authority — never fabricated as UI text:
    ///   CurrentWorkDescription / EmploymentState -> EmploymentRelationshipRegistry
    ///   SeekingReason                          -> the household's own recorded
    ///                                             SeekIncome decision reason
    ///   CurrentActivity                        -> PersonScheduleTracker
    ///   CapabilitySummary                      -> Worker D's capability stores
    ///                                             via an optional provider;
    ///                                             honest placeholder when absent
    ///   ExpectedConsequence                    -> the actual negotiated wage terms
    ///
    /// Privacy rule (Canon XXXI Part IV §4.4 — known information is evidence):
    /// the player sees their own household fully; another household's person is
    /// shown fully only to a linked observer (same household or a documented
    /// contact-graph link). Strangers see masked placeholders — NPC private
    /// information is never disclosed automatically.
    /// </summary>
    public sealed class LivelihoodReadModelBuilder
    {
        private readonly EmploymentRelationshipRegistry employments;
        private readonly PersonScheduleTracker schedules;
        private readonly ContactGraphService contactGraph;
        private readonly HouseholdMembershipRegistry memberships;
        private readonly HouseholdDecisionService decisions;

        /// <summary>
        /// Optional: descriptive capability summary for a person (Worker D).
        /// Null = capability integration not yet wired; the model says so honestly.
        /// </summary>
        public Func<int, string> CapabilitySummaryProvider;

        public LivelihoodReadModelBuilder(
            EmploymentRelationshipRegistry employments,
            PersonScheduleTracker schedules,
            ContactGraphService contactGraph,
            HouseholdMembershipRegistry memberships,
            HouseholdDecisionService decisions)
        {
            this.employments = employments;
            this.schedules = schedules;
            this.contactGraph = contactGraph;
            this.memberships = memberships;
            this.decisions = decisions;
        }

        public LivelihoodReadModel Build(int personId, int asOfDay, int asOfMinuteOfDay,
            int observerPersonId, List<string> diag)
        {
            var model = new LivelihoodReadModel
            {
                PersonId = "P" + personId,
                AsOfDay = asOfDay,
            };

            int householdId = -1;
            if (memberships != null)
            {
                HouseholdMembership membership = memberships.GetActiveMembership(personId);
                if (membership != null) householdId = membership.HouseholdId;
            }
            model.HouseholdId = householdId >= 0 ? householdId.ToString() : string.Empty;

            bool visible = IsVisibleTo(personId, householdId, observerPersonId);
            if (!visible)
            {
                model.CurrentWorkDescription = "not known to observer";
                model.EmploymentState = "not known to observer";
                model.SeekingReason = null;
                model.CurrentActivity = "not known to observer";
                model.CapabilitySummary = "not known to observer";
                model.ExpectedConsequence = "not known to observer";
                if (diag != null) diag.Add("LivelihoodReadModel: P" + personId + " masked for observer P" + observerPersonId + " (no legitimate knowledge)");
                return model;
            }

            // Employment: the actual registry, descriptive only.
            List<EmploymentRelationship> active = employments != null
                ? employments.GetActiveByEmployee(personId)
                : new List<EmploymentRelationship>();
            if (active.Count > 0)
            {
                var parts = new List<string>();
                foreach (EmploymentRelationship rel in active)
                {
                    if (rel == null) continue;
                    string role = string.IsNullOrWhiteSpace(rel.RoleDisplayName) ? "worker" : rel.RoleDisplayName;
                    parts.Add(role + " at " + rel.EmployerBusinessId);
                }
                model.CurrentWorkDescription = string.Join("; ", parts.ToArray());
                model.EmploymentState = "Accepted, active";
            }
            else
            {
                model.CurrentWorkDescription = "no employment";
                model.EmploymentState = "Not employed";
            }

            // Seeking: the household's own recorded decision — the real reason,
            // not a fabricated motive.
            if (decisions != null && householdId >= 0)
            {
                HouseholdDecision last = decisions.LastDecisionFor(householdId.ToString());
                if (last != null && last.Kind == HouseholdDecisionKind.SeekIncome)
                {
                    model.EmploymentState = active.Count > 0 ? "Accepted, active; seeking more" : "Seeking";
                    model.SeekingReason = last.Reason;
                }
            }

            model.CurrentActivity = schedules != null
                ? schedules.DescribeActivity(personId, asOfDay, asOfMinuteOfDay)
                : "schedule unavailable";

            model.CapabilitySummary = CapabilitySummaryProvider != null
                ? (CapabilitySummaryProvider(personId) ?? "no capability summary available")
                : "capability summary not yet integrated";

            if (active.Count > 0)
            {
                int wage = 0;
                foreach (EmploymentRelationship rel in active)
                    if (rel != null) wage = Math.Max(wage, rel.Compensation.AgreedWeeklyWageCents);
                model.ExpectedConsequence = string.Format(
                    "legitimate wage income up to {0}c/week after eligible work, per the actual agreement", wage);
            }
            else if (model.SeekingReason != null)
            {
                model.ExpectedConsequence = "income depends on securing work";
            }
            else
            {
                model.ExpectedConsequence = "no expected employment income";
            }

            return model;
        }

        private bool IsVisibleTo(int personId, int householdId, int observerPersonId)
        {
            if (observerPersonId == personId) return true;
            if (observerPersonId < 0) return false; // unknown observer: masked
            if (memberships != null && householdId >= 0)
            {
                HouseholdMembership observerMembership = memberships.GetActiveMembership(observerPersonId);
                if (observerMembership != null && observerMembership.HouseholdId == householdId)
                    return true;
            }
            // Documented contact: the observer legitimately knows this person.
            if (contactGraph != null && contactGraph.HasLink(observerPersonId, personId))
                return true;
            return false;
        }
    }
}
