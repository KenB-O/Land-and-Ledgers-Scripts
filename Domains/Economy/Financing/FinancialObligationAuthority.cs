using System;
using System.Collections.Generic;
using LandLedgers.Economy.Legal;
using LandLedgers.Primitives;

namespace LandLedgers.Economy.Financing
{
    /// <summary>
    /// Shared financial-debt authority. Instruments and compatibility ledgers are
    /// documentary/read-model surfaces; this authority owns the mutable principal,
    /// accrued interest, payment history and lifecycle of each obligation.
    /// </summary>
    public enum FinancialObligationStatus
    {
        Proposed = 0,
        Active = 1,
        Due = 2,
        Delinquent = 3,
        Defaulted = 4,
        Satisfied = 5,
        Cancelled = 6,
        Enforced = 7,
    }

    public enum FinancialObligationKind
    {
        Loan = 0,
        Payable = 1,
        SellerFinance = 2,
        TradeCredit = 3,
        GuarantyCall = 4,
        Refinance = 5,
        Recovery = 6,
    }

    public enum FinancialPaymentStructure
    {
        InterestAndPrincipal = 0,
        InterestOnlyBalloon = 1,
        PrincipalInstallments = 2,
        MaturityPrincipal = 3,
        Demand = 4,
    }

    [Serializable]
    public sealed class FinancialPaymentTerms
    {
        public int AnnualInterestRateBps;
        public FinancialPaymentStructure Structure = FinancialPaymentStructure.MaturityPrincipal;
        public int PaymentIntervalDays;
        public int InstallmentPrincipalCents;
        public int BalloonPrincipalCents;
        public bool AllowsEarlyPayoff = true;
        public int PrepaymentFeeCents;
    }

    [Serializable]
    public sealed class FinancialPaymentRecord
    {
        public string PaymentId = string.Empty;
        public int DayIndex;
        public int AmountCents;
        public int InterestCents;
        public int PrincipalCents;
        public int FeesCents;
        public string SourceEconomicEventId = string.Empty;
        public string Payer = string.Empty;
        public string Payee = string.Empty;
    }

    [Serializable]
    public sealed class SecurityInterestRecord
    {
        public string SecurityInterestId = string.Empty;
        public string ObligationId = string.Empty;
        public string Creditor = string.Empty;
        public string Grantor = string.Empty;
        public List<string> CollateralIds = new List<string>();
        public int AttachmentDayIndex;
        public string LegalRegimeId = string.Empty;
        public int Priority;
        public bool PermitsJuniorInterest = true;
        public bool Released;
        public string EnforcementState = string.Empty;
    }

    [Serializable]
    public sealed class GuarantyRecord
    {
        public string GuarantyId = string.Empty;
        public string CoveredObligationId = string.Empty;
        public string Creditor = string.Empty;
        public string PrimaryDebtor = string.Empty;
        public string Guarantor = string.Empty;
        public int MaximumExposureCents;
        public int CalledAmountCents;
        public string CalledObligationId = string.Empty;
        public bool Called;
        public bool Released;
        public string RecoveryObligationId = string.Empty;
    }

    [Serializable]
    public sealed class ObligationAssumptionRecord
    {
        public string AssumptionId = string.Empty;
        public string ObligationId = string.Empty;
        public string IncomingDebtor = string.Empty;
        public string OriginalDebtor = string.Empty;
        public bool OriginalDebtorReleased;
        public bool Novation;
        public bool CreditorConsented;
        public int DayIndex;
    }

    [Serializable]
    public sealed class ObligationLinkRecord
    {
        public string LinkId = string.Empty;
        public string PredecessorObligationId = string.Empty;
        public string SuccessorObligationId = string.Empty;
        public string LinkKind = string.Empty;
        public int DayIndex;
    }

    [Serializable]
    public sealed class FinancialObligation
    {
        public EntityId ObligationEntityId = EntityId.Invalid;
        public string ObligationId = string.Empty;
        public string AgreementId = string.Empty;
        public FinancialObligationKind Kind;
        public FinancialObligationStatus Status = FinancialObligationStatus.Active;
        public string Debtor = string.Empty;
        public string Creditor = string.Empty;
        public int OriginalPrincipalCents;
        public int OutstandingPrincipalCents;
        public int AccruedInterestCents;
        public int FeesCents;
        public int IssuedDayIndex;
        public int MaturityDayIndex = -1;
        public string Purpose = string.Empty;
        public string LegalRegimeId = string.Empty;
        public string Terms = string.Empty;
        public FinancialPaymentTerms PaymentTerms = new FinancialPaymentTerms();
        public string ScheduleReference = string.Empty;
        public string SourceEconomicEventId = string.Empty;
        public int LastInterestAccrualDayIndex = -1;
        public string PredecessorObligationId = string.Empty;
        public string SuccessorObligationId = string.Empty;
        public string AssumptionId = string.Empty;
        public bool OriginalDebtorReleased;
        public bool IsDemandObligation;
        public List<string> LiableParties = new List<string>();
        public List<string> AssumptionIds = new List<string>();
        public List<string> SecurityInterestIds = new List<string>();
        public List<string> GuarantyIds = new List<string>();
        public List<FinancialPaymentRecord> Payments = new List<FinancialPaymentRecord>();

        public int TotalOutstandingCents => Math.Max(0, OutstandingPrincipalCents + AccruedInterestCents + FeesCents);
        public bool Settled => TotalOutstandingCents <= 0 || Status == FinancialObligationStatus.Satisfied;
    }

    [Serializable]
    public sealed class CreditFacilityRecord
    {
        public string FacilityId = string.Empty;
        public string Creditor = string.Empty;
        public string Borrower = string.Empty;
        public int AuthorizedLimitCents;
        public int DrawnPrincipalCents;
        public int ReviewDayIndex = -1;
        public int ExpiryDayIndex = -1;
        public bool ReborrowingAllowed;
        public string Terms = string.Empty;
    }

    [Serializable]
    public sealed class FinancialObligationSaveDto
    {
        public List<FinancialObligation> Obligations = new List<FinancialObligation>();
        public List<SecurityInterestRecord> Securities = new List<SecurityInterestRecord>();
        public List<GuarantyRecord> Guaranties = new List<GuarantyRecord>();
        public List<CreditFacilityRecord> Facilities = new List<CreditFacilityRecord>();
        public List<ObligationAssumptionRecord> Assumptions = new List<ObligationAssumptionRecord>();
        public List<ObligationLinkRecord> Links = new List<ObligationLinkRecord>();
    }

    /// <summary>
    /// One mutable debt authority shared by private lenders, sellers, suppliers
    /// and banks. It deliberately does not move cash: the originating workflow
    /// records the corresponding cash/inventory event exactly once.
    /// </summary>
    public sealed class FinancialObligationAuthority
    {
        private readonly Dictionary<string, FinancialObligation> obligations =
            new Dictionary<string, FinancialObligation>(StringComparer.Ordinal);
        private readonly Dictionary<string, SecurityInterestRecord> securities =
            new Dictionary<string, SecurityInterestRecord>(StringComparer.Ordinal);
        private readonly Dictionary<string, GuarantyRecord> guaranties =
            new Dictionary<string, GuarantyRecord>(StringComparer.Ordinal);
        private readonly Dictionary<string, CreditFacilityRecord> facilities =
            new Dictionary<string, CreditFacilityRecord>(StringComparer.Ordinal);
        private readonly Dictionary<string, ObligationAssumptionRecord> assumptions =
            new Dictionary<string, ObligationAssumptionRecord>(StringComparer.Ordinal);
        private readonly Dictionary<string, ObligationLinkRecord> links =
            new Dictionary<string, ObligationLinkRecord>(StringComparer.Ordinal);

        public IReadOnlyCollection<FinancialObligation> Obligations => obligations.Values;
        public IReadOnlyCollection<SecurityInterestRecord> Securities => securities.Values;
        public IReadOnlyCollection<GuarantyRecord> Guaranties => guaranties.Values;
        public IReadOnlyCollection<CreditFacilityRecord> Facilities => facilities.Values;
        public IReadOnlyCollection<ObligationAssumptionRecord> Assumptions => assumptions.Values;
        public IReadOnlyCollection<ObligationLinkRecord> Links => links.Values;

        public FinancialObligation Create(
            EntityIdRegistry ids, FinancialObligationKind kind, string debtor,
            string creditor, int principalCents, int dayIndex, string terms,
            string purpose, string agreementId = "", int maturityDayIndex = -1,
            string sourceEconomicEventId = "")
        {
            if (ids == null || string.IsNullOrWhiteSpace(debtor) || string.IsNullOrWhiteSpace(creditor)
                || principalCents <= 0) return null;

            EntityId id = ids.Allocate(EntityKind.Contract);
            var obligation = new FinancialObligation
            {
                ObligationEntityId = id,
                ObligationId = id.ToString(), AgreementId = agreementId ?? string.Empty,
                Kind = kind, Debtor = debtor, Creditor = creditor,
                OriginalPrincipalCents = principalCents,
                OutstandingPrincipalCents = principalCents,
                IssuedDayIndex = dayIndex, Terms = terms ?? string.Empty,
                Purpose = purpose ?? string.Empty,
                MaturityDayIndex = maturityDayIndex,
                SourceEconomicEventId = sourceEconomicEventId ?? string.Empty,
            };
            obligations[obligation.ObligationId] = obligation;
            return obligation;
        }

        public FinancialObligation CreateWithTerms(EntityIdRegistry ids, FinancialObligationKind kind,
            string debtor, string creditor, int principalCents, int dayIndex, string terms,
            string purpose, FinancialPaymentTerms paymentTerms, string agreementId = "")
        {
            FinancialObligation obligation = Create(ids, kind, debtor, creditor, principalCents,
                dayIndex, terms, purpose, agreementId);
            if (obligation != null && paymentTerms != null) obligation.PaymentTerms = paymentTerms;
            return obligation;
        }

        /// <summary>
        /// Creation route for workflows that have a dated legal context. The
        /// legal regime affects whether the written rate may be accepted; the
        /// ordinary Create methods remain useful for already-authorized legacy
        /// records and migration.
        /// </summary>
        public FinancialObligation CreateWithLegalTerms(EntityIdRegistry ids, FinancialObligationKind kind,
            string debtor, string creditor, int principalCents, int dayIndex, string terms,
            string purpose, FinancialPaymentTerms paymentTerms, LegalRegime legalRegime,
            bool coveredBlackHillsCounty, out string rejection)
        {
            rejection = string.Empty;
            if (legalRegime == null) { rejection = "A dated legal regime is required."; return null; }
            double ratePercent = Math.Max(0, paymentTerms?.AnnualInterestRateBps ?? 0) / 100d;
            if (!legalRegime.IsAgreedRateLawful(ratePercent, dayIndex, coveredBlackHillsCounty, out rejection))
                return null;
            FinancialObligation obligation = CreateWithTerms(ids, kind, debtor, creditor, principalCents,
                dayIndex, terms, purpose, paymentTerms);
            if (obligation != null) obligation.LegalRegimeId = legalRegime.JurisdictionForDay(dayIndex).ToString();
            return obligation;
        }

        public FinancialObligation Find(string obligationId)
        {
            return !string.IsNullOrWhiteSpace(obligationId) && obligations.TryGetValue(obligationId, out var value)
                ? value : null;
        }

        public int TotalOutstandingFor(string debtor)
        {
            int total = 0;
            foreach (FinancialObligation obligation in obligations.Values)
            {
                if (obligation != null && string.Equals(obligation.Debtor, debtor, StringComparison.Ordinal)
                    && !obligation.Settled) total += obligation.TotalOutstandingCents;
            }
            return total;
        }

        public int TotalReceivableFor(string creditor)
        {
            int total = 0;
            foreach (FinancialObligation obligation in obligations.Values)
            {
                if (obligation != null && string.Equals(obligation.Creditor, creditor, StringComparison.Ordinal)
                    && !obligation.Settled) total += obligation.TotalOutstandingCents;
            }
            return total;
        }

        public string AccrueInterest(string obligationId, int amountCents)
        {
            FinancialObligation obligation = Find(obligationId);
            if (obligation == null) return "Unknown financial obligation.";
            if (amountCents <= 0) return "Interest must be positive.";
            if (obligation.Settled) return "Financial obligation is already settled.";
            obligation.AccruedInterestCents = checked(obligation.AccruedInterestCents + amountCents);
            return null;
        }

        public string AccrueInterestThroughDay(string obligationId, int dayIndex)
        {
            FinancialObligation obligation = Find(obligationId);
            if (obligation == null) return "Unknown financial obligation.";
            if (obligation.Settled) return null;
            if (dayIndex <= obligation.LastInterestAccrualDayIndex) return null;
            int startDay = obligation.LastInterestAccrualDayIndex < obligation.IssuedDayIndex
                ? obligation.IssuedDayIndex
                : obligation.LastInterestAccrualDayIndex;
            int elapsedDays = Math.Max(0, dayIndex - startDay);
            int annualRateBps = obligation.PaymentTerms != null
                ? Math.Max(0, obligation.PaymentTerms.AnnualInterestRateBps)
                : 0;
            if (elapsedDays > 0 && annualRateBps > 0 && obligation.OutstandingPrincipalCents > 0)
            {
                long numerator = (long)obligation.OutstandingPrincipalCents * annualRateBps * elapsedDays;
                int interest = (int)Math.Max(0, numerator / (365L * 10000L));
                if (interest > 0) obligation.AccruedInterestCents = checked(obligation.AccruedInterestCents + interest);
            }
            obligation.LastInterestAccrualDayIndex = dayIndex;
            return null;
        }

        public FinancialPaymentRecord ApplyPayment(
            string obligationId, int amountCents, int dayIndex, string payer,
            string payee, string sourceEconomicEventId = "", int feesFirst = 0)
        {
            FinancialObligation obligation = Find(obligationId);
            if (obligation == null || amountCents <= 0 || obligation.Settled
                || amountCents > obligation.TotalOutstandingCents) return null;
            int remaining = amountCents;
            int fees = Math.Min(remaining, Math.Max(0, feesFirst));
            remaining -= fees;
            int interest = Math.Min(remaining, obligation.AccruedInterestCents);
            remaining -= interest;
            int principal = Math.Min(remaining, obligation.OutstandingPrincipalCents);
            var payment = new FinancialPaymentRecord
            {
                PaymentId = obligationId + ":" + (obligation.Payments.Count + 1),
                DayIndex = dayIndex, AmountCents = fees + interest + principal,
                FeesCents = fees, InterestCents = interest, PrincipalCents = principal,
                Payer = payer ?? string.Empty, Payee = payee ?? string.Empty,
                SourceEconomicEventId = sourceEconomicEventId ?? string.Empty,
            };
            obligation.FeesCents -= fees;
            obligation.AccruedInterestCents -= interest;
            obligation.OutstandingPrincipalCents -= principal;
            obligation.Payments.Add(payment);
            if (obligation.Settled) obligation.Status = FinancialObligationStatus.Satisfied;
            return payment;
        }

        /// <summary>
        /// Resolves the amount contractually due at a day without mutating the
        /// obligation. The caller records cash and then applies the returned
        /// amount through ApplyPayment, preserving one payment authority.
        /// </summary>
        public int CalculateDueAmountCents(string obligationId, int dayIndex)
        {
            FinancialObligation obligation = Find(obligationId);
            if (obligation == null || obligation.Settled || obligation.PaymentTerms == null) return 0;
            FinancialPaymentTerms terms = obligation.PaymentTerms;
            bool maturityReached = obligation.MaturityDayIndex >= 0 && dayIndex >= obligation.MaturityDayIndex;
            bool dueNow = obligation.Status == FinancialObligationStatus.Due
                || obligation.Status == FinancialObligationStatus.Delinquent
                || obligation.Status == FinancialObligationStatus.Defaulted;
            int interest = Math.Max(0, obligation.AccruedInterestCents);
            if (!maturityReached && !dueNow && terms.Structure == FinancialPaymentStructure.MaturityPrincipal)
                return 0;
            if (!maturityReached && !dueNow && terms.Structure == FinancialPaymentStructure.InterestOnlyBalloon)
                return interest;
            if (terms.Structure == FinancialPaymentStructure.Demand && !dueNow && !maturityReached)
                return 0;
            int principal = maturityReached || terms.Structure == FinancialPaymentStructure.MaturityPrincipal
                ? obligation.OutstandingPrincipalCents
                : Math.Min(obligation.OutstandingPrincipalCents, Math.Max(0, terms.InstallmentPrincipalCents));
            if (terms.Structure == FinancialPaymentStructure.InterestOnlyBalloon && maturityReached)
                principal = obligation.OutstandingPrincipalCents;
            return Math.Max(0, principal + interest + obligation.FeesCents);
        }

        public FinancialPaymentRecord ApplyScheduledPayment(string obligationId, int dayIndex,
            string payer, string payee, string sourceEconomicEventId = "")
        {
            int due = CalculateDueAmountCents(obligationId, dayIndex);
            return due > 0 ? ApplyPayment(obligationId, due, dayIndex, payer, payee, sourceEconomicEventId) : null;
        }

        public int CalculateEarlyPayoffAmountCents(string obligationId)
        {
            FinancialObligation obligation = Find(obligationId);
            if (obligation == null || obligation.Settled) return 0;
            if (obligation.PaymentTerms != null && !obligation.PaymentTerms.AllowsEarlyPayoff) return -1;
            return Math.Max(0, obligation.TotalOutstandingCents + (obligation.PaymentTerms?.PrepaymentFeeCents ?? 0));
        }

        public bool MarkDelinquent(string obligationId)
        {
            FinancialObligation obligation = Find(obligationId);
            if (obligation == null || obligation.Settled) return false;
            obligation.Status = FinancialObligationStatus.Delinquent;
            return true;
        }

        public bool MarkDue(string obligationId)
        {
            FinancialObligation obligation = Find(obligationId);
            if (obligation == null || obligation.Settled) return false;
            obligation.Status = FinancialObligationStatus.Due;
            return true;
        }

        public bool MarkDefaulted(string obligationId)
        {
            FinancialObligation obligation = Find(obligationId);
            if (obligation == null || obligation.Settled) return false;
            obligation.Status = FinancialObligationStatus.Defaulted;
            return true;
        }

        public bool SetLegalRegimeContext(string obligationId, string legalRegimeId)
        {
            FinancialObligation obligation = Find(obligationId);
            if (obligation == null) return false;
            obligation.LegalRegimeId = legalRegimeId ?? string.Empty;
            for (int i = 0; i < obligation.SecurityInterestIds.Count; i++)
            {
                if (securities.TryGetValue(obligation.SecurityInterestIds[i], out SecurityInterestRecord security))
                    security.LegalRegimeId = obligation.LegalRegimeId;
            }
            return true;
        }

        public ObligationAssumptionRecord RecordAssumption(EntityIdRegistry ids, string obligationId,
            string incomingDebtor, bool releaseOriginal, bool novation, bool creditorConsented, int dayIndex)
        {
            FinancialObligation obligation = Find(obligationId);
            if (ids == null || obligation == null || string.IsNullOrWhiteSpace(incomingDebtor)) return null;
            if (novation && !creditorConsented) return null;
            var assumption = new ObligationAssumptionRecord
            {
                AssumptionId = ids.Allocate(EntityKind.Contract).ToString(), ObligationId = obligationId,
                IncomingDebtor = incomingDebtor, OriginalDebtor = obligation.Debtor,
                OriginalDebtorReleased = releaseOriginal, Novation = novation,
                CreditorConsented = creditorConsented, DayIndex = dayIndex,
            };
            assumptions[assumption.AssumptionId] = assumption;
            obligation.AssumptionIds.Add(assumption.AssumptionId);
            if (!obligation.LiableParties.Contains(incomingDebtor)) obligation.LiableParties.Add(incomingDebtor);
            if (novation)
            {
                obligation.Debtor = incomingDebtor;
                obligation.OriginalDebtorReleased = releaseOriginal;
            }
            return assumption;
        }

        public FinancialObligation ConvertPayableToNote(EntityIdRegistry ids, string payableId,
            string terms, int dayIndex, string agreementId = "")
        {
            FinancialObligation payable = Find(payableId);
            if (ids == null || payable == null || payable.Kind != FinancialObligationKind.Payable || payable.Settled)
                return null;
            FinancialObligation note = Create(ids, FinancialObligationKind.SellerFinance,
                payable.Debtor, payable.Creditor, payable.TotalOutstandingCents, dayIndex,
                terms, "payable restructuring", agreementId);
            if (note == null) return null;
            payable.OutstandingPrincipalCents = 0;
            payable.AccruedInterestCents = 0;
            payable.FeesCents = 0;
            payable.Status = FinancialObligationStatus.Satisfied;
            Link(payable, note, "account-to-note", ids, dayIndex);
            return note;
        }

        public FinancialObligation Refinance(EntityIdRegistry ids, IEnumerable<string> predecessorIds,
            string debtor, string creditor, int principalCents, int dayIndex, string terms, string purpose)
        {
            if (ids == null || predecessorIds == null) return null;
            var predecessors = new List<FinancialObligation>();
            foreach (string id in predecessorIds)
            {
                FinancialObligation predecessor = Find(id);
                if (predecessor == null || predecessor.Settled) return null;
                predecessors.Add(predecessor);
            }
            FinancialObligation successor = Create(ids, FinancialObligationKind.Refinance,
                debtor, creditor, principalCents, dayIndex, terms, purpose);
            if (successor == null) return null;
            foreach (FinancialObligation predecessor in predecessors)
            {
                predecessor.OutstandingPrincipalCents = 0;
                predecessor.AccruedInterestCents = 0;
                predecessor.FeesCents = 0;
                predecessor.Status = FinancialObligationStatus.Satisfied;
                Link(predecessor, successor, "refinance", ids, dayIndex);
            }
            return successor;
        }

        private void Link(FinancialObligation predecessor, FinancialObligation successor,
            string kind, EntityIdRegistry ids, int dayIndex)
        {
            predecessor.SuccessorObligationId = successor.ObligationId;
            successor.PredecessorObligationId = predecessor.ObligationId;
            var link = new ObligationLinkRecord
            {
                LinkId = ids.Allocate(EntityKind.Contract).ToString(),
                PredecessorObligationId = predecessor.ObligationId,
                SuccessorObligationId = successor.ObligationId,
                LinkKind = kind, DayIndex = dayIndex,
            };
            links[link.LinkId] = link;
        }

        public SecurityInterestRecord AttachSecurity(
            EntityIdRegistry ids, string obligationId, string creditor, string grantor,
            IEnumerable<string> collateralIds, int dayIndex, int priority = 1,
            bool permitsJuniorInterest = true)
        {
            FinancialObligation obligation = Find(obligationId);
            if (ids == null || obligation == null || string.IsNullOrWhiteSpace(creditor)
                || string.IsNullOrWhiteSpace(grantor)) return null;
            EntityId id = ids.Allocate(EntityKind.Contract);
            var security = new SecurityInterestRecord
            {
                SecurityInterestId = id.ToString(), ObligationId = obligationId,
                Creditor = creditor, Grantor = grantor, AttachmentDayIndex = dayIndex,
                Priority = Math.Max(1, priority), PermitsJuniorInterest = permitsJuniorInterest,
            };
            if (collateralIds != null) security.CollateralIds.AddRange(collateralIds);
            securities[security.SecurityInterestId] = security;
            obligation.SecurityInterestIds.Add(security.SecurityInterestId);
            return security;
        }

        /// <summary>
        /// Dated-law attachment route for collateral whose legal character is
        /// known to the caller.  Homestead encumbrance requires the recorded
        /// spousal participation recognized by the active LegalRegime; this
        /// check affects attachment only and never transfers title.
        /// </summary>
        public SecurityInterestRecord AttachSecurityWithLegalRegime(
            EntityIdRegistry ids, string obligationId, string creditor, string grantor,
            IEnumerable<string> collateralIds, int dayIndex, LegalRegime legalRegime,
            bool collateralIsHomestead, bool spousalParticipationRecorded,
            int priority, bool permitsJuniorInterest, out string rejection)
        {
            rejection = string.Empty;
            if (legalRegime == null)
            {
                rejection = "A dated legal regime is required for this security attachment.";
                return null;
            }
            if (collateralIsHomestead
                && legalRegime.SpousalParticipationRequiredForHomesteadTransferOrEncumbrance(dayIndex)
                && !spousalParticipationRecorded)
            {
                rejection = "Homestead encumbrance requires recorded spousal participation under the active legal regime.";
                return null;
            }
            SecurityInterestRecord security = AttachSecurity(ids, obligationId, creditor, grantor,
                collateralIds, dayIndex, priority, permitsJuniorInterest);
            if (security != null) security.LegalRegimeId = legalRegime.JurisdictionForDay(dayIndex).ToString();
            return security;
        }

        public bool ReleaseSecurity(string securityInterestId, string reason = "")
        {
            if (!securities.TryGetValue(securityInterestId ?? string.Empty, out SecurityInterestRecord security)
                || security.Released) return false;
            security.Released = true;
            security.EnforcementState = string.IsNullOrWhiteSpace(reason) ? "released" : "released: " + reason;
            return true;
        }

        public GuarantyRecord AddGuaranty(
            EntityIdRegistry ids, string obligationId, string creditor, string debtor,
            string guarantor, int maximumExposureCents)
        {
            FinancialObligation obligation = Find(obligationId);
            if (ids == null || obligation == null || string.IsNullOrWhiteSpace(guarantor)
                || maximumExposureCents <= 0) return null;
            EntityId id = ids.Allocate(EntityKind.Contract);
            var guaranty = new GuarantyRecord
            {
                GuarantyId = id.ToString(), CoveredObligationId = obligationId,
                Creditor = creditor ?? string.Empty, PrimaryDebtor = debtor ?? string.Empty,
                Guarantor = guarantor, MaximumExposureCents = maximumExposureCents,
            };
            guaranties[guaranty.GuarantyId] = guaranty;
            obligation.GuarantyIds.Add(guaranty.GuarantyId);
            return guaranty;
        }

        public FinancialObligation CallGuaranty(EntityIdRegistry ids, string guarantyId, int amountCents, int dayIndex)
        {
            if (!guaranties.TryGetValue(guarantyId ?? string.Empty, out GuarantyRecord guaranty)
                || guaranty.Called || guaranty.Released) return null;
            FinancialObligation covered = Find(guaranty.CoveredObligationId);
            if (covered == null) return null;
            int amount = Math.Min(Math.Max(0, amountCents), guaranty.MaximumExposureCents);
            amount = Math.Min(amount, covered.TotalOutstandingCents);
            if (amount <= 0) return null;
            guaranty.Called = true;
            guaranty.CalledAmountCents = amount;
            FinancialObligation called = Create(ids, FinancialObligationKind.GuarantyCall, guaranty.Guarantor,
                guaranty.Creditor, amount, dayIndex, "guaranty call", "guaranty", guaranty.GuarantyId);
            if (called != null) guaranty.CalledObligationId = called.ObligationId;
            return called;
        }

        /// <summary>
        /// Records one guarantor payment against the creditor claim and the
        /// called guaranty exposure, then creates the guarantor's subrogation
        /// claim against the primary debtor. The creditor is not paid twice.
        /// </summary>
        public FinancialObligation SettleGuarantyPayment(EntityIdRegistry ids, string guarantyId,
            int amountCents, int dayIndex, string economicEventId = "")
        {
            if (!guaranties.TryGetValue(guarantyId ?? string.Empty, out GuarantyRecord guaranty)
                || !guaranty.Called || string.IsNullOrWhiteSpace(guaranty.CalledObligationId)) return null;
            FinancialObligation called = Find(guaranty.CalledObligationId);
            FinancialObligation covered = Find(guaranty.CoveredObligationId);
            if (called == null || covered == null || amountCents <= 0) return null;
            int amount = Math.Min(amountCents, Math.Min(called.TotalOutstandingCents, covered.TotalOutstandingCents));
            if (amount <= 0) return null;
            ApplyPayment(called.ObligationId, amount, dayIndex, guaranty.Guarantor, guaranty.Creditor, economicEventId);
            ApplyPayment(covered.ObligationId, amount, dayIndex, guaranty.Guarantor, guaranty.Creditor, economicEventId);
            FinancialObligation recovery = Create(ids, FinancialObligationKind.Recovery,
                guaranty.PrimaryDebtor, guaranty.Guarantor, amount, dayIndex,
                "subrogation/reimbursement", "guaranty recovery", guaranty.GuarantyId);
            if (recovery != null) guaranty.RecoveryObligationId = recovery.ObligationId;
            return recovery;
        }

        public CreditFacilityRecord CreateFacility(EntityIdRegistry ids, string creditor, string borrower,
            int limitCents, int reviewDayIndex, int expiryDayIndex, bool reborrowingAllowed, string terms)
        {
            if (ids == null || limitCents <= 0 || string.IsNullOrWhiteSpace(creditor)
                || string.IsNullOrWhiteSpace(borrower)) return null;
            var facility = new CreditFacilityRecord
            {
                FacilityId = ids.Allocate(EntityKind.Contract).ToString(), Creditor = creditor,
                Borrower = borrower, AuthorizedLimitCents = limitCents,
                ReviewDayIndex = reviewDayIndex, ExpiryDayIndex = expiryDayIndex,
                ReborrowingAllowed = reborrowingAllowed, Terms = terms ?? string.Empty,
            };
            facilities[facility.FacilityId] = facility;
            return facility;
        }

        public FinancialObligation DrawFacility(EntityIdRegistry ids, string facilityId, int amountCents, int dayIndex)
        {
            if (!facilities.TryGetValue(facilityId ?? string.Empty, out CreditFacilityRecord facility)
                || amountCents <= 0 || amountCents > facility.AuthorizedLimitCents - facility.DrawnPrincipalCents)
                return null;
            FinancialObligation draw = Create(ids, FinancialObligationKind.TradeCredit, facility.Borrower,
                facility.Creditor, amountCents, dayIndex, facility.Terms, "facility draw", facilityId);
            if (draw != null) facility.DrawnPrincipalCents += amountCents;
            return draw;
        }

        /// <summary>
        /// Draws against a facility while moving only the drawn amount between
        /// the explicit lender and borrower cash accounts.  Authorization is
        /// not debt and does not move cash; this is the executable draw path.
        /// </summary>
        public FinancialObligation DrawFacility(EntityIdRegistry ids, string facilityId,
            int amountCents, int dayIndex, CreditCashAccount lenderCash,
            CreditCashAccount borrowerCash, out string message)
        {
            message = string.Empty;
            if (lenderCash == null || borrowerCash == null)
            {
                message = "Both lender and borrower cash accounts are required.";
                return null;
            }
            if (!facilities.TryGetValue(facilityId ?? string.Empty, out CreditFacilityRecord facility))
            {
                message = "Unknown credit facility.";
                return null;
            }
            if (!string.Equals(lenderCash.Owner, facility.Creditor, StringComparison.Ordinal)
                || !string.Equals(borrowerCash.Owner, facility.Borrower, StringComparison.Ordinal))
            {
                message = "Cash accounts do not match the facility parties.";
                return null;
            }
            if (lenderCash.BalanceCents < amountCents)
            {
                message = "Lender liquidity is insufficient for this draw.";
                return null;
            }
            FinancialObligation draw = DrawFacility(ids, facilityId, amountCents, dayIndex);
            if (draw == null)
            {
                message = "The requested draw exceeds the remaining facility authority.";
                return null;
            }
            lenderCash.BalanceCents -= amountCents;
            borrowerCash.BalanceCents += amountCents;
            return draw;
        }

        public FinancialObligationSaveDto CaptureSaveDto()
        {
            var dto = new FinancialObligationSaveDto();
            dto.Obligations.AddRange(obligations.Values);
            dto.Securities.AddRange(securities.Values);
            dto.Guaranties.AddRange(guaranties.Values);
            dto.Facilities.AddRange(facilities.Values);
            dto.Assumptions.AddRange(assumptions.Values);
            dto.Links.AddRange(links.Values);
            return dto;
        }

        public void LoadFromSaveDto(FinancialObligationSaveDto dto)
        {
            obligations.Clear(); securities.Clear(); guaranties.Clear(); facilities.Clear(); assumptions.Clear(); links.Clear();
            if (dto == null) return;
            if (dto.Obligations != null) foreach (FinancialObligation value in dto.Obligations)
                if (value != null && !string.IsNullOrWhiteSpace(value.ObligationId)) obligations[value.ObligationId] = value;
            if (dto.Securities != null) foreach (SecurityInterestRecord value in dto.Securities)
                if (value != null && !string.IsNullOrWhiteSpace(value.SecurityInterestId)) securities[value.SecurityInterestId] = value;
            if (dto.Guaranties != null) foreach (GuarantyRecord value in dto.Guaranties)
                if (value != null && !string.IsNullOrWhiteSpace(value.GuarantyId)) guaranties[value.GuarantyId] = value;
            if (dto.Facilities != null) foreach (CreditFacilityRecord value in dto.Facilities)
                if (value != null && !string.IsNullOrWhiteSpace(value.FacilityId)) facilities[value.FacilityId] = value;
            if (dto.Assumptions != null) foreach (ObligationAssumptionRecord value in dto.Assumptions)
                if (value != null && !string.IsNullOrWhiteSpace(value.AssumptionId)) assumptions[value.AssumptionId] = value;
            if (dto.Links != null) foreach (ObligationLinkRecord value in dto.Links)
                if (value != null && !string.IsNullOrWhiteSpace(value.LinkId)) links[value.LinkId] = value;
        }

        public List<string> Reconcile()
        {
            var errors = new List<string>();
            var paymentIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (FinancialObligation obligation in obligations.Values)
            {
                if (obligation == null || string.IsNullOrWhiteSpace(obligation.ObligationId)) errors.Add("Obligation without identity.");
                else if (obligation.OriginalPrincipalCents < 0 || obligation.OutstandingPrincipalCents < 0
                    || obligation.AccruedInterestCents < 0 || obligation.FeesCents < 0)
                    errors.Add("Negative balance component on " + obligation.ObligationId + ".");
                int principalPaid = 0;
                if (obligation?.Payments != null)
                    foreach (FinancialPaymentRecord payment in obligation.Payments)
                    {
                        if (payment != null && !paymentIds.Add(payment.PaymentId)) errors.Add("Duplicate payment event: " + payment.PaymentId);
                        if (payment != null) principalPaid = checked(principalPaid + Math.Max(0, payment.PrincipalCents));
                    }
                if (obligation != null && obligation.OriginalPrincipalCents >= 0
                    && principalPaid + obligation.OutstandingPrincipalCents != obligation.OriginalPrincipalCents)
                    errors.Add("Principal conservation mismatch on " + obligation.ObligationId + ".");
            }
            foreach (SecurityInterestRecord security in securities.Values)
                if (security != null && Find(security.ObligationId) == null) errors.Add("Security without obligation: " + security.SecurityInterestId);
            foreach (GuarantyRecord guaranty in guaranties.Values)
                if (guaranty != null && Find(guaranty.CoveredObligationId) == null) errors.Add("Guaranty without obligation: " + guaranty.GuarantyId);
            foreach (CreditFacilityRecord facility in facilities.Values)
                if (facility != null && (facility.DrawnPrincipalCents < 0 || facility.DrawnPrincipalCents > facility.AuthorizedLimitCents))
                    errors.Add("Facility draw exceeds authorization: " + facility.FacilityId);
            return errors;
        }
    }
}
