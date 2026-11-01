using System;
using System.Collections.Generic;
using LandLedgers.Economy.Financing;
using LandLedgers.Primitives;

namespace LandLedgers.Population
{
    /// <summary>
    /// Pass V P2-2: the credit port a distressed household borrows through.
    /// The facility is a REAL lender (a private individual with finite funds
    /// and a real cash purse). Lender judgment runs through
    /// <see cref="PrivateLenderCreditParticipant.EvaluateOffer"/> and the cash
    /// advance through <see cref="PrivateLenderCreditParticipant.AcceptOffer"/>:
    /// a refusal returns null and creates nothing — refusal never mints money.
    /// </summary>
    public interface IHouseholdCreditFacility
    {
        string CreditorName { get; }
        int LendableCents();
        /// <summary>
        /// Runs the full lender judgment and, on acceptance, advances real
        /// cash and records a real obligation. Returns the obligation, or
        /// null when the lender refuses or the advance fails.
        /// </summary>
        FinancialObligation RequestLoan(int borrowerHouseholdId, int amountCents,
            int dayIndex, List<string> diagnostics);
    }

    /// <summary>
    /// Pass V P2-2: production credit facility — a private lender's judgment
    /// and finite purse, exposed to household hardship borrowing. The cash
    /// advance flows through the credit bridge into the household's real cash
    /// store (backed by its ledger), and the debt through the single
    /// FinancialObligationAuthority.
    /// </summary>
    public sealed class PrivateLenderHouseholdCreditFacility : IHouseholdCreditFacility
    {
        private readonly PrivateLenderCreditParticipant participant;
        private readonly CreditOfferWorkflow workflow;
        private readonly FinancialObligationAuthority authority;
        private readonly CreditCashBridge cash;
        private readonly EntityIdRegistry ids;
        private readonly CreditEventLog events;

        public PrivateLenderHouseholdCreditFacility(
            PrivateLenderCreditParticipant participant,
            CreditOfferWorkflow workflow,
            FinancialObligationAuthority authority,
            CreditCashBridge cash,
            EntityIdRegistry ids,
            CreditEventLog events)
        {
            this.participant = participant;
            this.workflow = workflow;
            this.authority = authority;
            this.cash = cash;
            this.ids = ids;
            this.events = events;
        }

        public string CreditorName => participant != null ? participant.LenderName : string.Empty;

        public int LendableCents() => participant != null ? participant.LendableCents() : 0;

        public FinancialObligation RequestLoan(int borrowerHouseholdId, int amountCents,
            int dayIndex, List<string> diagnostics)
        {
            diagnostics = diagnostics ?? new List<string>();
            if (participant == null || workflow == null || authority == null || cash == null || ids == null)
            {
                diagnostics.Add("HouseholdCredit: facility not fully wired — refusing to invent credit.");
                return null;
            }

            if (amountCents <= 0)
            {
                diagnostics.Add("HouseholdCredit: non-positive loan request refused.");
                return null;
            }

            string borrowerKey = "household:" + borrowerHouseholdId;
            var request = new CreditRequest
            {
                RequestId = $"hhloan-{borrowerHouseholdId}-{dayIndex}-{amountCents}",
                Borrower = borrowerKey,
                Lender = participant.LenderName,
                RequestedAmountCents = amountCents,
                Purpose = "household hardship: rent and food",
                DesiredTermDays = 180,
                RequiredFundingDayIndex = dayIndex,
                RepaymentSource = "future wages and household income",
            };

            CreditOffer offer = participant.EvaluateOffer(ids, workflow, authority, request, dayIndex, events, diagnostics);
            if (offer == null || offer.Status != CreditOfferStatus.Proposed)
            {
                // Refusal is already recorded by the participant (evidence,
                // policy, or lendable limits). Nothing was created.
                diagnostics.Add($"HouseholdCredit: {participant.LenderName} refused the {amountCents}c request " +
                    $"({offer?.DecisionReason ?? "no offer"}). No debt recorded, no cash moved.");
                return null;
            }

            FinancialObligation obligation = participant.AcceptOffer(
                ids, workflow, authority, cash, offer.OfferId, borrowerKey, dayIndex, events, diagnostics);
            if (obligation == null)
            {
                diagnostics.Add($"HouseholdCredit: {participant.LenderName} accepted in judgment but the advance failed — no debt recorded, no cash moved.");
                return null;
            }

            diagnostics.Add($"HouseholdCredit: {participant.LenderName} advanced {obligation.OutstandingPrincipalCents}c to household {borrowerHouseholdId} " +
                $"(obligation {obligation.ObligationId}) — real cash, real debt.");
            return obligation;
        }
    }

    /// <summary>
    /// Pass V P2-2: the durable registry of private individuals willing to
    /// lend to households. Lenders are REAL persons with finite capital and a
    /// real purse; world generation (P1-5) registers wealthy NPCs here with
    /// documented capital sources. A household can only borrow from a lender
    /// it actually knows (contact graph), and only what the lender can truly
    /// advance. No registered lender, or no known lender, means no credit —
    /// never a guaranteed loan.
    /// </summary>
    public sealed class HouseholdLenderDirectory
    {
        private sealed class LenderRecord
        {
            public string LenderName = string.Empty;
            public int LenderPersonId = -1;
            public PrivateLenderFunds Funds;
            public PurseCashStore Purse;
            public CreditLenderPolicy Policy;
        }

        private readonly Dictionary<string, LenderRecord> lenders =
            new Dictionary<string, LenderRecord>(StringComparer.Ordinal);
        private readonly List<string> diagnostics = new List<string>();
        public IReadOnlyList<string> Diagnostics => diagnostics;

        // Hub services bound at composition time (not serialized).
        private CreditOfferWorkflow workflow;
        private FinancialObligationAuthority authority;
        private CreditCashBridge cash;
        private EntityIdRegistry ids;
        private CreditEventLog events;

        public HouseholdLenderDirectory() { }

        public void BindHubServices(CreditOfferWorkflow workflow, FinancialObligationAuthority authority,
            CreditCashBridge cash, EntityIdRegistry ids, CreditEventLog events)
        {
            this.workflow = workflow;
            this.authority = authority;
            this.cash = cash;
            this.ids = ids;
            this.events = events;
        }

        /// <summary>
        /// Registers a private individual as a household lender. Capital must
        /// be documented (the purse's opening source names where it came
        /// from); the purse is registered in the credit bridge under the
        /// lender's name so advances move real cash.
        /// </summary>
        public string RegisterPrivateLender(string lenderName, int lenderPersonId,
            int capitalCents, string capitalSource, CreditLenderPolicy policy = null)
        {
            if (string.IsNullOrWhiteSpace(lenderName))
                return "HouseholdLenderDirectory: a lender must be named.";
            if (capitalCents <= 0)
                return $"HouseholdLenderDirectory: {lenderName} has no capital to lend.";
            if (string.IsNullOrWhiteSpace(capitalSource))
                return $"HouseholdLenderDirectory: {lenderName}'s capital needs a documented source — cash is never conjured.";

            var purse = new PurseCashStore(lenderName, capitalCents, capitalSource);
            var record = new LenderRecord
            {
                LenderName = lenderName,
                LenderPersonId = lenderPersonId,
                Funds = new PrivateLenderFunds(lenderName, capitalCents),
                Purse = purse,
                Policy = policy ?? CreditLenderPolicy.ForPrivateIndividual(),
            };
            lenders[lenderName] = record;
            cash?.Register(lenderName, purse);
            diagnostics.Add($"HouseholdLenderDirectory: registered private lender '{lenderName}' with {capitalCents}c ({capitalSource}).");
            return null;
        }

        public bool HasLender(string lenderName) => lenders.ContainsKey(lenderName ?? string.Empty);

        /// <summary>
        /// Finds the best facility for a household: among registered lenders
        /// the household's members actually know, the one with the most
        /// lendable cents. Returns null when there is no known lender —
        /// the honest "no credit available" signal.
        /// </summary>
        public IHouseholdCreditFacility FindFacilityFor(IEnumerable<int> memberPersonIds,
            Func<int, IEnumerable<int>> contactsOf, Func<int, string> personNameOf)
        {
            if (memberPersonIds == null || contactsOf == null || personNameOf == null) return null;
            if (workflow == null || authority == null || cash == null || ids == null) return null;

            var knownNames = new HashSet<string>(StringComparer.Ordinal);
            foreach (int memberId in memberPersonIds)
            {
                foreach (int contactId in contactsOf(memberId) ?? Array.Empty<int>())
                {
                    string name = personNameOf(contactId);
                    if (!string.IsNullOrWhiteSpace(name)) knownNames.Add(name);
                }
            }

            LenderRecord best = null;
            int bestLendable = 0;
            foreach (LenderRecord record in lenders.Values)
            {
                if (!knownNames.Contains(record.LenderName)) continue;
                var participant = new PrivateLenderCreditParticipant(
                    record.LenderName, record.Funds, record.Purse, record.Policy);
                int lendable = participant.LendableCents();
                if (lendable > bestLendable)
                {
                    bestLendable = lendable;
                    best = record;
                }
            }

            if (best == null) return null;
            var bestParticipant = new PrivateLenderCreditParticipant(
                best.LenderName, best.Funds, best.Purse, best.Policy);
            return new PrivateLenderHouseholdCreditFacility(
                bestParticipant, workflow, authority, cash, ids, events);
        }

        public int RegisteredLenderCount => lenders.Count;

        // ================= save/load (clear-then-load) =================

        [Serializable]
        public sealed class HouseholdLenderDirectorySaveDto
        {
            public List<LenderSaveEntry> Lenders = new List<LenderSaveEntry>();
        }

        [Serializable]
        public sealed class LenderSaveEntry
        {
            public string LenderName = string.Empty;
            public int LenderPersonId = -1;
            public int CapitalCents;
            public int PurseBalanceCents;
            public string CapitalSource = string.Empty;
        }

        public HouseholdLenderDirectorySaveDto CaptureSaveDto()
        {
            var dto = new HouseholdLenderDirectorySaveDto();
            foreach (LenderRecord record in lenders.Values)
            {
                dto.Lenders.Add(new LenderSaveEntry
                {
                    LenderName = record.LenderName,
                    LenderPersonId = record.LenderPersonId,
                    CapitalCents = record.Funds != null ? Math.Max(0, record.Funds.CapitalCents) : 0,
                    PurseBalanceCents = record.Purse != null ? Math.Max(0, record.Purse.ReadBalanceCents()) : 0,
                    CapitalSource = string.Empty, // source was documented at registration; balance restores from purse
                });
            }

            return dto;
        }

        public void LoadFromSaveDto(HouseholdLenderDirectorySaveDto dto)
        {
            lenders.Clear();
            diagnostics.Clear();
            if (dto?.Lenders == null) return;
            foreach (LenderSaveEntry entry in dto.Lenders)
            {
                if (entry == null || string.IsNullOrWhiteSpace(entry.LenderName)) continue;
                // The purse re-opens from the saved balance with the saved
                // capital as its documented source (the original source was
                // recorded in the registration diagnostics at world-gen).
                var purse = new PurseCashStore(entry.LenderName,
                    Math.Max(0, entry.PurseBalanceCents),
                    $"restored lender capital ({Math.Max(0, entry.CapitalCents)}c documented at registration)");
                var record = new LenderRecord
                {
                    LenderName = entry.LenderName,
                    LenderPersonId = entry.LenderPersonId,
                    Funds = new PrivateLenderFunds(entry.LenderName, Math.Max(0, entry.CapitalCents)),
                    Purse = purse,
                    Policy = CreditLenderPolicy.ForPrivateIndividual(),
                };
                lenders[entry.LenderName] = record;
                cash?.Register(entry.LenderName, purse);
            }
        }
    }
}
