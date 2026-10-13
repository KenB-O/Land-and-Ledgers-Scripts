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
    /// Worker C (Pass III): NPC autonomous household responses (brief §13).
    ///
    /// Each day, for a household, this driver:
    ///   1. Builds a HouseholdPressureInput from REAL state only — ledger
    ///      balance, rent obligations (Worker B's RentObligationState via a
    ///      provider), expected income from the employment registry, dependents
    ///      from membership, known opportunities from the opportunity log,
    ///      shelter risk from a provider.
    ///   2. Detects job loss (a member had active employment at the last
    ///      evaluation and has none now) and carries it as RecentJobLoss.
    ///   3. Evaluates via HouseholdDecisionService and executes via
    ///      HouseholdDecisionDriver — real operations, never records alone.
    ///
    /// Representative §13 responses produced:
    ///   declining reserves  -> SeekIncome when opportunities are known;
    ///   rent approaching    -> obligations enter the pressure input;
    ///   job loss            -> job search (known opportunities) vs. relying on
    ///                          household resources (adequate reserves);
    ///   housing change      -> shelter risk -> NegotiateTerms / RelocateCheaper.
    ///
    /// No omniscient planner: every input is the household's own record or a
    /// legitimately-known fact. NPCs exercise only legitimate authority, with
    /// imperfect information — a household with no known opportunities and no
    /// known lender cannot borrow or seek; it economizes.
    /// </summary>
    public sealed class HouseholdAutonomyDriver
    {
        /// <summary>All per-call inputs. Providers may be null (treated as absent).</summary>
        public sealed class AutonomyContext
        {
            public HouseholdLedgerRegistry Ledgers;
            public HouseholdMembershipRegistry Memberships;
            public EmploymentRelationshipRegistry Employments;
            public ContactGraphService ContactGraph;
            public HouseholdDecisionService Decisions;
            public HouseholdDecisionDriver DecisionDriver;
            public HousingSearchNeedRegistry HousingSearchNeeds;
            public FinancialObligationAuthority FinancialObligations;
            public EntityIdRegistry Ids;
            /// <summary>Worker B's rent/board obligations for a household; null = none wired.</summary>
            public Func<int, List<RentObligationState>> RentObligationsFor;
            /// <summary>True when the household's shelter is at risk; null = false.</summary>
            public Func<int, bool> ShelterRiskFor;
            /// <summary>
            /// Pass V P2-2: the durable registry of private lenders. Null =
            /// no lenders known to the world: borrowing is refused everywhere.
            /// </summary>
            public HouseholdLenderDirectory Lenders;
            /// <summary>
            /// Pass V P2-2: person id to display name (for matching known
            /// lender contacts). Null = contacts cannot be named, no facility.
            /// </summary>
            public Func<int, string> PersonNameOf;
            /// <summary>
            /// Pass V P2-2: the hardship asset-sale port. Null = no sale
            /// channel wired.
            /// </summary>
            public IHardshipAssetSalePort AssetSale;
            /// <summary>
            /// Pass V P2-2: distress value (cents) of the household's saleable
            /// surplus; null = 0.
            /// </summary>
            public Func<int, int> SaleableSurplusValueFor;
            public ResidentialConditionKind CurrentCondition = ResidentialConditionKind.SuitablyHoused;
            public HouseholdDecisionDriver.DecisionExecutionParams ExecutionParams =
                new HouseholdDecisionDriver.DecisionExecutionParams();
        }

        // Last-known active-employment fingerprint per person, for job-loss
        // detection. Persisted: a save/load must not re-trigger job-loss
        // responses for employment that already ended.
        private readonly Dictionary<int, string> lastEmploymentFingerprint = new Dictionary<int, string>();
        private readonly List<string> diagnostics = new List<string>();

        public IReadOnlyList<string> Diagnostics => diagnostics;

        /// <summary>
        /// Evaluates one household for one day: builds pressure from real state,
        /// detects job loss, decides, and executes. Deterministic.
        /// </summary>
        public void EvaluateHouseholdDay(int householdId, int dayIndex, AutonomyContext ctx, List<string> diag)
        {
            if (ctx == null)
            {
                if (diag != null) diag.Add("HouseholdAutonomy: no context for household " + householdId);
                return;
            }
            if (ctx.Decisions == null || ctx.DecisionDriver == null)
            {
                if (diag != null) diag.Add("HouseholdAutonomy: decision service/driver missing for household " + householdId);
                return;
            }

            List<int> members = ctx.Memberships != null
                ? ctx.Memberships.GetActiveMembers(householdId)
                : new List<int>();

            // Reserves: the single authoritative household cash ledger.
            long reserves = 0;
            if (ctx.Ledgers != null)
            {
                HouseholdLedger ledger = ctx.Ledgers.Get(householdId);
                if (ledger != null) reserves = ledger.GetBalanceCents();
            }

            // Obligations due: rent/board obligations from Worker B's terms
            // (unpaid portion only). Nothing else invents obligations.
            long obligationsDue = 0;
            if (ctx.RentObligationsFor != null)
            {
                List<RentObligationState> states = ctx.RentObligationsFor(householdId);
                if (states != null)
                {
                    foreach (RentObligationState s in states)
                    {
                        if (s == null) continue;
                        long unpaid = Math.Max(0, s.AmountDueCents - s.PaidCents);
                        if (s.Status != RentObligationStatus.Current || unpaid > 0)
                            obligationsDue += unpaid > 0 ? unpaid : s.AmountDueCents;
                    }
                }
            }

            // Expected income: actual active employments of members, per day.
            // Compensation.AgreedWeeklyWageCents is the negotiated agreement —
            // never status-derived income (Pass II Phase F rule).
            long expectedPerDay = 0;
            int employedMembers = 0;
            var fingerprintParts = new List<string>();
            foreach (int memberId in members)
            {
                List<EmploymentRelationship> active = ctx.Employments != null
                    ? ctx.Employments.GetActiveByEmployee(memberId)
                    : new List<EmploymentRelationship>();
                if (active.Count > 0) employedMembers++;
                foreach (EmploymentRelationship rel in active)
                {
                    if (rel == null) continue;
                    expectedPerDay += Math.Max(0, rel.Compensation.AgreedWeeklyWageCents) / 7;
                    fingerprintParts.Add(rel.Id ?? string.Empty);
                }
            }
            fingerprintParts.Sort(StringComparer.Ordinal);
            string fingerprint = string.Join("|", fingerprintParts.ToArray());

            // Job-loss detection: had employment at last evaluation, none now.
            // Limited information: only this household's own registry history.
            bool recentJobLoss = false;
            string jobLossNote = string.Empty;
            foreach (int memberId in members)
            {
                string last;
                bool had = lastEmploymentFingerprint.TryGetValue(memberId, out last) && !string.IsNullOrEmpty(last);
                List<EmploymentRelationship> now = ctx.Employments != null
                    ? ctx.Employments.GetActiveByEmployee(memberId)
                    : new List<EmploymentRelationship>();
                if (had && now.Count == 0)
                {
                    recentJobLoss = true;
                    jobLossNote = "P" + memberId + " employment ended";
                    if (diag != null) diag.Add("HouseholdAutonomy: household " + householdId + " job loss — " + jobLossNote);
                }
                lastEmploymentFingerprint[memberId] = fingerprint;
            }

            // Dependents: members without active employment. Documented
            // simplification — the integrator may refine with ages.
            int dependents = Math.Max(0, members.Count - employedMembers);

            int knownOpportunities = ctx.DecisionDriver.OpportunityLog.UnpursuedCount(householdId);
            bool shelterRisk = ctx.ShelterRiskFor != null && ctx.ShelterRiskFor(householdId);

            // Searchable contacts: any member with documented one-hop links.
            // Searching the household's own contacts is legitimate even when no
            // opening is known yet (brief §13: declining reserves -> search).
            bool hasSearchableContacts = false;
            if (ctx.ContactGraph != null)
            {
                foreach (int memberId in members)
                {
                    if (ctx.ContactGraph.ReachableCandidates(string.Empty, new[] { memberId }).Count > 0)
                    {
                        hasSearchableContacts = true;
                        break;
                    }
                }
            }

            // Pass V P2-2: legitimate credit comes only from a KNOWN private
            // lender with real finite funds. No known lender -> no credit,
            // never a guaranteed loan.
            IHouseholdCreditFacility facility = null;
            if (ctx.Lenders != null && ctx.ContactGraph != null && ctx.PersonNameOf != null)
            {
                facility = ctx.Lenders.FindFacilityFor(
                    members,
                    memberId => ctx.ContactGraph.ReachableCandidates(string.Empty, new[] { memberId }),
                    ctx.PersonNameOf);
            }

            var input = new HouseholdPressureInput
            {
                HouseholdId = householdId.ToString(),
                Day = dayIndex,
                ReservesCents = reserves,
                ObligationsDueCents = obligationsDue,
                ExpectedIncomeCentsPerDay = expectedPerDay,
                Dependents = dependents,
                KnownOpportunityCount = knownOpportunities,
                HasShelterRisk = shelterRisk,
                HasSearchableContacts = hasSearchableContacts,
                AvailableCreditCents = facility != null ? facility.LendableCents() : 0,
                KnownCreditorId = facility != null ? facility.CreditorName : string.Empty,
                SaleableSurplusValueCents = ctx.SaleableSurplusValueFor != null
                    ? Math.Max(0, ctx.SaleableSurplusValueFor(householdId)) : 0,
                RecentJobLoss = recentJobLoss,
                JobLossNote = jobLossNote,
            };

            HouseholdDecision decision = ctx.Decisions.Evaluate(input);
            if (diag != null) diag.Add("HouseholdAutonomy: household " + householdId + " -> " + decision.Kind + " (" + decision.Reason + ")");

            var execCtx = new HouseholdDecisionDriver.ExecutionContext
            {
                Memberships = ctx.Memberships,
                Params = ctx.ExecutionParams ?? new HouseholdDecisionDriver.DecisionExecutionParams(),
                CreditFacility = facility,
                AssetSale = ctx.AssetSale,
            };
            execCtx.Params.CurrentCondition = ctx.CurrentCondition;
            ctx.DecisionDriver.Execute(decision, input, execCtx, dayIndex, diag);
        }

        /// <summary>Captures job-loss fingerprints for the save pipeline.</summary>
        public HouseholdAutonomyDriverSaveDto CaptureSaveDto()
        {
            var dto = new HouseholdAutonomyDriverSaveDto();
            foreach (KeyValuePair<int, string> kv in lastEmploymentFingerprint)
            {
                dto.employmentFingerprints.Add(new EmploymentFingerprintRecord
                {
                    personId = kv.Key,
                    fingerprint = kv.Value ?? string.Empty,
                });
            }
            return dto;
        }

        /// <summary>
        /// Clear-then-load. Restored fingerprints resume job-loss detection
        /// without re-triggering responses for already-ended employment.
        /// </summary>
        public void LoadFromSaveDto(HouseholdAutonomyDriverSaveDto dto)
        {
            lastEmploymentFingerprint.Clear();
            if (dto == null || dto.employmentFingerprints == null) return;
            foreach (EmploymentFingerprintRecord r in dto.employmentFingerprints)
            {
                if (r == null) continue;
                lastEmploymentFingerprint[r.personId] = r.fingerprint ?? string.Empty;
            }
        }
    }

    [Serializable]
    public sealed class EmploymentFingerprintRecord
    {
        public int personId;
        public string fingerprint = string.Empty;
    }

    /// <summary>Save data for the household autonomy driver.</summary>
    [Serializable]
    public sealed class HouseholdAutonomyDriverSaveDto
    {
        public List<EmploymentFingerprintRecord> employmentFingerprints = new List<EmploymentFingerprintRecord>();
    }
}
