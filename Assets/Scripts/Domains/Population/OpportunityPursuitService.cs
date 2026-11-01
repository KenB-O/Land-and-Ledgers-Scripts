using System;
using System.Collections.Generic;
using LandLedgers.Economy;
using LandLedgers.Economy.Labor;
using LandLedgers.Economy.Recruitment;

namespace LandLedgers.Population
{
    /// <summary>
    /// Pass VIII Worker 7: closes the first missing connection in the
    /// person-decision cycle — household circumstances → person decisions →
    /// ACTUAL WORK.
    ///
    /// HouseholdDecisionDriver.ExecuteSeekIncome discovers openings through
    /// the household's documented contacts and records them in the
    /// HouseholdOpportunityLog. Until now nothing ever acted on that log: a
    /// person could hear of work forever and never apply. This service turns
    /// unpursued log entries into real person-initiated job applications
    /// (RecruitmentService.ApplyForOpening).
    ///
    /// What it does and does not do:
    /// - Picks one eligible applicant per opportunity: the lowest-id active
    ///   household member who is a living adult with labor access and no
    ///   active employment. Children, the dead, and the already-employed
    ///   never apply.
    /// - Applies only to a genuinely OPEN hiring effort at the business the
    ///   household heard about. No open effort → no application (the entry
    ///   stays unpursued and is retried another day; nothing is invented).
    /// - Files an inquiry, never an employment: hiring stays the employer's
    ///   decision through HireFromInquiry. No automatic job from a mere
    ///   discovery.
    /// - One attempt per opportunity: a filed application marks the entry
    ///   pursued. A refused application (e.g. the effort closed mid-day)
    ///   also marks it pursued — re-applying daily to the same opening is
    ///   noise, not search; a NEW discovery re-opens the trail.
    /// - Person-time honesty: applying is real job-seeking. The service does
    ///   not reserve schedule time itself; the daily driver plans JobSeeking
    ///   blocks through DailyActivityPlanner, and this service is the work
    ///   those blocks do.
    /// </summary>
    public sealed class OpportunityPursuitService
    {
        /// <summary>All per-call inputs. Null registries mean no pursuit.</summary>
        public sealed class PursuitContext
        {
            public HouseholdOpportunityLog OpportunityLog;
            public HouseholdMembershipRegistry Memberships;
            public PopulationState Population;
            public EmploymentRelationshipRegistry Employments;
            public RecruitmentService Recruitment;
        }

        private readonly List<string> diagnostics = new List<string>();
        public IReadOnlyList<string> Diagnostics => diagnostics;

        /// <summary>
        /// Files applications for one household's unpursued opportunities.
        /// Returns the number of applications filed. Deterministic.
        /// </summary>
        public int PursueForHousehold(int householdId, int dayIndex, PursuitContext ctx, List<string> diag)
        {
            diag = diag ?? diagnostics;
            if (ctx == null || ctx.OpportunityLog == null || ctx.Memberships == null
                || ctx.Population == null || ctx.Recruitment == null)
            {
                return 0;
            }

            List<DiscoveredOpportunity> entries = ctx.OpportunityLog.GetForHousehold(householdId);
            if (entries.Count == 0)
            {
                return 0;
            }

            List<int> members = ctx.Memberships.GetActiveMembers(householdId);
            int filed = 0;
            foreach (DiscoveredOpportunity entry in entries)
            {
                if (entry == null || entry.Pursued || string.IsNullOrWhiteSpace(entry.BusinessId))
                {
                    continue;
                }
                int applicant = FindApplicant(members, ctx);
                if (applicant < 0)
                {
                    continue; // nobody eligible today; the entry waits
                }
                List<RecruitmentEffort> open = ctx.Recruitment.OpenEffortsForBusiness(entry.BusinessId);
                if (open.Count == 0)
                {
                    continue; // nothing open to apply to; the entry waits
                }
                RecruitmentEffort effort = open[0];
                CandidateInquiry inquiry = ctx.Recruitment.ApplyForOpening(
                    effort.EffortId, applicant, ctx.Population, ctx.Employments,
                    dayIndex, "heard of the opening: " + entry.Provenance, diag);
                // One attempt per opportunity, whatever the outcome.
                ctx.OpportunityLog.MarkPursued(householdId, entry.BusinessId);
                if (inquiry != null)
                {
                    filed++;
                    diag.Add($"OpportunityPursuit: H{householdId} P{applicant} applied to " +
                        $"'{effort.RoleDisplayName}' at {entry.BusinessId} (inquiry {inquiry.InquiryId}).");
                }
                else
                {
                    diag.Add($"OpportunityPursuit: H{householdId} P{applicant} could not apply to " +
                        $"{entry.BusinessId} — opportunity marked pursued, no application filed.");
                }
            }
            return filed;
        }

        /// <summary>
        /// Lowest-id active member who may apply: living adult with labor
        /// access and no active employment. -1 when nobody qualifies.
        /// </summary>
        private int FindApplicant(List<int> members, PursuitContext ctx)
        {
            if (members == null)
            {
                return -1;
            }
            foreach (int memberId in members)
            {
                PersonState person = ctx.Population.GetPerson(memberId);
                if (person == null || person.deathDayIndex >= 0)
                {
                    continue;
                }
                if (person.ageBand != AgeBand.Adult18Plus || person.laborAccessLevel == LaborAccessLevel.None)
                {
                    continue;
                }
                bool employed = false;
                if (ctx.Employments != null)
                {
                    foreach (EmploymentRelationship rel in ctx.Employments.GetActiveByEmployee(memberId))
                    {
                        employed = true;
                        break;
                    }
                }
                if (employed)
                {
                    continue;
                }
                return memberId;
            }
            return -1;
        }
    }
}
