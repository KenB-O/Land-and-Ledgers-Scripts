// Pass VIII worker 8: a worker's employment-security decision, driven by
// social knowledge through the ordinary information authorities.
//
// The flagship foreman chain must not remain a one-off. This is the
// generalized worker side: a miner hears of layoffs at their employer
// through a legitimate conversation, and the ordinary employment decision
// consumes the packet. The evaluator reads ONLY the packets the worker
// actually received (SocialEvidenceEvaluator); a worker who heard nothing
// decides on nothing. Corroborated layoff risk (a firsthand telling from
// someone directly affected, or two independent sources) moves the worker
// to seek other work. A single uncorroborated rumor is noted and does not
// move the decision: the worker does not quit, does not panic, and the
// rumor is not treated as fact.
//
// The decision is returned, not executed: the caller (the daily driver in
// production, the test in the harness) performs the real consequence —
// planning JobSeeking time through the schedule tracker and submitting a
// real inquiry through the recruitment service.
using System;
using System.Collections.Generic;
using LandLedgers.Economy;

namespace LandLedgers.Population
{
    /// <summary>
    /// The outcome of one worker's employment-security evaluation.
    /// </summary>
    public sealed class JobSecurityDecision
    {
        /// <summary>True when the worker should seek other work now.</summary>
        public bool SeekWork;
        public string Reason = string.Empty;
        public SocialEvidenceAssessment Evidence;
    }

    /// <summary>
    /// Decides whether a worker seeks other work, from the social evidence
    /// they actually hold about layoff risk at their employer. Ordinary
    /// authority: no omniscience, no invented sources, no rumor-driven
    /// panic.
    /// </summary>
    public static class WorkerJobSecurityEvaluator
    {
        /// <summary>
        /// Evaluates one worker's employment security at one employer.
        /// Returns null (with a diagnostic) only when the evaluation cannot
        /// run honestly: unknown worker, unknown employer, or no active
        /// employment there. A worker with no layoff-risk packets receives a
        /// stay decision: nothing heard, nothing decided.
        /// </summary>
        public static JobSecurityDecision Evaluate(
            int workerPersonId,
            string employerBusinessId,
            IKnowledgeTransmissionStore knowledge,
            EmploymentRelationshipRegistry employments,
            List<string> diagnostics)
        {
            diagnostics = diagnostics ?? new List<string>();
            if (workerPersonId < 0 || string.IsNullOrWhiteSpace(employerBusinessId))
            {
                diagnostics.Add("WorkerJobSecurity: no worker or no employer named — no evaluation.");
                return null;
            }

            bool activelyEmployed = false;
            if (employments != null)
            {
                foreach (EmploymentRelationship relationship in employments.GetActiveByEmployee(workerPersonId))
                {
                    if (relationship != null
                        && string.Equals(relationship.EmployerBusinessId, employerBusinessId, StringComparison.Ordinal))
                    {
                        activelyEmployed = true;
                        break;
                    }
                }
            }
            if (!activelyEmployed)
            {
                diagnostics.Add($"WorkerJobSecurity: P{workerPersonId} holds no active employment at " +
                                $"'{employerBusinessId}' — no employment-security decision to make.");
                return null;
            }

            SocialEvidenceAssessment evidence = SocialEvidenceEvaluator.Assess(
                knowledge, workerPersonId, SocialEvidenceEvaluator.LayoffRiskSubject(employerBusinessId));
            string citation = SocialEvidenceEvaluator.CiteEvidence(evidence);

            if (evidence.PacketCount == 0)
            {
                diagnostics.Add($"WorkerJobSecurity: P{workerPersonId} heard nothing about layoff risk at " +
                                $"'{employerBusinessId}' — staying. {citation}.");
                return new JobSecurityDecision
                {
                    SeekWork = false,
                    Reason = "no layoff-risk information received",
                    Evidence = evidence,
                };
            }

            if (evidence.Corroborated)
            {
                string reason = $"corroborated layoff risk at '{employerBusinessId}': {citation}";
                diagnostics.Add($"WorkerJobSecurity: P{workerPersonId} seeks other work — {reason}.");
                return new JobSecurityDecision
                {
                    SeekWork = true,
                    Reason = reason,
                    Evidence = evidence,
                };
            }

            diagnostics.Add($"WorkerJobSecurity: P{workerPersonId} heard an uncorroborated rumor about layoff risk " +
                            $"at '{employerBusinessId}' — staying, not acting on a single rumor. {citation}.");
            return new JobSecurityDecision
            {
                SeekWork = false,
                Reason = "layoff rumor uncorroborated — seeking corroboration before acting",
                Evidence = evidence,
            };
        }
    }
}
