using System;
using System.Collections.Generic;
using LandLedgers.Economy;
using LandLedgers.Economy.Financing;
using LandLedgers.Economy.Labor;
using LandLedgers.Primitives;
using LandLedgers.World.Property;

namespace LandLedgers.Population
{
    /// <summary>
    /// Worker C (Pass III): turns household decisions into REAL operations.
    ///
    /// Canon: the foundation contract — "a decision record alone is not
    /// completed economic activity." Every executed decision produces real state
    /// through existing authorities:
    ///   SeekIncome      -> opportunity discovery through the contact graph
    ///                      (Pass II ContactGraphService, reused as-is) +
    ///                      the employment registry. No omniscient lists.
    ///   RelocateCheaper -> opens a real HousingSearchNeed.
    ///   Borrow          -> a real FinancialObligation via
    ///                      FinancialObligationAuthority with a LEGITIMATE known
    ///                      lender; never an invented counterparty. The liability
    ///                      is recorded here; the lender's actual cash payment is
    ///                      the lender's own action through HouseholdLedger —
    ///                      never synthesized.
    ///   NegotiateTerms  -> a real pending TenancyNegotiationRequest against the
    ///                      tenancy agreement (Worker B's terms). The landlord's
    ///                      response is a separate legitimate action.
    ///   Economize       -> a standing HouseholdSpendingPolicy (real household
    ///                      instruction, consumed by the shopping loop once wired).
    ///   NoAction        -> nothing to do.
    /// </summary>
    public sealed class HouseholdDecisionDriver
    {
        private readonly ContactGraphService contactGraph;
        private readonly EmploymentRelationshipRegistry employments;
        private readonly HousingSearchNeedRegistry housingSearchNeeds;
        private readonly FinancialObligationAuthority financialObligations;
        private readonly EntityIdRegistry ids;

        private readonly HouseholdOpportunityLog opportunityLog = new HouseholdOpportunityLog();
        private readonly List<TenancyNegotiationRequest> negotiationRequests = new List<TenancyNegotiationRequest>();
        private readonly List<HouseholdSpendingPolicy> spendingPolicies = new List<HouseholdSpendingPolicy>();
        private readonly List<string> diagnostics = new List<string>();
        private int negotiationSequence;

        public IReadOnlyList<string> Diagnostics => diagnostics;
        public HouseholdOpportunityLog OpportunityLog => opportunityLog;
        public IReadOnlyList<TenancyNegotiationRequest> NegotiationRequests => negotiationRequests;
        public IReadOnlyList<HouseholdSpendingPolicy> SpendingPolicies => spendingPolicies;

        public HouseholdDecisionDriver(
            ContactGraphService contactGraph,
            EmploymentRelationshipRegistry employments,
            HousingSearchNeedRegistry housingSearchNeeds,
            FinancialObligationAuthority financialObligations,
            EntityIdRegistry ids)
        {
            this.contactGraph = contactGraph;
            this.employments = employments;
            this.housingSearchNeeds = housingSearchNeeds;
            this.financialObligations = financialObligations;
            this.ids = ids;
        }

        /// <summary>Per-call execution data: registries the driver does not own.</summary>
        public sealed class ExecutionContext
        {
            /// <summary>May be null: without memberships, SeekIncome discovers nothing.</summary>
            public HouseholdMembershipRegistry Memberships;
            /// <summary>Negotiation target for NegotiateTerms; null fields = cannot execute.</summary>
            public DecisionExecutionParams Params = new DecisionExecutionParams();
        }

        public sealed class DecisionExecutionParams
        {
            public string AgreementId = string.Empty;
            public string LandlordHolderId = string.Empty;
            public string RequestedChange = string.Empty;
            public ResidentialConditionKind CurrentCondition = ResidentialConditionKind.SuitablyHoused;
        }

        public sealed class DecisionExecutionResult
        {
            public bool Executed;
            public string Detail = string.Empty;
        }

        public DecisionExecutionResult Execute(HouseholdDecision decision,
            HouseholdPressureInput pressure, ExecutionContext ctx, int dayIndex, List<string> diag)
        {
            if (decision == null)
                return NotExecuted(diag, "no decision to execute");
            if (ctx == null) ctx = new ExecutionContext();

            switch (decision.Kind)
            {
                case HouseholdDecisionKind.SeekIncome:
                    return ExecuteSeekIncome(decision, ctx, dayIndex, diag);
                case HouseholdDecisionKind.RelocateCheaper:
                    return ExecuteRelocateCheaper(decision, ctx, dayIndex, diag);
                case HouseholdDecisionKind.Borrow:
                    return ExecuteBorrow(decision, pressure, dayIndex, diag);
                case HouseholdDecisionKind.NegotiateTerms:
                    return ExecuteNegotiateTerms(decision, ctx, dayIndex, diag);
                case HouseholdDecisionKind.Economize:
                    return ExecuteEconomize(decision, dayIndex, diag);
                default:
                    return new DecisionExecutionResult
                    {
                        Executed = true,
                        Detail = "NoAction: nothing to execute",
                    };
            }
        }

        private DecisionExecutionResult ExecuteSeekIncome(HouseholdDecision decision,
            ExecutionContext ctx, int dayIndex, List<string> diag)
        {
            if (!int.TryParse(decision.HouseholdId, out int householdId))
                return NotExecuted(diag, "SeekIncome: household id '" + decision.HouseholdId + "' is not a numeric household");
            if (contactGraph == null || employments == null)
                return NotExecuted(diag, "SeekIncome: contact graph or employment registry unavailable");
            if (ctx.Memberships == null)
                return NotExecuted(diag, "SeekIncome: no membership registry — cannot enumerate members");

            List<int> members = ctx.Memberships.GetActiveMembers(householdId);
            int discovered = 0;
            int membersSearched = 0;
            foreach (int memberId in members)
            {
                membersSearched++;
                // One-hop documented contacts only — the employer-side rule from
                // Pass II, applied person-side: a person learns about work through
                // people they actually know (Canon XXXI Part IV §4.4).
                List<int> contacts = contactGraph.ReachableCandidates(string.Empty, new[] { memberId });
                foreach (int contactId in contacts)
                {
                    List<EmploymentRelationship> active = employments.GetActiveByEmployee(contactId);
                    foreach (EmploymentRelationship rel in active)
                    {
                        if (rel == null || string.IsNullOrWhiteSpace(rel.EmployerBusinessId))
                            continue;
                        string provenance = contactGraph.DescribeProvenance(memberId, contactId)
                            ?? ("P" + memberId + " knows P" + contactId);
                        if (opportunityLog.RecordIfNew(householdId, rel.EmployerBusinessId,
                            rel.RoleDisplayName, provenance, dayIndex))
                            discovered++;
                    }
                }
            }

            string detail = string.Format(
                "SeekIncome executed: searched {0} member(s)' documented contacts, discovered {1} new opportunitie(s) (log holds {2} unpursued)",
                membersSearched, discovered, opportunityLog.UnpursuedCount(householdId));
            if (diag != null) diag.Add(detail);
            return new DecisionExecutionResult { Executed = true, Detail = detail };
        }

        private DecisionExecutionResult ExecuteRelocateCheaper(HouseholdDecision decision,
            ExecutionContext ctx, int dayIndex, List<string> diag)
        {
            if (!int.TryParse(decision.HouseholdId, out int householdId))
                return NotExecuted(diag, "RelocateCheaper: household id '" + decision.HouseholdId + "' is not numeric");
            if (housingSearchNeeds == null)
                return NotExecuted(diag, "RelocateCheaper: housing search registry unavailable");
            if (ctx.Memberships == null)
                return NotExecuted(diag, "RelocateCheaper: no membership registry — cannot size the need");

            int places = Math.Max(1, ctx.Memberships.GetActiveMembers(householdId).Count);
            HousingSearchNeed need = housingSearchNeeds.OpenNeed(householdId, places,
                ctx.Params.CurrentCondition,
                "Household decision: " + decision.Reason, dayIndex);
            string detail = string.Format(
                "RelocateCheaper executed: housing search need {0} opened for household {1} ({2} places)",
                need.NeedId, householdId, places);
            if (diag != null) diag.Add(detail);
            return new DecisionExecutionResult { Executed = true, Detail = detail };
        }

        private DecisionExecutionResult ExecuteBorrow(HouseholdDecision decision,
            HouseholdPressureInput pressure, int dayIndex, List<string> diag)
        {
            if (!int.TryParse(decision.HouseholdId, out int householdId))
                return NotExecuted(diag, "Borrow: household id '" + decision.HouseholdId + "' is not numeric");
            if (financialObligations == null || ids == null)
                return NotExecuted(diag, "Borrow: financial authority or id registry unavailable");

            string creditor = pressure != null ? (pressure.KnownCreditorId ?? string.Empty).Trim() : string.Empty;
            if (string.IsNullOrEmpty(creditor))
            {
                // Honest refusal: a loan needs a real counterparty. The decision
                // stands as intent; the integrator must supply a known lender.
                return NotExecuted(diag, "Borrow: no legitimate known lender — will not invent a counterparty");
            }

            long coverage = (pressure != null ? pressure.ReservesCents : 0)
                + (long)HouseholdDecisionService.CoverageDays * Math.Max(0, pressure != null ? pressure.ExpectedIncomeCentsPerDay : 0);
            long obligations = pressure != null ? Math.Max(0, pressure.ObligationsDueCents) : 0;
            long principal = Math.Max(1, obligations - coverage);

            FinancialObligation obligation = financialObligations.Create(
                ids, FinancialObligationKind.Loan,
                debtor: "household-" + householdId,
                creditor: creditor,
                principalCents: (int)Math.Min(principal, int.MaxValue),
                dayIndex: dayIndex,
                terms: "household borrowing per decision " + decision.Day,
                purpose: "cover household obligations shortfall",
                agreementId: "dec-" + householdId + "-" + decision.Day);
            if (obligation == null)
                return NotExecuted(diag, "Borrow: financial authority rejected the obligation");

            string detail = string.Format(
                "Borrow executed: obligation {0} opened — household-{1} owes {2} {3}c (liability recorded; lender's cash payment is the lender's own action)",
                obligation.ObligationId, householdId, creditor, obligation.OutstandingPrincipalCents);
            if (diag != null) diag.Add(detail);
            return new DecisionExecutionResult { Executed = true, Detail = detail };
        }

        private DecisionExecutionResult ExecuteNegotiateTerms(HouseholdDecision decision,
            ExecutionContext ctx, int dayIndex, List<string> diag)
        {
            if (!int.TryParse(decision.HouseholdId, out int householdId))
                return NotExecuted(diag, "NegotiateTerms: household id '" + decision.HouseholdId + "' is not numeric");
            string agreementId = ctx.Params.AgreementId ?? string.Empty;
            string landlord = ctx.Params.LandlordHolderId ?? string.Empty;
            if (string.IsNullOrWhiteSpace(agreementId) || string.IsNullOrWhiteSpace(landlord))
                return NotExecuted(diag, "NegotiateTerms: no tenancy agreement/landlord supplied — cannot address a request");

            var request = new TenancyNegotiationRequest
            {
                RequestId = "tneg-" + householdId + "-" + (negotiationSequence++),
                AgreementId = agreementId,
                HouseholdId = householdId,
                LandlordHolderId = landlord,
                DayIndex = dayIndex,
                RequestedChange = string.IsNullOrWhiteSpace(ctx.Params.RequestedChange)
                    ? "extension or revised payment terms per household decision"
                    : ctx.Params.RequestedChange,
                Status = TenancyNegotiationStatus.Pending,
            };
            negotiationRequests.Add(request);
            string detail = string.Format(
                "NegotiateTerms executed: request {0} sent to {1} on agreement {2} — PENDING landlord response (no automatic concession)",
                request.RequestId, landlord, agreementId);
            if (diag != null) diag.Add(detail);
            return new DecisionExecutionResult { Executed = true, Detail = detail };
        }

        private DecisionExecutionResult ExecuteEconomize(HouseholdDecision decision,
            int dayIndex, List<string> diag)
        {
            if (!int.TryParse(decision.HouseholdId, out int householdId))
                return NotExecuted(diag, "Economize: household id '" + decision.HouseholdId + "' is not numeric");

            // A standing household instruction with real economic meaning: halve
            // discretionary spending until the cushion recovers. Consumed by the
            // shopping loop once wired (Codex/integrator — see handoff).
            var policy = new HouseholdSpendingPolicy
            {
                HouseholdId = householdId,
                DayIndexSet = dayIndex,
                DiscretionaryCapPercent = 50,
                Reason = decision.Reason ?? string.Empty,
            };
            // Replace any existing policy: one standing instruction at a time.
            for (int i = spendingPolicies.Count - 1; i >= 0; i--)
                if (spendingPolicies[i].HouseholdId == householdId)
                    spendingPolicies.RemoveAt(i);
            spendingPolicies.Add(policy);

            string detail = string.Format(
                "Economize executed: household {0} discretionary spending capped at {1}% from day {2}",
                householdId, policy.DiscretionaryCapPercent, dayIndex);
            if (diag != null) diag.Add(detail);
            return new DecisionExecutionResult { Executed = true, Detail = detail };
        }

        private DecisionExecutionResult NotExecuted(List<string> diag, string detail)
        {
            if (diag != null) diag.Add(detail);
            diagnostics.Add(detail);
            return new DecisionExecutionResult { Executed = false, Detail = detail };
        }

        public HouseholdSpendingPolicy GetSpendingPolicy(int householdId)
        {
            for (int i = spendingPolicies.Count - 1; i >= 0; i--)
                if (spendingPolicies[i].HouseholdId == householdId)
                    return spendingPolicies[i];
            return null;
        }

        /// <summary>Captures execution state for the save pipeline.</summary>
        public HouseholdDecisionDriverSaveDto CaptureSaveDto()
        {
            return new HouseholdDecisionDriverSaveDto
            {
                opportunityLog = opportunityLog.CaptureSaveDto(),
                negotiationRequests = new List<TenancyNegotiationRequest>(negotiationRequests),
                spendingPolicies = new List<HouseholdSpendingPolicy>(spendingPolicies),
                negotiationSequence = negotiationSequence,
            };
        }

        /// <summary>
        /// Clear-then-load. Restored obligations/requests/policies are records —
        /// reload never re-opens a housing need, re-records an obligation, or
        /// re-sends a negotiation request (no duplicated obligations).
        /// </summary>
        public void LoadFromSaveDto(HouseholdDecisionDriverSaveDto dto)
        {
            negotiationRequests.Clear();
            spendingPolicies.Clear();
            negotiationSequence = 0;
            if (dto == null) return;
            opportunityLog.LoadFromSaveDto(dto.opportunityLog);
            negotiationSequence = Math.Max(0, dto.negotiationSequence);
            if (dto.negotiationRequests != null)
                foreach (TenancyNegotiationRequest r in dto.negotiationRequests)
                    if (r != null) negotiationRequests.Add(r);
            if (dto.spendingPolicies != null)
                foreach (HouseholdSpendingPolicy p in dto.spendingPolicies)
                    if (p != null) spendingPolicies.Add(p);
        }
    }

    /// <summary>
    /// A household's request to its landlord for revised tenancy terms.
    /// Canon: XXXI Part VI §§6.1-6.8 — tenancy is a contractual relationship;
    /// no automatic eviction on missed rent, and symmetrically no automatic
    /// concession on request. The landlord's response is a separate action.
    /// </summary>
    [Serializable]
    public sealed class TenancyNegotiationRequest
    {
        public string RequestId = string.Empty;
        public string AgreementId = string.Empty;
        public int HouseholdId;
        public string LandlordHolderId = string.Empty;
        public int DayIndex;
        public string RequestedChange = string.Empty;
        public TenancyNegotiationStatus Status = TenancyNegotiationStatus.Pending;
    }

    public enum TenancyNegotiationStatus
    {
        Pending = 0,
        Accepted = 1,
        Refused = 2,
    }

    /// <summary>
    /// Standing household spending instruction from an Economize decision.
    /// Real economic meaning once consumed by the shopping loop (wiring:
    /// Codex/integrator — see handoff). One policy per household.
    /// </summary>
    [Serializable]
    public sealed class HouseholdSpendingPolicy
    {
        public int HouseholdId;
        public int DayIndexSet;
        /// <summary>Discretionary spending allowed, percent of normal (50 = halve).</summary>
        public int DiscretionaryCapPercent = 100;
        public string Reason = string.Empty;
    }

    /// <summary>Save data for the household decision driver.</summary>
    [Serializable]
    public sealed class HouseholdDecisionDriverSaveDto
    {
        public HouseholdOpportunityLogSaveDto opportunityLog = new HouseholdOpportunityLogSaveDto();
        public List<TenancyNegotiationRequest> negotiationRequests = new List<TenancyNegotiationRequest>();
        public List<HouseholdSpendingPolicy> spendingPolicies = new List<HouseholdSpendingPolicy>();
        public int negotiationSequence;
    }
}
