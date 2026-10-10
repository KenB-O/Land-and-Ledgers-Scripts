using System;
using LandLedgers.Primitives;

namespace LandLedgers.Economy.Financing
{
    public enum CreditParticipantRole
    {
        PrivatePerson = 0,
        Business = 1,
        Bank = 2,
        Seller = 3,
        Supplier = 4
    }

    public enum CreditDelinquencyResponse
    {
        Tolerate = 0,
        RequestPayment = 1,
        OfferWorkout = 2,
        Accelerate = 3,
        EnforceSecurity = 4
    }

    /// <summary>
    /// Participant-facing finance workflow over the shared offer and
    /// obligation authorities. It owns neither debt balances nor a second
    /// ledger; it only supplies finite participant cash and policy decisions.
    /// </summary>
    [Serializable]
    public sealed class CreditParticipantService
    {
        public string ParticipantId { get; private set; }
        public CreditParticipantRole Role { get; private set; }
        public CreditCashAccount Cash { get; private set; }
        public float LendingWillingness01 { get; private set; }

        public CreditParticipantService(string participantId, CreditParticipantRole role,
            int cashCents, float lendingWillingness01 = 1f)
        {
            ParticipantId = participantId ?? string.Empty;
            Role = role;
            Cash = new CreditCashAccount(ParticipantId, cashCents);
            LendingWillingness01 = Math.Clamp(lendingWillingness01, 0f, 1f);
        }

        public CreditOffer ConsiderRequest(EntityIdRegistry ids, CreditOfferWorkflow workflow,
            FinancialObligationAuthority authority, CreditRequest request, int dayIndex)
        {
            if (ids == null || workflow == null || request == null
                || !string.Equals(request.Lender, ParticipantId, StringComparison.Ordinal)
                || LendingWillingness01 <= 0f)
                return null;

            return workflow.Evaluate(ids, request, authority, Cash.BalanceCents, dayIndex);
        }

        public FinancialObligation AcceptOffer(EntityIdRegistry ids, CreditOfferWorkflow workflow,
            FinancialObligationAuthority authority, string offerId, CreditCashAccount borrowerCash,
            int dayIndex)
        {
            if (workflow == null || authority == null || Cash == null) return null;
            return workflow.Accept(ids, offerId, authority, Cash, borrowerCash, dayIndex);
        }

        public FinancialPaymentRecord CollectPayment(CreditOfferWorkflow workflow,
            FinancialObligationAuthority authority, string obligationId, CreditCashAccount borrowerCash,
            int amountCents, int dayIndex, out string message)
        {
            message = string.Empty;
            if (workflow == null)
            {
                message = "Credit offer workflow is unavailable.";
                return null;
            }
            return workflow?.CollectPayment(obligationId, amountCents, dayIndex, authority,
                borrowerCash, Cash, out message);
        }

        public CreditDelinquencyResponse ChooseDelinquencyResponse(FinancialObligation obligation,
            int availableCashCents, bool securityAvailable, bool relationshipMatters)
        {
            if (obligation == null) return CreditDelinquencyResponse.RequestPayment;
            if (availableCashCents > 0 && relationshipMatters) return CreditDelinquencyResponse.Tolerate;
            if (securityAvailable && obligation.Status == FinancialObligationStatus.Defaulted)
                return CreditDelinquencyResponse.EnforceSecurity;
            if (obligation.Status == FinancialObligationStatus.Delinquent)
                return CreditDelinquencyResponse.OfferWorkout;
            return CreditDelinquencyResponse.RequestPayment;
        }
    }
}
