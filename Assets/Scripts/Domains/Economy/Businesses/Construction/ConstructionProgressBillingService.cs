using System;
using System.Collections.Generic;

namespace LandLedgers.Economy
{
    /// <summary>
    /// D4H: the progress-billing implementation behind
    /// <see cref="IConstructionProgressBilling"/> — deposits, milestone
    /// draws, retainage, and the completion balance for D4G building
    /// contracts.
    ///
    /// Division of authority with D4G (seam discipline):
    /// - D4G records the contract, its payment TERMS, staged acceptance,
    ///   change orders, and the deposit/final-balance OBLIGATIONS. It never
    ///   moves money. This service never rewrites D4G's records: it reads
    ///   terms/acceptance/obligations, creates milestone obligations as new
    ///   records (D4G defines the deposit and final-balance ones; the task
    ///   gives D4H the milestone draws), and moves money only through the
    ///   injected <see cref="IConstructionBillingCashPort"/>.
    /// - Money moves owner-to-builder ONLY through the port, only against a
    ///   recorded obligation, and only when the guards pass. A port refusal
    ///   leaves the obligation un-settled and the draw unrecorded — never a
    ///   half-state, never synthetic funds.
    ///
    /// Guards (all refusals are LOUD — a non-null return string):
    /// - Deposit: only when the terms name a deposit; once per contract.
    /// - Milestones: only against an ACCEPTED stage, and only through an
    ///   explicitly recorded milestone-to-stage link (see
    ///   <see cref="RecordMilestoneStageLink"/>) — the D4G terms carry no
    ///   stage binding, and this service refuses to guess one.
    /// - Overbilling: cumulative released billing (every event's moved
    ///   cents, including externally-settled obligations and retainage
    ///   releases) can never exceed the contract price
    ///   (AgreedPriceCents, which already folds in accepted change orders).
    /// - Shortfalls: when the port reports the owner's lots cannot cover a
    ///   draw, the draw is refused and nothing is recorded as settled.
    /// - Retainage (see <see cref="ConstructionProgressBillingPolicy"/>):
    ///   the policy's holdback is withheld from eligible draws, tracked per
    ///   contract, and released ONLY on final acceptance, with the
    ///   completion balance.
    ///
    /// Canon: Part VI §7.3 (deposits, milestone/progress payments,
    /// completion balances are legitimate terms; exact percentages are
    /// contract data, never canon values). Audit 04: Project Authorization
    /// is not Completion — the completion balance requires final
    /// acceptance, and no draw ever precedes the work it pays for.
    /// </summary>
    public sealed class ConstructionProgressBillingService : IConstructionProgressBilling
    {
        /// <summary>D4H: one explicit binding between a payment milestone and the acceptance stage that earns it.</summary>
        [Serializable]
        public sealed class ConstructionMilestoneStageLink
        {
            public string ContractId = string.Empty;
            public string MilestoneId = string.Empty;
            public string StageId = string.Empty;
            public int RecordedDayIndex = -1;

            public ConstructionMilestoneStageLink() { }
        }

        /// <summary>D4H: one per-contract retainage balance line (for save/load).</summary>
        [Serializable]
        public sealed class ConstructionRetainageLedgerLine
        {
            public string ContractId = string.Empty;
            public int RetainedCents;

            public ConstructionRetainageLedgerLine() { }
        }

        private readonly IConstructionBillingCashPort cashPort;
        private readonly List<ConstructionBillingEvent> events = new List<ConstructionBillingEvent>();
        private readonly List<ConstructionMilestoneStageLink> milestoneStageLinks = new List<ConstructionMilestoneStageLink>();
        private readonly Dictionary<string, int> retainageByContract =
            new Dictionary<string, int>(StringComparer.Ordinal);

        private int nextEventNumber = 1;
        private int nextObligationNumber = 1;

        public string BillingServiceName => "ConstructionProgressBillingService (D4H)";

        /// <summary>
        /// D4H: the operator-policy calibration in force (retainage). The
        /// canon reserves exact percentages to contract data, so the policy
        /// is replaceable at any time; it defaults to the research-grounded
        /// 10% holdback on progress draws.
        /// </summary>
        public ConstructionProgressBillingPolicy Policy { get; set; } =
            new ConstructionProgressBillingPolicy();

        public IReadOnlyList<ConstructionMilestoneStageLink> MilestoneStageLinks => milestoneStageLinks;

        public ConstructionProgressBillingService(IConstructionBillingCashPort cashPort)
            : this(cashPort, null)
        {
        }

        public ConstructionProgressBillingService(IConstructionBillingCashPort cashPort,
            ConstructionProgressBillingPolicy policy)
        {
            this.cashPort = cashPort;
            if (policy != null) Policy = policy;
        }

        /// <summary>
        /// D4H: bills the contract's signing deposit owner-to-builder through
        /// the cash port. Refuses when the terms name no deposit, when the
        /// contract is not in a billable state, when the deposit was already
        /// settled, when the draw would overbill the contract price, or when
        /// the port refuses the move (shortfall — refused loudly).
        /// </summary>
        public string RecordDeposit(ConstructionContract contract, int dayIndex, List<string> diagnostics)
        {
            diagnostics = diagnostics ?? new List<string>();
            string refusal = CheckContractBillable(contract, "RecordDeposit", allowPreWork: true, diagnostics);
            if (refusal != null) return refusal;

            int depositCents = contract.PaymentTerms != null
                ? Math.Max(0, contract.PaymentTerms.DepositCents)
                : 0;
            if (depositCents <= 0)
                return $"ConstructionProgressBillingService: {contract.ContractId} — the contract terms name no deposit; refusing rather than inventing one.";

            ConstructionPaymentObligation obligation = FindObligation(contract, "deposit");
            if (obligation == null)
            {
                // D4G records the deposit obligation at formation; a
                // hand-built contract may lack it. The TERMS are the recorded
                // authority — create the record, never the money.
                obligation = new ConstructionPaymentObligation
                {
                    ObligationId = $"PAYD-{nextObligationNumber++:D4}",
                    ContractId = contract.ContractId,
                    Kind = "deposit",
                    Description = "Contract deposit due at signing (recorded by D4H billing; authority: contract terms)",
                    AmountCents = depositCents,
                    PayeeDisplayName = contract.BuilderParty != null ? contract.BuilderParty.DisplayName : string.Empty,
                    RecordedDayIndex = dayIndex,
                    Status = ConstructionPaymentObligationStatus.Recorded,
                };
                contract.PaymentObligations.Add(obligation);
            }
            if (obligation.Status == ConstructionPaymentObligationStatus.SettledExternally)
                return $"ConstructionProgressBillingService: {contract.ContractId} deposit {obligation.ObligationId} is already settled — double-billing refused.";

            refusal = CheckOverbilling(contract, depositCents, diagnostics);
            if (refusal != null) return refusal;

            string ownerKey = ResolvePartyKey(contract.OwnerParty, "owner", contract.ContractId, diagnostics);
            if (ownerKey == null) return diagnostics[diagnostics.Count - 1];
            string builderKey = ResolvePartyKey(contract.BuilderParty, "builder", contract.ContractId, diagnostics);
            if (builderKey == null) return diagnostics[diagnostics.Count - 1];

            Policy.SplitDraw(ConstructionBillingDrawKind.Deposit, depositCents,
                out int retainedCents, out int movedCents);
            if (movedCents <= 0 && depositCents > 0)
                return $"ConstructionProgressBillingService: {contract.ContractId} — the billing policy would move $0 of the deposit; refusing rather than recording a phantom draw.";

            if (movedCents > 0)
            {
                string portRefusal = cashPort.MoveCash(ownerKey, builderKey, movedCents, dayIndex,
                    $"{contract.ContractId} deposit", diagnostics);
                if (portRefusal != null)
                {
                    diagnostics.Add($"ConstructionProgressBillingService: {contract.ContractId} deposit REFUSED — {portRefusal} Nothing moved; the obligation stays recorded, not settled.");
                    return $"ConstructionProgressBillingService: {contract.ContractId} deposit refused: {portRefusal}";
                }
            }

            obligation.Status = ConstructionPaymentObligationStatus.SettledExternally;
            AddRetainage(contract.ContractId, retainedCents);
            RecordEvent(contract.ContractId, obligation.ObligationId, "deposit", movedCents, dayIndex,
                $"Deposit {FormatMoney(depositCents)} settled owner-to-builder" +
                (retainedCents > 0 ? $"; {FormatMoney(retainedCents)} retained to final acceptance (policy)" : "") +
                $"; cumulative billed {FormatMoney(CumulativeBilledCents(contract.ContractId) + movedCents)}.");
            diagnostics.Add($"ConstructionProgressBillingService: {contract.ContractId} deposit {FormatMoney(movedCents)} moved owner-to-builder on day {dayIndex}.");
            return null;
        }

        /// <summary>
        /// D4H: bills one named milestone draw. The milestone must be linked
        /// to an acceptance stage via <see cref="RecordMilestoneStageLink"/>
        /// and that stage must be ACCEPTED — draws never run ahead of work.
        /// Releases the draw as a recorded obligation on the contract, moves
        /// the draw (less the policy retainage slice) through the cash port,
        /// and tracks the retained slice for release at final acceptance.
        /// </summary>
        public string RecordMilestone(ConstructionContract contract, string milestoneId, int dayIndex, List<string> diagnostics)
        {
            diagnostics = diagnostics ?? new List<string>();
            string refusal = CheckContractBillable(contract, "RecordMilestone", allowPreWork: false, diagnostics);
            if (refusal != null) return refusal;
            if (string.IsNullOrWhiteSpace(milestoneId))
                return $"ConstructionProgressBillingService: {contract.ContractId} — no milestone id supplied.";

            ConstructionPaymentMilestone milestone = FindMilestone(contract, milestoneId);
            if (milestone == null)
                return $"ConstructionProgressBillingService: {contract.ContractId} has no milestone '{milestoneId}' in its payment terms — refusing rather than inventing one.";
            int milestoneCents = Math.Max(0, milestone.AmountCents);
            if (milestoneCents <= 0)
                return $"ConstructionProgressBillingService: {contract.ContractId} milestone '{milestoneId}' is for {FormatMoney(milestoneCents)} — nothing to bill.";

            string obligationKind = "milestone:" + milestoneId;
            ConstructionPaymentObligation obligation = FindObligation(contract, obligationKind);
            if (obligation == null)
            {
                obligation = new ConstructionPaymentObligation
                {
                    ObligationId = $"PAYM-{nextObligationNumber++:D4}",
                    ContractId = contract.ContractId,
                    Kind = obligationKind,
                    Description = $"Milestone draw '{milestone.Name}' (authority: contract terms + accepted stage link)",
                    AmountCents = milestoneCents,
                    PayeeDisplayName = contract.BuilderParty != null ? contract.BuilderParty.DisplayName : string.Empty,
                    RecordedDayIndex = dayIndex,
                    ReleasedDayIndex = dayIndex,
                    Status = ConstructionPaymentObligationStatus.Released,
                };
                contract.PaymentObligations.Add(obligation);
            }
            if (obligation.Status == ConstructionPaymentObligationStatus.SettledExternally)
                return $"ConstructionProgressBillingService: {contract.ContractId} milestone '{milestoneId}' ({obligation.ObligationId}) is already settled — double-billing refused.";

            // Draws only against ACCEPTED stages, never ahead of work. The
            // D4G terms carry no stage binding, so the binding must be
            // recorded explicitly first — no guessing.
            ConstructionMilestoneStageLink link = FindLink(contract.ContractId, milestoneId);
            if (link == null)
                return $"ConstructionProgressBillingService: {contract.ContractId} milestone '{milestoneId}' has no recorded stage link — record the milestone-to-stage binding first (refusing rather than guessing which work earns it).";
            ConstructionAcceptanceStage stage = contract.FindStage(link.StageId);
            if (stage == null)
                return $"ConstructionProgressBillingService: {contract.ContractId} milestone '{milestoneId}' links to unknown stage '{link.StageId}' — refusing.";
            if (!stage.IsAccepted)
                return $"ConstructionProgressBillingService: {contract.ContractId} milestone '{milestoneId}' is earned by stage '{stage.StageName}', which is {stage.Status} — draws only against accepted stages, never ahead of work.";

            refusal = CheckOverbilling(contract, milestoneCents, diagnostics);
            if (refusal != null) return refusal;

            string ownerKey = ResolvePartyKey(contract.OwnerParty, "owner", contract.ContractId, diagnostics);
            if (ownerKey == null) return diagnostics[diagnostics.Count - 1];
            string builderKey = ResolvePartyKey(contract.BuilderParty, "builder", contract.ContractId, diagnostics);
            if (builderKey == null) return diagnostics[diagnostics.Count - 1];

            Policy.SplitDraw(ConstructionBillingDrawKind.Milestone, milestoneCents,
                out int retainedCents, out int movedCents);
            if (movedCents <= 0)
                return $"ConstructionProgressBillingService: {contract.ContractId} — the billing policy would move $0 of milestone '{milestoneId}'; refusing rather than recording a phantom draw.";

            string portRefusal = cashPort.MoveCash(ownerKey, builderKey, movedCents, dayIndex,
                $"{contract.ContractId} milestone {milestoneId}", diagnostics);
            if (portRefusal != null)
            {
                diagnostics.Add($"ConstructionProgressBillingService: {contract.ContractId} milestone '{milestoneId}' REFUSED — {portRefusal} Nothing moved; the obligation stays recorded, not settled.");
                return $"ConstructionProgressBillingService: {contract.ContractId} milestone '{milestoneId}' refused: {portRefusal}";
            }

            obligation.Status = ConstructionPaymentObligationStatus.SettledExternally;
            AddRetainage(contract.ContractId, retainedCents);
            RecordEvent(contract.ContractId, obligation.ObligationId, "milestone", movedCents, dayIndex,
                $"Milestone '{milestone.Name}' {FormatMoney(milestoneCents)} against accepted stage '{stage.StageName}'; " +
                $"moved {FormatMoney(movedCents)}, retained {FormatMoney(retainedCents)} to final acceptance.");
            diagnostics.Add($"ConstructionProgressBillingService: {contract.ContractId} milestone '{milestone.Name}' {FormatMoney(movedCents)} moved owner-to-builder on day {dayIndex} (retained {FormatMoney(retainedCents)}).");
            return null;
        }

        /// <summary>
        /// D4H: bills the completion balance released at final acceptance —
        /// plus every retainage slice withheld from earlier draws. Requires
        /// the contract to be Accepted (no completion without acceptance)
        /// and the final-balance obligation D4G released at final
        /// acceptance. The retained money is the owner's until this moment;
        /// it becomes the builder's only here.
        /// </summary>
        public string RecordCompletionBalance(ConstructionContract contract, int dayIndex, List<string> diagnostics)
        {
            diagnostics = diagnostics ?? new List<string>();
            if (contract == null)
                return "ConstructionProgressBillingService: no contract supplied.";
            if (contract.Status != ConstructionContractStatus.Accepted)
                return $"ConstructionProgressBillingService: {contract.ContractId} is {contract.Status}, not Accepted — the completion balance requires final acceptance (Audit 04: Project Authorization is not Completion).";

            ConstructionPaymentObligation obligation = FindObligation(contract, "final-balance");
            if (obligation == null)
                return $"ConstructionProgressBillingService: {contract.ContractId} — D4G releases the final-balance obligation at final acceptance; none is on record, so there is nothing to bill.";
            if (obligation.Status == ConstructionPaymentObligationStatus.SettledExternally)
                return $"ConstructionProgressBillingService: {contract.ContractId} completion balance {obligation.ObligationId} is already settled — double-billing refused.";

            int balanceCents = Math.Max(0, obligation.AmountCents);
            int retainedCents = RetainageBalanceCents(contract.ContractId);
            int totalMoveCents = balanceCents + retainedCents;
            if (totalMoveCents <= 0)
                return $"ConstructionProgressBillingService: {contract.ContractId} — the completion balance and retained holdback are both $0; nothing to bill.";

            string refusal = CheckOverbilling(contract, balanceCents, diagnostics);
            if (refusal != null) return refusal;

            string ownerKey = ResolvePartyKey(contract.OwnerParty, "owner", contract.ContractId, diagnostics);
            if (ownerKey == null) return diagnostics[diagnostics.Count - 1];
            string builderKey = ResolvePartyKey(contract.BuilderParty, "builder", contract.ContractId, diagnostics);
            if (builderKey == null) return diagnostics[diagnostics.Count - 1];

            string portRefusal = cashPort.MoveCash(ownerKey, builderKey, totalMoveCents, dayIndex,
                $"{contract.ContractId} completion balance + retainage release", diagnostics);
            if (portRefusal != null)
            {
                diagnostics.Add($"ConstructionProgressBillingService: {contract.ContractId} completion balance REFUSED — {portRefusal} Nothing moved; the obligation stays as D4G released it.");
                return $"ConstructionProgressBillingService: {contract.ContractId} completion balance refused: {portRefusal}";
            }

            obligation.Status = ConstructionPaymentObligationStatus.SettledExternally;
            RecordEvent(contract.ContractId, obligation.ObligationId, "final-balance", balanceCents, dayIndex,
                $"Completion balance {FormatMoney(balanceCents)} settled on final acceptance.");
            if (retainedCents > 0)
            {
                RecordEvent(contract.ContractId, obligation.ObligationId, "retainage-release", retainedCents, dayIndex,
                    $"Retainage {FormatMoney(retainedCents)} released on final acceptance (withheld from earlier progress draws per billing policy).");
            }
            ClearRetainage(contract.ContractId);
            diagnostics.Add($"ConstructionProgressBillingService: {contract.ContractId} completion balance {FormatMoney(balanceCents)}" +
                (retainedCents > 0 ? $" + retainage {FormatMoney(retainedCents)}" : "") +
                $" moved owner-to-builder on day {dayIndex}.");
            return null;
        }

        /// <summary>D4H: every billing event recorded for one contract, in record order.</summary>
        public IReadOnlyList<ConstructionBillingEvent> EventsForContract(string contractId)
        {
            var result = new List<ConstructionBillingEvent>();
            if (string.IsNullOrWhiteSpace(contractId)) return result;
            foreach (ConstructionBillingEvent e in events)
            {
                if (e != null && string.Equals(e.ContractId, contractId, StringComparison.Ordinal))
                    result.Add(e);
            }
            return result;
        }

        /// <summary>
        /// D4H: records that a payment obligation was settled in the ledgers
        /// OUTSIDE this service (e.g. the owner handed the builder cash
        /// directly). Recording only — the port is never touched here, and
        /// an obligation already settled through the billing service cannot
        /// be recorded again (no double-counting the cumulative guard).
        /// </summary>
        public string MarkObligationSettled(ConstructionContract contract, string obligationId,
            string settledBy, int dayIndex, List<string> diagnostics)
        {
            diagnostics = diagnostics ?? new List<string>();
            if (contract == null) return "ConstructionProgressBillingService: no contract supplied.";
            if (string.IsNullOrWhiteSpace(obligationId))
                return $"ConstructionProgressBillingService: {contract.ContractId} — no obligation id supplied.";
            if (contract.Status == ConstructionContractStatus.Cancelled
                || contract.Status == ConstructionContractStatus.Closed)
                return $"ConstructionProgressBillingService: {contract.ContractId} is {contract.Status} — no new settlement records on a closed contract.";

            ConstructionPaymentObligation obligation = null;
            if (contract.PaymentObligations != null)
            {
                foreach (ConstructionPaymentObligation o in contract.PaymentObligations)
                {
                    if (o != null && string.Equals(o.ObligationId, obligationId, StringComparison.Ordinal))
                    {
                        obligation = o;
                        break;
                    }
                }
            }
            if (obligation == null)
                return $"ConstructionProgressBillingService: {contract.ContractId} has no obligation '{obligationId}' — refusing to record settlement of a phantom obligation.";
            if (obligation.Status == ConstructionPaymentObligationStatus.SettledExternally)
                return $"ConstructionProgressBillingService: {contract.ContractId} obligation '{obligationId}' is already recorded settled — no second record.";

            string refusal = CheckOverbilling(contract, Math.Max(0, obligation.AmountCents), diagnostics);
            if (refusal != null) return refusal;

            obligation.Status = ConstructionPaymentObligationStatus.SettledExternally;
            RecordEvent(contract.ContractId, obligation.ObligationId, "settlement",
                Math.Max(0, obligation.AmountCents), dayIndex,
                $"Obligation '{obligation.Description}' settled in the ledgers outside the billing service by {settledBy ?? "unknown"} (recording only — no money moved here).");
            diagnostics.Add($"ConstructionProgressBillingService: {contract.ContractId} obligation {obligationId} recorded as settled externally by {settledBy} on day {dayIndex}.");
            return null;
        }

        /// <summary>
        /// D4H: records the explicit binding between a payment milestone and
        /// the acceptance stage that earns it. <see cref="RecordMilestone"/>
        /// bills only through a recorded link — this is that record. Refuses
        /// unknown milestones/stages rather than guessing.
        /// </summary>
        public string RecordMilestoneStageLink(ConstructionContract contract, string milestoneId,
            string stageId, int dayIndex, List<string> diagnostics)
        {
            diagnostics = diagnostics ?? new List<string>();
            if (contract == null) return "ConstructionProgressBillingService: no contract supplied.";
            if (FindMilestone(contract, milestoneId) == null)
                return $"ConstructionProgressBillingService: {contract.ContractId} has no milestone '{milestoneId}' in its payment terms.";
            if (contract.FindStage(stageId) == null)
                return $"ConstructionProgressBillingService: {contract.ContractId} has no stage '{stageId}'.";

            ConstructionMilestoneStageLink existing = FindLink(contract.ContractId, milestoneId);
            if (existing != null)
            {
                existing.StageId = stageId;
                existing.RecordedDayIndex = dayIndex;
            }
            else
            {
                milestoneStageLinks.Add(new ConstructionMilestoneStageLink
                {
                    ContractId = contract.ContractId,
                    MilestoneId = milestoneId,
                    StageId = stageId,
                    RecordedDayIndex = dayIndex,
                });
            }
            diagnostics.Add($"ConstructionProgressBillingService: {contract.ContractId} milestone '{milestoneId}' linked to stage '{stageId}' (day {dayIndex}).");
            return null;
        }

        /// <summary>
        /// D4H: cumulative released billing for one contract — the sum of
        /// every billing event's moved cents (deposits, milestone draws,
        /// retainage releases, completion balances, external settlements).
        /// The overbilling guard caps this at the contract price.
        /// </summary>
        public int CumulativeBilledCents(string contractId)
        {
            int total = 0;
            foreach (ConstructionBillingEvent e in EventsForContract(contractId))
            {
                total += Math.Max(0, e.AmountCents);
            }
            return total;
        }

        /// <summary>D4H: the retainage currently held back for one contract (the owner's money until final acceptance).</summary>
        public int RetainageBalanceCents(string contractId)
        {
            if (string.IsNullOrWhiteSpace(contractId)) return 0;
            return retainageByContract.TryGetValue(contractId, out int cents) ? Math.Max(0, cents) : 0;
        }

        // ---------- internals ----------

        private static ConstructionPaymentObligation FindObligation(ConstructionContract contract, string kind)
        {
            if (contract == null || contract.PaymentObligations == null || string.IsNullOrWhiteSpace(kind)) return null;
            foreach (ConstructionPaymentObligation o in contract.PaymentObligations)
            {
                if (o != null && string.Equals(o.Kind, kind, StringComparison.Ordinal)) return o;
            }
            return null;
        }

        private static ConstructionPaymentMilestone FindMilestone(ConstructionContract contract, string milestoneId)
        {
            if (contract == null || contract.PaymentTerms == null || contract.PaymentTerms.Milestones == null
                || string.IsNullOrWhiteSpace(milestoneId)) return null;
            foreach (ConstructionPaymentMilestone m in contract.PaymentTerms.Milestones)
            {
                if (m != null && string.Equals(m.MilestoneId, milestoneId, StringComparison.Ordinal)) return m;
            }
            return null;
        }

        private ConstructionMilestoneStageLink FindLink(string contractId, string milestoneId)
        {
            foreach (ConstructionMilestoneStageLink link in milestoneStageLinks)
            {
                if (link != null
                    && string.Equals(link.ContractId, contractId, StringComparison.Ordinal)
                    && string.Equals(link.MilestoneId, milestoneId, StringComparison.Ordinal))
                    return link;
            }
            return null;
        }

        private static string CheckContractBillable(ConstructionContract contract, string method,
            bool allowPreWork, List<string> diagnostics)
        {
            if (contract == null) return "ConstructionProgressBillingService: no contract supplied.";
            switch (contract.Status)
            {
                case ConstructionContractStatus.Executed:
                    if (!allowPreWork)
                        return $"ConstructionProgressBillingService: {contract.ContractId} is Executed — work has not started; {method} draws never run ahead of work.";
                    return null;
                case ConstructionContractStatus.InProgress:
                case ConstructionContractStatus.AcceptanceInProgress:
                case ConstructionContractStatus.Accepted:
                    return null;
                default:
                    return $"ConstructionProgressBillingService: {contract.ContractId} is {contract.Status} — {method} refused.";
            }
        }

        private string CheckOverbilling(ConstructionContract contract, int newReleaseCents, List<string> diagnostics)
        {
            int ceiling = Math.Max(0, contract.AgreedPriceCents);
            int cumulative = CumulativeBilledCents(contract.ContractId);
            if (cumulative + newReleaseCents > ceiling)
            {
                diagnostics.Add($"ConstructionProgressBillingService: {contract.ContractId} OVERBILLING GUARD — cumulative {FormatMoney(cumulative)} + draw {FormatMoney(newReleaseCents)} would exceed the contract price {FormatMoney(ceiling)} (accepted change orders included).");
                return $"ConstructionProgressBillingService: {contract.ContractId} — cumulative draws {FormatMoney(cumulative)} + this draw {FormatMoney(newReleaseCents)} would exceed the contract price {FormatMoney(ceiling)}; overbilling refused. Adjust the price with an accepted change order first.";
            }
            return null;
        }

        private static string ResolvePartyKey(ConstructionContractParty party, string role,
            string contractId, List<string> diagnostics)
        {
            if (party == null || !party.IsReal)
            {
                diagnostics.Add($"ConstructionProgressBillingService: {contractId} — the {role} party is not real; no money moves without two real parties.");
                return null;
            }
            string key = ConstructionBillingPartyKeys.ForParty(party);
            if (string.IsNullOrWhiteSpace(key) || key.EndsWith(":", StringComparison.Ordinal))
            {
                diagnostics.Add($"ConstructionProgressBillingService: {contractId} — the {role} party has no usable cash key; refusing rather than guessing.");
                return null;
            }
            return key;
        }

        private void AddRetainage(string contractId, int retainedCents)
        {
            if (retainedCents <= 0 || string.IsNullOrWhiteSpace(contractId)) return;
            retainageByContract.TryGetValue(contractId, out int current);
            retainageByContract[contractId] = current + retainedCents;
        }

        private void ClearRetainage(string contractId)
        {
            if (!string.IsNullOrWhiteSpace(contractId)) retainageByContract.Remove(contractId);
        }

        private void RecordEvent(string contractId, string obligationId, string kind,
            int amountCents, int dayIndex, string note)
        {
            events.Add(new ConstructionBillingEvent
            {
                EventId = $"BEVT-{nextEventNumber++:D4}",
                ContractId = contractId,
                ObligationId = obligationId,
                Kind = kind,
                AmountCents = Math.Max(0, amountCents),
                DayIndex = dayIndex,
                Note = note ?? string.Empty,
            });
        }

        private static string FormatMoney(int cents)
        {
            return "$" + (Math.Max(0, cents) / 100f).ToString("N2");
        }

        // ---------- save DTO (inside the owning service class) ----------

        [Serializable]
        public sealed class ConstructionProgressBillingServiceSaveDto
        {
            public List<ConstructionBillingEvent> Events = new List<ConstructionBillingEvent>();
            public List<ConstructionMilestoneStageLink> MilestoneStageLinks = new List<ConstructionMilestoneStageLink>();
            public List<ConstructionRetainageLedgerLine> RetainageLines = new List<ConstructionRetainageLedgerLine>();
            public ConstructionProgressBillingPolicy Policy = new ConstructionProgressBillingPolicy();
            public int NextEventNumber = 1;
            public int NextObligationNumber = 1;
        }

        public ConstructionProgressBillingServiceSaveDto CaptureSaveDto()
        {
            var dto = new ConstructionProgressBillingServiceSaveDto
            {
                Policy = Policy ?? new ConstructionProgressBillingPolicy(),
                NextEventNumber = Math.Max(1, nextEventNumber),
                NextObligationNumber = Math.Max(1, nextObligationNumber),
            };
            foreach (ConstructionBillingEvent e in events)
            {
                if (e == null) continue;
                dto.Events.Add(new ConstructionBillingEvent
                {
                    EventId = e.EventId,
                    ContractId = e.ContractId,
                    ObligationId = e.ObligationId,
                    Kind = e.Kind,
                    AmountCents = Math.Max(0, e.AmountCents),
                    DayIndex = e.DayIndex,
                    Note = e.Note ?? string.Empty,
                });
            }
            foreach (ConstructionMilestoneStageLink link in milestoneStageLinks)
            {
                if (link == null) continue;
                dto.MilestoneStageLinks.Add(new ConstructionMilestoneStageLink
                {
                    ContractId = link.ContractId,
                    MilestoneId = link.MilestoneId,
                    StageId = link.StageId,
                    RecordedDayIndex = link.RecordedDayIndex,
                });
            }
            foreach (KeyValuePair<string, int> kvp in retainageByContract)
            {
                dto.RetainageLines.Add(new ConstructionRetainageLedgerLine
                {
                    ContractId = kvp.Key,
                    RetainedCents = Math.Max(0, kvp.Value),
                });
            }
            return dto;
        }

        public void LoadFromSaveDto(ConstructionProgressBillingServiceSaveDto dto)
        {
            events.Clear();
            milestoneStageLinks.Clear();
            retainageByContract.Clear();
            if (dto == null)
            {
                nextEventNumber = 1;
                nextObligationNumber = 1;
                Policy = new ConstructionProgressBillingPolicy();
                return;
            }
            foreach (ConstructionBillingEvent e in dto.Events)
            {
                if (e != null) events.Add(e);
            }
            foreach (ConstructionMilestoneStageLink link in dto.MilestoneStageLinks)
            {
                if (link != null) milestoneStageLinks.Add(link);
            }
            foreach (ConstructionRetainageLedgerLine line in dto.RetainageLines)
            {
                if (line != null && !string.IsNullOrWhiteSpace(line.ContractId))
                    retainageByContract[line.ContractId] = Math.Max(0, line.RetainedCents);
            }
            Policy = dto.Policy ?? new ConstructionProgressBillingPolicy();
            nextEventNumber = Math.Max(1, dto.NextEventNumber);
            nextObligationNumber = Math.Max(1, dto.NextObligationNumber);
        }
    }
}
