using System;
using System.Collections.Generic;
using UnityEngine;

namespace LandLedgers.Orchestration.Scenarios.StartingChoice
{
    /// <summary>
    /// T2H (PL-45): kinds of starting opportunities. A constrained pool of
    /// CREDIBLE opportunities — not a freeform wishlist.
    /// </summary>
    public enum StartingOpportunityKind
    {
        Unspecified = 0,
        BuyBusiness = 1,
        TakeEmployment = 2,  // Canon §6.6: player employment is a valid starting state
        HomesteadClaim = 3,
        StartTrade = 4,      // start a trade with tools and skill
        BuyProperty = 5,
    }

    /// <summary>
    /// T2H: one starting opportunity. GHOST-CAN-009 (CANON LOCK): starting
    /// investment follows investigation — the player may research through
    /// available channels before scarce capital is committed, and a failed
    /// investigation is a successful use of the system.
    /// </summary>
    [Serializable]
    public sealed class StartingOpportunity
    {
        public string OpportunityId = string.Empty;
        public StartingOpportunityKind Kind;
        public string Title = string.Empty;
        public string Description = string.Empty;
        public int CapitalRequiredCents;
        /// <summary>Investigation steps available before committing (CAN-009).</summary>
        public List<string> InvestigationSteps = new List<string>();
        public List<string> InvestigationFindings = new List<string>();
        public bool InvestigationComplete;
        public bool Rejected; // killed by investigation — an honest outcome
        public bool Chosen;

        public StartingOpportunity() { }
    }

    /// <summary>
    /// T2H (PL-45, GHOST-CAN-008): the player chooses from a constrained pool
    /// of credible starting opportunities BEFORE the campaign clock starts.
    /// First Ledger currently starts one way; this is the canon-corrected
    /// onboarding it plugs into.
    /// </summary>
    public sealed class StartingChoiceService
    {
        private readonly Dictionary<string, StartingOpportunity> opportunities =
            new Dictionary<string, StartingOpportunity>(StringComparer.Ordinal);
        private readonly List<string> diagnostics = new List<string>();
        private bool clockStarted;

        public IReadOnlyList<string> Diagnostics => diagnostics;
        public bool ClockStarted => clockStarted;
        public IReadOnlyCollection<StartingOpportunity> Opportunities => opportunities.Values;

        public StartingOpportunity OfferOpportunity(
            string opportunityId, StartingOpportunityKind kind, string title,
            string description, int capitalRequiredCents, List<string> investigationSteps,
            List<string> diag)
        {
            diag = diag ?? diagnostics;
            if (clockStarted)
            {
                diag.Add("StartingChoiceService: the campaign clock has started — the starting pool is closed.");
                return null;
            }
            if (string.IsNullOrWhiteSpace(opportunityId) || opportunities.ContainsKey(opportunityId))
            {
                diag.Add($"StartingChoiceService: opportunity id '{opportunityId}' missing or duplicated.");
                return null;
            }
            var opportunity = new StartingOpportunity
            {
                OpportunityId = opportunityId,
                Kind = kind,
                Title = title ?? string.Empty,
                Description = description ?? string.Empty,
                CapitalRequiredCents = Math.Max(0, capitalRequiredCents),
            };
            if (investigationSteps != null) opportunity.InvestigationSteps.AddRange(investigationSteps);
            opportunities[opportunityId] = opportunity;
            diag.Add($"StartingChoiceService: opportunity '{title}' ({kind}) offered — investigate before committing (GHOST-CAN-009).");
            return opportunity;
        }

        /// <summary>
        /// T2H: records an investigation finding. Investigation may KILL the
        /// idea — a failed investigation is a successful use of the system
        /// (GHOST-CAN-009), not a content failure.
        /// </summary>
        public string Investigate(string opportunityId, string finding, bool killsIdea, List<string> diag)
        {
            diag = diag ?? diagnostics;
            if (!opportunities.TryGetValue(opportunityId, out StartingOpportunity opportunity))
                return $"StartingChoiceService.Investigate: unknown opportunity '{opportunityId}'.";
            if (opportunity.Chosen)
                return $"StartingChoiceService.Investigate: '{opportunityId}' already chosen — investigation comes first (GHOST-CAN-009).";
            opportunity.InvestigationFindings.Add(finding ?? string.Empty);
            if (killsIdea)
            {
                opportunity.Rejected = true;
                diag.Add($"StartingChoiceService: investigation killed '{opportunity.Title}' — \"{finding}\". A failed investigation is a successful use of the system.");
            }
            else
            {
                diag.Add($"StartingChoiceService: finding on '{opportunity.Title}' — \"{finding}\".");
            }
            if (opportunity.InvestigationFindings.Count >= opportunity.InvestigationSteps.Count &&
                opportunity.InvestigationSteps.Count > 0)
                opportunity.InvestigationComplete = true;
            return null;
        }

        /// <summary>
        /// T2H: the player chooses. The campaign clock starts on choice —
        /// never before (GHOST-CAN-008).
        /// </summary>
        public string Choose(string opportunityId, List<string> diag)
        {
            diag = diag ?? diagnostics;
            if (clockStarted)
                return "StartingChoiceService.Choose: the campaign clock has already started — one start per campaign.";
            if (!opportunities.TryGetValue(opportunityId, out StartingOpportunity opportunity))
                return $"StartingChoiceService.Choose: unknown opportunity '{opportunityId}'.";
            if (opportunity.Rejected)
                return $"StartingChoiceService.Choose: '{opportunity.Title}' was killed by investigation — it cannot be chosen.";
            opportunity.Chosen = true;
            clockStarted = true;
            diag.Add($"StartingChoiceService: '{opportunity.Title}' chosen — the campaign clock starts now.");
            return null;
        }
    }
}
