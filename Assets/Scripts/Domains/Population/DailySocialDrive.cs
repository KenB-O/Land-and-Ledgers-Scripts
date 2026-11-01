// Pass IX (A2-06 + A2-07): the production daily social drive.
//
// Pass VII/VIII built the conversation/knowledge stack (SocialVisitPlanner,
// ConversationOpportunityService, ConversationRunner, KnowledgeTransmission
// Store, WorkerJobSecurityEvaluator) but nothing in the live game ever
// called it: the knowledge store stayed empty forever and every
// social-evidence consumer read an empty set. This drive is the missing
// production producer, called daily from SimulationDrivers:
//
//   1. Social visits: a deterministic subset of living, on-map adults plan
//      an evening visit at a real venue business through SocialVisitPlanner
//      (real person-time, refused loudly when the person is busy).
//   2. Conversations: persons co-located at the same venue talk through
//      ConversationRunner — which re-checks the legitimate channel itself.
//      What they can say is only what they honestly hold:
//        - a packet they actually received (relayed, confidence never
//          improving, chain extended — InformationPacketFactory.Relay), or
//        - a firsthand observation grounded in real registry state: the
//          speaker was personally dismissed from, or missed payroll at, an
//          employer (layoff risk they lived, not a rumor invented here).
//      Nothing else is fabricated: a person with nothing to tell says
//      nothing, and the store's duplicate refusal keeps retellings from
//      manufacturing independent sources.
//   3. Job security (A2-07): every employed person is evaluated daily via
//      WorkerJobSecurityEvaluator against their own employer. A
//      corroborated SeekWork decision routes into the real recruitment
//      path — a CandidateInquiry at an open effort of ANOTHER business —
//      and reserves JobSeeking person-time. An uncorroborated rumor moves
//      nobody, exactly as the evaluator decides.
using System;
using System.Collections.Generic;
using LandLedgers.Economy;
using LandLedgers.Economy.Recruitment;

namespace LandLedgers.Population
{
    /// <summary>Inputs for one day of the production social drive.</summary>
    public sealed class DailySocialDriveContext
    {
        public PopulationState Population;
        public PersonScheduleTracker Schedules;
        public EmploymentRelationshipRegistry Employments;
        public KnowledgeTransmissionStore Knowledge;
        /// <summary>Optional: job-security routing target (real applications).</summary>
        public RecruitmentService Recruiting;
        /// <summary>Business lookup for the conversation opportunity service.</summary>
        public Func<string, BusinessInstanceState> BusinessById;
        /// <summary>Real venue business ids (e.g. the saloon). Empty = no visits.</summary>
        public List<string> VenueBusinessIds = new List<string>();
        public int DayIndex;
        public int WorldSeed;
    }

    /// <summary>What one social day actually did (read model for diagnostics/tests).</summary>
    public sealed class DailySocialDayReport
    {
        public int VisitsPlanned;
        public int ConversationsHeld;
        public int PacketsRecorded;
        public int WorkersEvaluated;
        public int SeekWorkDecisions;
        public int InquiriesFiled;
    }

    /// <summary>Pass IX: the daily social drive (see file header).</summary>
    public static class DailySocialDrive
    {
        /// <summary>Evening visit window: 19:00 for one hour.</summary>
        public const int VisitStartMinuteOfDay = 1140;
        public const int VisitDurationMinutes = 60;
        /// <summary>Daily probability an eligible adult plans a social visit (calibration).</summary>
        public const double DailyVisitProbability = 0.30;

        public static DailySocialDayReport DriveDay(DailySocialDriveContext ctx, List<string> diag)
        {
            var report = new DailySocialDayReport();
            diag = diag ?? new List<string>();
            if (ctx == null || ctx.Population == null || ctx.Population.people == null
                || ctx.Schedules == null || ctx.Knowledge == null)
            {
                diag.Add("DailySocialDrive: missing population, schedules, or knowledge store — social day skipped.");
                return report;
            }

            PlanVisits(ctx, report, diag);
            HoldConversations(ctx, report, diag);
            EvaluateJobSecurity(ctx, report, diag);
            return report;
        }

        // ---------- 1. visits ----------

        private static void PlanVisits(DailySocialDriveContext ctx, DailySocialDayReport report, List<string> diag)
        {
            if (ctx.VenueBusinessIds == null || ctx.VenueBusinessIds.Count == 0)
            {
                return;
            }

            foreach (PersonState person in ctx.Population.people)
            {
                if (!IsEligibleAdult(ctx, person))
                {
                    continue;
                }

                double draw = DemographicRng.Draw01(ctx.WorldSeed, ctx.DayIndex, person.id, "social-visit");
                if (draw >= DailyVisitProbability)
                {
                    continue;
                }

                int venueIndex = (int)(DemographicRng.Draw01(ctx.WorldSeed, ctx.DayIndex, person.id, "social-venue")
                    * ctx.VenueBusinessIds.Count) % ctx.VenueBusinessIds.Count;
                string venue = ctx.VenueBusinessIds[venueIndex];
                string refusal = SocialVisitPlanner.PlanSocialVisit(
                    person.id, venue, ctx.DayIndex, VisitStartMinuteOfDay, VisitDurationMinutes,
                    SocialVisitMotivation.Companionship, ctx.Schedules, diag);
                if (refusal == null)
                {
                    report.VisitsPlanned++;
                }
            }
        }

        private static bool IsEligibleAdult(DailySocialDriveContext ctx, PersonState person)
        {
            return person != null
                && person.deathDayIndex < 0
                && !person.isOffMap
                && person.ageBand == AgeBand.Adult18Plus;
        }

        // ---------- 2. conversations ----------

        private static void HoldConversations(DailySocialDriveContext ctx, DailySocialDayReport report, List<string> diag)
        {
            var opportunities = new ConversationOpportunityService(
                ctx.Schedules,
                personId => ctx.Population.GetPerson(personId),
                ctx.Employments,
                ctx.BusinessById);

            // Group today's social visitors by venue from real reservations.
            var visitorsByVenue = new Dictionary<string, List<int>>(StringComparer.Ordinal);
            foreach (PersonState person in ctx.Population.people)
            {
                if (person == null || person.deathDayIndex >= 0 || person.isOffMap)
                {
                    continue;
                }

                foreach (PersonActivityReservation reservation in ctx.Schedules.ActiveFor(person.id, ctx.DayIndex))
                {
                    if (reservation == null || reservation.Activity != PersonActivityKind.Socializing)
                    {
                        continue;
                    }

                    if (!SocialVisitPlanner.TryParseSocialLabel(reservation.Label, out string venue, out _))
                    {
                        continue;
                    }

                    if (!visitorsByVenue.TryGetValue(venue, out List<int> visitors))
                    {
                        visitors = new List<int>();
                        visitorsByVenue[venue] = visitors;
                    }

                    if (!visitors.Contains(person.id))
                    {
                        visitors.Add(person.id);
                    }
                }
            }

            foreach (KeyValuePair<string, List<int>> entry in visitorsByVenue)
            {
                List<int> visitors = entry.Value;
                visitors.Sort();
                for (int s = 0; s < visitors.Count; s++)
                {
                    for (int l = 0; l < visitors.Count; l++)
                    {
                        if (s == l)
                        {
                            continue;
                        }

                        TryConverse(ctx, opportunities, visitors[s], visitors[l], report, diag);
                    }
                }
            }
        }

        private static void TryConverse(
            DailySocialDriveContext ctx, ConversationOpportunityService opportunities,
            int speakerId, int listenerId, DailySocialDayReport report, List<string> diag)
        {
            if (ctx.Employments == null)
            {
                return;
            }

            // (a) Relay something the speaker actually holds that the
            // listener does not already hold from the same chain root.
            foreach (InformationPacket held in ctx.Knowledge.PacketsFor(speakerId, null))
            {
                if (held == null)
                {
                    continue;
                }

                // Never tell the originator their own telling back.
                if (string.Equals(KnowledgeTransmissionStore.ChainRoot(held), listenerId.ToString(), StringComparison.Ordinal))
                {
                    continue;
                }

                if (ListenerAlreadyHolds(ctx, listenerId, held))
                {
                    continue;
                }

                InformationPacket relay = InformationPacketFactory.Relay(
                    held, speakerId, listenerId, ctx.DayIndex, held.Confidence01);
                if (relay == null)
                {
                    continue;
                }

                int before = ctx.Knowledge.PacketCount;
                string refusal = ConversationRunner.Converse(
                    opportunities, ctx.Knowledge, speakerId, listenerId, ctx.DayIndex, relay, diag);
                if (refusal == null)
                {
                    report.ConversationsHeld++;
                    report.PacketsRecorded += ctx.Knowledge.PacketCount - before;
                    return; // one telling per ordered pair per day
                }
            }

            // (b) Firsthand: the speaker personally lived layoffs at an
            // employer (dismissed, or payroll missed and employment ended /
            // suspended for it). Only that employer's current workers are
            // told — the telling is about their shared workplace risk.
            foreach (EmploymentRelationship employment in ctx.Employments.GetByEmployee(speakerId))
            {
                if (employment == null || string.IsNullOrWhiteSpace(employment.EmployerBusinessId))
                {
                    continue;
                }

                bool livedLayoff = employment.EndReason == EmploymentEndReason.Dismissed
                    || employment.EndReason == EmploymentEndReason.MissedPayroll
                    || employment.SuspensionReason == EmploymentSuspensionReason.EmployerPaymentDefault;
                if (!livedLayoff)
                {
                    continue;
                }

                string employer = employment.EmployerBusinessId;
                bool listenerWorksThere = false;
                foreach (EmploymentRelationship listenerJob in ctx.Employments.GetActiveByEmployee(listenerId))
                {
                    if (listenerJob != null
                        && string.Equals(listenerJob.EmployerBusinessId, employer, StringComparison.Ordinal))
                    {
                        listenerWorksThere = true;
                        break;
                    }
                }

                if (!listenerWorksThere)
                {
                    continue;
                }

                string subject = SocialEvidenceEvaluator.LayoffRiskSubject(employer);
                if (ListenerAlreadyHoldsSubjectFromRoot(ctx, listenerId, subject, speakerId))
                {
                    continue;
                }

                InformationPacket observed = InformationPacketFactory.Observe(
                    subject,
                    $"{employer} dismissed workers, including me",
                    speakerId, ctx.DayIndex, 0.9f);
                int before = ctx.Knowledge.PacketCount;
                string refusal = ConversationRunner.Converse(
                    opportunities, ctx.Knowledge, speakerId, listenerId, ctx.DayIndex, observed, diag);
                if (refusal == null)
                {
                    report.ConversationsHeld++;
                    report.PacketsRecorded += ctx.Knowledge.PacketCount - before;
                    return;
                }
            }
        }

        private static bool ListenerAlreadyHolds(
            DailySocialDriveContext ctx, int listenerId, InformationPacket packet)
        {
            string root = KnowledgeTransmissionStore.ChainRoot(packet);
            foreach (InformationPacket held in ctx.Knowledge.PacketsFor(listenerId, packet.Subject))
            {
                if (held != null
                    && string.Equals(held.ContentSummary, packet.ContentSummary, StringComparison.Ordinal)
                    && string.Equals(KnowledgeTransmissionStore.ChainRoot(held), root, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool ListenerAlreadyHoldsSubjectFromRoot(
            DailySocialDriveContext ctx, int listenerId, string subject, int rootPersonId)
        {
            foreach (InformationPacket held in ctx.Knowledge.PacketsFor(listenerId, subject))
            {
                if (held != null
                    && string.Equals(KnowledgeTransmissionStore.ChainRoot(held), rootPersonId.ToString(), StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        // ---------- 3. job security (A2-07) ----------

        private static void EvaluateJobSecurity(DailySocialDriveContext ctx, DailySocialDayReport report, List<string> diag)
        {
            if (ctx.Employments == null)
            {
                return;
            }

            foreach (PersonState person in ctx.Population.people)
            {
                if (person == null || person.deathDayIndex >= 0 || person.isOffMap)
                {
                    continue;
                }

                foreach (EmploymentRelationship employment in ctx.Employments.GetActiveByEmployee(person.id))
                {
                    if (employment == null || string.IsNullOrWhiteSpace(employment.EmployerBusinessId))
                    {
                        continue;
                    }

                    JobSecurityDecision decision = WorkerJobSecurityEvaluator.Evaluate(
                        person.id, employment.EmployerBusinessId, ctx.Knowledge, ctx.Employments, diag);
                    if (decision == null)
                    {
                        continue;
                    }

                    report.WorkersEvaluated++;
                    if (!decision.SeekWork)
                    {
                        continue;
                    }

                    report.SeekWorkDecisions++;
                    diag.Add($"DailySocialDrive: P{person.id} seeks other work — {decision.Reason}.");
                    RouteSeekWork(ctx, person, employment.EmployerBusinessId, decision, report, diag);
                }
            }
        }

        /// <summary>
        /// Routes a SeekWork decision into reality: reserves JobSeeking
        /// person-time tomorrow and files a real CandidateInquiry at the
        /// first open recruitment effort of another business, when one
        /// exists. No open effort, no application — the decision stays a
        /// decision (the inquiry is never fabricated).
        /// </summary>
        private static void RouteSeekWork(
            DailySocialDriveContext ctx, PersonState person, string currentEmployer,
            JobSecurityDecision decision, DailySocialDayReport report, List<string> diag)
        {
            // Real person-time: a job-seeking block tomorrow morning on the
            // person's own schedule (refused loudly if their time is taken).
            string refusal = ctx.Schedules.TryReserve(
                person.id, PersonActivityKind.JobSeeking, ctx.DayIndex + 1,
                540, 120, "job seeking (corroborated layoff risk)");
            if (refusal != null)
            {
                diag.Add($"DailySocialDrive: P{person.id} job-seeking time refused — {refusal}");
            }

            if (ctx.Recruiting == null)
            {
                return;
            }

            foreach (RecruitmentEffort effort in ctx.Recruiting.Efforts)
            {
                if (effort == null || effort.Closed)
                {
                    continue;
                }

                if (string.Equals(effort.BusinessInstanceId, currentEmployer, StringComparison.Ordinal))
                {
                    continue; // already works there — seeking means elsewhere
                }

                CandidateInquiry inquiry = ctx.Recruiting.ApplyForEffort(
                    effort.EffortId, person.id, ctx.Population, ctx.Employments,
                    $"word of mouth: {decision.Reason}", ctx.DayIndex, diag);
                if (inquiry != null)
                {
                    report.InquiriesFiled++;
                    diag.Add($"DailySocialDrive: P{person.id} applied to '{effort.BusinessInstanceId}' " +
                        $"(effort {effort.EffortId}) after corroborated layoff risk at '{currentEmployer}'.");
                    return;
                }
            }
        }
    }
}
