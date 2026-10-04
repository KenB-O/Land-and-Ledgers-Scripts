using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using LandLedgers.Economy;
using LandLedgers.FirstLedger;
using LandLedgers.Persistence;
using LandLedgers.Reputation;
using LandLedgers.Time;
using UnityEngine;

namespace LandLedgers.Economy.Financing
{
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(287)]
    public sealed class PlayerDebtManager : MonoBehaviour
    {
        private const int DefaultRequestedAmountCents = 10000;
        private const int MinimumPrincipalCapacityCents = 50000;
        private const int LoanTermMonths = 12;
        private const int FirstPaymentOffsetDays = 7;
        private const int RepeatMissedPaymentIntervalDays = 7;
        private const int FinalWarningMissedPaymentCount = 3;
        private const int DefaultMissedPaymentCount = 4;
        private const int MaximumWorkoutCountPerLoan = 1;
        private const int WorkoutTermExtensionMonths = 3;
        private const int MinimumWorkoutTermMonths = 6;
        private const int WorkoutRatePenaltyBps = 75;
        private const float MinimumWorkoutLenderTrust01 = 0.26f;
        private const int WorkoutCashCureFloorCents = 1500;

        [Header("Sources")]
        [SerializeField] private TimeManager timeManager;
        [SerializeField] private GeneralStoreRuntimeManager storeRuntime;
        [SerializeField] private AcquisitionMarketManager acquisitionMarket;
        [SerializeField] private SharedBusinessRuntimeManager sharedBusinessRuntime;
        [SerializeField] private PlayerPortfolioManager playerPortfolio;

        [Header("Lender")]
        [SerializeField] private LenderProfile lender = LenderProfile.DefaultLocalBank;
        [SerializeField] private PlayerReputationState reputation = new(0.55f, 0.58f, 0.5f, 0.5f, 0.55f);

        [Header("Runtime")]
        [SerializeField] private int requestedAmountCents = DefaultRequestedAmountCents;
        [SerializeField] private BankLoanApplicationState application = new();
        [SerializeField] private LoanContract activeLoan;
        [SerializeField] private int consecutiveMissedPayments;
        [SerializeField] private int lifetimeMissedPayments;
        [SerializeField] private int defaultCount;
        [SerializeField] private int priorFailedFinancingCount;
        [SerializeField] private int lastMissedPaymentDayIndex = -1;
        [SerializeField, TextArea(2, 4)] private string lastStatusSummary = "No bank loan activity yet.";
        [SerializeField, TextArea(2, 4)] private string lastDecisionSummary = string.Empty;
        [SerializeField, TextArea(2, 4)] private string lastRepaymentSummary = string.Empty;
        [SerializeField, TextArea(2, 4)] private string lastRecoverySummary = string.Empty;

        private bool subscribedToTime;
        private readonly PaymentScheduleBuilder scheduleBuilder = new();
        private readonly ReputationEventApplier reputationApplier = new();

        public int RequestedAmountCents => Mathf.Max(0, requestedAmountCents);
        public BankLoanApplicationState Application => application;
        public LoanContract ActiveLoan => activeLoan;
        public PlayerReputationState Reputation => reputation;
        public float LenderTrust01 => reputation != null ? Mathf.Clamp01(reputation.lenderTrust01) : lender.Sanitized().trust01;
        public int ConsecutiveMissedPayments => Mathf.Max(0, consecutiveMissedPayments);
        public int LifetimeMissedPayments => Mathf.Max(0, lifetimeMissedPayments);
        public int DefaultCount => Mathf.Max(0, defaultCount);
        public string LastStatusSummary => lastStatusSummary ?? string.Empty;
        public string LastDecisionSummary => lastDecisionSummary ?? string.Empty;
        public string LastRepaymentSummary => lastRepaymentSummary ?? string.Empty;
        public string LastRecoverySummary => lastRecoverySummary ?? string.Empty;
        public bool HasPendingApplication => application != null && application.IsPending;
        public bool HasActiveLoan => IsUnresolvedDebt(activeLoan);
        public bool CanEditRequestedAmount => !HasPendingApplication && !HasActiveLoan;
        public bool CanSubmitApplication => CanEditRequestedAmount && RequestedAmountCents > 0;
        public int ActiveDebtPaymentCents => GetNextUnpaidPayment(activeLoan)?.totalDueCents ?? 0;
        public int ActiveDebtWeeklyEquivalentPaymentCents => GetWeeklyEquivalentDebtPaymentCents(activeLoan);
        public int ActiveDebtPrincipalCents => HasActiveLoan && activeLoan != null ? Mathf.Max(0, activeLoan.remainingPrincipalCents) : 0;

        public PlayerDebtSaveDto CaptureSaveDto()
        {
            SanitizeRuntime();
            return new PlayerDebtSaveDto
            {
                requestedAmountCents = RequestedAmountCents,
                application = application,
                activeLoan = activeLoan,
                reputation = reputation != null ? reputation.Clone() : new PlayerReputationState(),
                consecutiveMissedPayments = ConsecutiveMissedPayments,
                lifetimeMissedPayments = LifetimeMissedPayments,
                defaultCount = DefaultCount,
                priorFailedFinancingCount = Mathf.Max(0, priorFailedFinancingCount),
                lastMissedPaymentDayIndex = lastMissedPaymentDayIndex,
                lastStatusSummary = LastStatusSummary,
                lastDecisionSummary = LastDecisionSummary,
                lastRepaymentSummary = LastRepaymentSummary,
                lastRecoverySummary = LastRecoverySummary
            };
        }

        public void LoadFromSaveDto(PlayerDebtSaveDto dto)
        {
            AutoWire();
            if (dto == null)
            {
                requestedAmountCents = DefaultRequestedAmountCents;
                application = new BankLoanApplicationState();
                activeLoan = null;
                reputation = new PlayerReputationState(0.55f, lender.Sanitized().trust01, 0.5f, 0.5f, 0.55f);
                consecutiveMissedPayments = 0;
                lifetimeMissedPayments = 0;
                defaultCount = 0;
                priorFailedFinancingCount = 0;
                lastMissedPaymentDayIndex = -1;
                lastStatusSummary = "No bank loan activity yet.";
                lastDecisionSummary = string.Empty;
                lastRepaymentSummary = string.Empty;
                lastRecoverySummary = string.Empty;
                return;
            }

            requestedAmountCents = Mathf.Max(0, dto.requestedAmountCents <= 0 ? DefaultRequestedAmountCents : dto.requestedAmountCents);
            application = dto.application ?? new BankLoanApplicationState();
            activeLoan = dto.activeLoan;
            reputation = dto.reputation ?? new PlayerReputationState(0.55f, lender.Sanitized().trust01, 0.5f, 0.5f, 0.55f);
            reputation.Clamp();
            consecutiveMissedPayments = Mathf.Max(0, dto.consecutiveMissedPayments);
            lifetimeMissedPayments = Mathf.Max(0, dto.lifetimeMissedPayments);
            defaultCount = Mathf.Max(0, dto.defaultCount);
            priorFailedFinancingCount = Mathf.Max(0, dto.priorFailedFinancingCount);
            lastMissedPaymentDayIndex = dto.lastMissedPaymentDayIndex;
            lastStatusSummary = string.IsNullOrWhiteSpace(dto.lastStatusSummary) ? "Bank loan state restored." : dto.lastStatusSummary;
            lastDecisionSummary = dto.lastDecisionSummary ?? string.Empty;
            lastRepaymentSummary = dto.lastRepaymentSummary ?? string.Empty;
            lastRecoverySummary = dto.lastRecoverySummary ?? string.Empty;
            SanitizeRuntime();
        }

        public void Configure(
            TimeManager newTimeManager,
            GeneralStoreRuntimeManager newStoreRuntime,
            AcquisitionMarketManager newAcquisitionMarket,
            SharedBusinessRuntimeManager newSharedBusinessRuntime,
            PlayerPortfolioManager newPlayerPortfolio = null)
        {
            timeManager = newTimeManager;
            storeRuntime = newStoreRuntime;
            acquisitionMarket = newAcquisitionMarket;
            sharedBusinessRuntime = newSharedBusinessRuntime;
            playerPortfolio = newPlayerPortfolio != null ? newPlayerPortfolio : playerPortfolio;
            SubscribeToTime();
        }

        public void AdjustRequestedAmountDollars(int deltaDollars)
        {
            if (!CanEditRequestedAmount)
            {
                return;
            }

            requestedAmountCents = Mathf.Max(0, requestedAmountCents + deltaDollars * 100);
            lastStatusSummary = "Loan request amount adjusted.";
            RefreshEstimatedApplication();
        }

        public bool SubmitApplication(out string message)
        {
            AutoWire();
            SanitizeRuntime();
            if (!CanSubmitApplication)
            {
                message = HasActiveLoan
                    ? BuildDebtBlockedMessage("new application")
                    : HasPendingApplication
                        ? "A loan application is already pending review."
                        : "Choose an amount before submitting.";
                lastStatusSummary = message;
                return false;
            }

            int currentDay = GetCurrentDayIndex();
            if (HasActiveLenderReapplyCooldown(currentDay))
            {
                message = BuildReapplyCooldownBlockedMessage(currentDay);
                lastStatusSummary = message;
                lastDecisionSummary = message;
                return false;
            }

            LoanTermStructure term = BuildTerm(RequestedAmountCents);
            BankLoanUnderwritingResult provisionalReview = EvaluateUnderwriting(RequestedAmountCents);
            int scheduledReviewDays = Mathf.Max(1, provisionalReview.reviewDays);
            SimulationDate expectedDecisionDate = BuildDate(currentDay + scheduledReviewDays);
            application = new BankLoanApplicationState
            {
                applicationId = BuildLoanId("application", currentDay),
                status = BankLoanApplicationStatus.PendingReview,
                purpose = LoanPurpose.WorkingCapital,
                requestedAmountCents = RequestedAmountCents,
                submittedDayIndex = currentDay,
                decisionDayIndex = currentDay + scheduledReviewDays,
                submittedDate = GetCurrentDate(),
                decisionDate = expectedDecisionDate,
                estimatedTerm = term,
                estimatedSchedule = scheduleBuilder.Build(BuildLoanId("estimate", currentDay), term, GetCurrentDate(), FirstPaymentOffsetDays, GetDaysPerMonth()),
                decision = FinancingDecision.EstimateOnly,
                reasonCodes = new List<FinancingReasonCode>(),
                decisionSummary = "Loan Application Submitted",
                declineReason = string.Empty,
                reviewBand = provisionalReview.reviewBand,
                scheduledReviewDays = scheduledReviewDays,
                reviewContext = provisionalReview.reviewContext
            };

            message = "Loan Application Submitted";
            lastStatusSummary = BuildPendingReviewStatus(application.reviewBand, application.decisionDate, application.reviewContext);
            lastDecisionSummary = BuildPendingReviewDecisionMessage(application.reviewBand, application.decisionDate, application.reviewContext);
            return true;
        }

        public void ProcessCurrentDay()
        {
            ProcessDay(GetCurrentDayIndex());
        }

        public void ProcessDay(int absoluteDayIndex)
        {
            AutoWire();
            SanitizeRuntime();
            ResolvePendingApplication(absoluteDayIndex);
            ProcessRepayment(absoluteDayIndex);
        }

        public BankLoanUnderwritingResult EvaluateRequestedAmount()
        {
            return EvaluateUnderwriting(RequestedAmountCents);
        }

        public FinancingApplicantProfile BuildAcquisitionApplicantProfile()
        {
            AutoWire();
            SanitizeRuntime();
            int cashCents = GetOwnerCashCents();
            int weeklyNetCashFlowCents = GetWeeklyNetCashFlowCents();
            int currentDay = GetCurrentDayIndex();
            // Reuse the lane's existing lender-memory tail so acquisition files do not ignore recent bank setbacks that already affect working-capital review.
            int lenderCooldownDays = GetRemainingLenderCooldownDays(currentDay);
            ReputationEvaluator evaluator = new ReputationEvaluator();
            float ownerHeadline = evaluator.EvaluateHeadline01(reputation);
            return new FinancingApplicantProfile
            {
                applicantId = "player",
                reputation01 = ownerHeadline > 0f ? ownerHeadline : 0.55f,
                lenderTrust01 = LenderTrust01,
                operationalReliability01 = reputation != null ? reputation.operationalReliability01 : 0.55f,
                availableCashCents = cashCents,
                existingDebtPaymentCents = HasActiveLoan ? ActiveDebtWeeklyEquivalentPaymentCents : 0,
                weeklyNetCashFlowCents = weeklyNetCashFlowCents,
                ownedAssetValueCents = GetOwnedAssetValueCents(),
                priorFailedFinancingCount = priorFailedFinancingCount,
                lenderReapplyCooldownDays = lenderCooldownDays,
                lenderCaution01 = lenderCooldownDays > 0 && application != null ? application.lenderCaution01 : 0f
            }.Sanitized();
        }

        public FinancingApprovalResult EvaluateAcquisitionFinancing(
            string agreementId,
            int purchasePriceCents,
            int downPaymentCents,
            int collateralValueCents,
            LoanCollateralKind collateralKind,
            string collateralId)
        {
            LoanPurpose purpose = InferAcquisitionLoanPurpose(collateralKind);
            FinancingApprovalRequest request = new FinancingApprovalRequest
            {
                agreementId = string.IsNullOrWhiteSpace(agreementId) ? BuildLoanId("acquisition_agreement", GetCurrentDayIndex()) : agreementId,
                purpose = purpose,
                exactPurchasePriceCents = Mathf.Max(1, purchasePriceCents),
                exactDownPaymentCents = Mathf.Clamp(downPaymentCents, 0, Mathf.Max(1, purchasePriceCents)),
                applicant = BuildAcquisitionApplicantProfile(),
                lender = lender.Sanitized(),
                collateral = BuildDefaultCollateralProfile(collateralValueCents, collateralKind, collateralId),
                requestedTerm = BuildAcquisitionRequestedTerm(),
                approvalDate = GetCurrentDate(),
                daysPerMonth = GetDaysPerMonth()
            };

            return new FinancingEvaluator().EvaluateApproval(request);
        }

        public string BuildAcquisitionFinancingReviewText(
            string agreementId,
            int purchasePriceCents,
            int downPaymentCents,
            int collateralValueCents,
            LoanCollateralKind collateralKind,
            string collateralId)
        {
            FinancingApprovalResult approval = EvaluateAcquisitionFinancing(
                agreementId,
                purchasePriceCents,
                downPaymentCents,
                collateralValueCents,
                collateralKind,
                collateralId);

            if (approval == null || !approval.HasOffer)
            {
                return "Acquisition financing unavailable\nCould not evaluate this offer right now.";
            }

            LoanOffer offer = approval.Offer;
            FinancingApplicantProfile applicant = BuildAcquisitionApplicantProfile();
            int weeklyFreeCashAfterDebt = Mathf.Max(0, applicant.weeklyNetCashFlowCents - applicant.existingDebtPaymentCents);
            List<string> lines = new List<string>
            {
                DescribeAcquisitionDecision(approval.Decision),
                $"Purpose: {FormatLoanPurpose(offer.purpose)}",
                $"Principal {FormatMoney(approval.EstimatedPrincipalCents)} | Payment {FormatMoney(approval.EstimatedPeriodicPaymentCents)} {DescribeRepaymentFrequency(offer.termStructure.repaymentFrequency)}",
                $"Rate {FormatRateBps(offer.termStructure.annualInterestRateBps)} | Term {offer.termStructure.termMonths} months | Fee {FormatMoney(approval.EstimatedOriginationFeeCents)}",
                $"Total scheduled repayment incl. fee: {FormatMoney(approval.EstimatedTotalBorrowerCostCents)}",
                $"Collateral support: {FormatMoney(offer.maxPrincipalCents)} bankable vs {FormatMoney(approval.EstimatedPrincipalCents)} requested",
                $"Repayment source: {FormatMoney(weeklyFreeCashAfterDebt)} weekly free cash after current debt service",
                $"Offer good until: {FormatDate(offer.expiresOnDate)}"
            };

            string gapSummary = BuildAcquisitionGapSummary(approval, 3);
            if (!string.IsNullOrWhiteSpace(gapSummary))
            {
                lines.Add($"Still missing: {gapSummary}");
            }
            else if (approval.HasConditions)
            {
                lines.Add($"Next steps: {BuildConditionSummary(approval.RequiredConditions, 2)}");
            }
            else if (approval.CanFundImmediately && !approval.HasBlockingReadinessGap)
            {
                lines.Add("Ready to close now.");
            }
            else
            {
                // Closing is rechecked at activation time so this preview stays honest about moving debt and trust conditions.
                lines.Add("Closing note: bank terms are rechecked when funding is activated.");
            }

            return string.Join("\n", lines);
        }

        public string BuildAcquisitionFundingReadinessText(
            string agreementId,
            int purchasePriceCents,
            int downPaymentCents,
            int collateralValueCents,
            LoanCollateralKind collateralKind,
            string collateralId)
        {
            TryRecheckAcquisitionFundingReadiness(
                agreementId,
                purchasePriceCents,
                downPaymentCents,
                collateralValueCents,
                collateralKind,
                collateralId,
                out _,
                out string message);
            return message;
        }

        public bool TryRecheckAcquisitionFundingReadiness(
            string agreementId,
            int purchasePriceCents,
            int downPaymentCents,
            int collateralValueCents,
            LoanCollateralKind collateralKind,
            string collateralId,
            out FinancingApprovalResult approval,
            out string message)
        {
            AutoWire();
            SanitizeRuntime();
            approval = null;

            if (HasActiveLoan)
            {
                message = BuildDebtBlockedMessage("acquisition financing");
                return false;
            }

            if (HasPendingApplication)
            {
                message = "A bank loan application is already pending review.";
                return false;
            }

            // This is the single close-time truth check so preview, readiness, and activation all read the same file state.
            approval = EvaluateAcquisitionFinancing(
                agreementId,
                purchasePriceCents,
                downPaymentCents,
                collateralValueCents,
                collateralKind,
                collateralId);

            if (approval == null || !approval.HasOffer)
            {
                message = "Acquisition financing could not be evaluated.";
                return false;
            }

            if (!approval.Approved || approval.HasBlockingReadinessGap)
            {
                message = BuildAcquisitionDecisionMessage(approval);
                return false;
            }

            message = BuildAcquisitionReadyMessage(approval);
            return true;
        }

        public bool TryActivateAcquisitionLoan(
            string agreementId,
            int purchasePriceCents,
            int downPaymentCents,
            int collateralValueCents,
            LoanCollateralKind collateralKind,
            string collateralId,
            out int fundedPrincipalCents,
            out string message)
        {
            AutoWire();
            SanitizeRuntime();
            fundedPrincipalCents = 0;

            if (!TryRecheckAcquisitionFundingReadiness(
                agreementId,
                purchasePriceCents,
                downPaymentCents,
                collateralValueCents,
                collateralKind,
                collateralId,
                out FinancingApprovalResult approval,
                out message))
            {
                lastStatusSummary = message;
                lastDecisionSummary = message;
                return false;
            }

            int currentDay = GetCurrentDayIndex();
            string loanId = BuildLoanId("acquisition_loan", currentDay);
            LoanTermStructure term = approval.Offer.termStructure.Sanitized();
            if (term.principalCents <= 0)
            {
                message = "Acquisition financing approved no principal.";
                return false;
            }

            LoanPaymentSchedule schedule = scheduleBuilder.Build(
                loanId,
                term,
                GetCurrentDate(),
                FirstPaymentOffsetDays,
                GetDaysPerMonth());

            activeLoan = new LoanContract
            {
                loanId = loanId,
                lenderId = approval.Offer.lenderId,
                purpose = LoanPurposeRules.Sanitize(approval.Offer.purpose, InferAcquisitionLoanPurpose(collateralKind)),
                collateralIds = new List<string> { collateralId ?? string.Empty },
                status = LoanStatus.Active,
                termStructure = term,
                remainingPrincipalCents = term.principalCents,
                nextPaymentDueDate = schedule.payments.Count > 0 ? schedule.payments[0].dueDate : BuildDate(currentDay + FirstPaymentOffsetDays),
                schedule = schedule,
                workoutCount = 0,
                lastWorkoutDayIndex = -1
            };

            application = new BankLoanApplicationState
            {
                applicationId = BuildLoanId("acquisition_application", currentDay),
                status = BankLoanApplicationStatus.Approved,
                purpose = activeLoan.purpose,
                requestedAmountCents = term.principalCents,
                submittedDayIndex = currentDay,
                decisionDayIndex = currentDay,
                submittedDate = GetCurrentDate(),
                decisionDate = GetCurrentDate(),
                estimatedTerm = term,
                estimatedSchedule = schedule,
                decision = approval.Decision,
                reasonCodes = new List<FinancingReasonCode>(approval.ReasonCodes),
                decisionSummary = "Acquisition Financing Approved",
                declineReason = string.Empty
            };

            consecutiveMissedPayments = 0;
            lastMissedPaymentDayIndex = -1;
            fundedPrincipalCents = term.principalCents;
            AddOwnerCashToPortfolio(fundedPrincipalCents, "approved acquisition loan");
            lastStatusSummary = $"{application.decisionSummary}. Funds have been added to owner cash.";
            lastDecisionSummary = $"{application.decisionSummary}. {BuildFirstPaymentSummary(activeLoan)}";
            lastRepaymentSummary = BuildFirstPaymentSummary(activeLoan);
            message = lastStatusSummary;
            return true;
        }

        public void RecordAcquisitionFinancingFailure(FinancingFailureResult result)
        {
            if (result == null)
            {
                return;
            }

            SanitizeRuntime();
            int currentDay = GetCurrentDayIndex();
            priorFailedFinancingCount += Mathf.Max(1, result.FailureStrikes);
            reputation.Add(ReputationSubcategory.DealTrust, result.ReputationDelta01);
            reputation.Add(ReputationSubcategory.LenderTrust, result.LenderTrustDelta01);
            reputation.Clamp();
            ApplyLenderSetbackMemory(currentDay, result.CooldownDays, result.LenderCaution01, result.Summary);
            string cooldownSuffix = BuildReapplyWindowSentence(currentDay);
            lastDecisionSummary = AppendCooldownContext(result.Summary, cooldownSuffix);
            lastStatusSummary = $"{result.Summary} Earnest forfeited: {FormatMoney(result.ForfeitedEarnestMoneyCents)}.{cooldownSuffix}";
        }

        public string BuildRequestedAmountText()
        {
            return $"Requested amount: {FormatMoney(RequestedAmountCents)}";
        }

        public string BuildEstimatedPaymentText()
        {
            BankLoanUnderwritingResult estimate = EvaluateRequestedAmount();
            return $"Estimated payment: {FormatMoney(estimate.estimatedPaymentCents)} {DescribeRepaymentFrequency(estimate.term.repaymentFrequency)}";
        }

        public string BuildPaymentScheduleText()
        {
            BankLoanUnderwritingResult estimate = EvaluateRequestedAmount();
            return $"Payment schedule: {DescribeRepaymentFrequency(estimate.term.repaymentFrequency)} for {estimate.term.termMonths} months";
        }

        public string BuildEstimatedTotalOwedText()
        {
            BankLoanUnderwritingResult estimate = EvaluateRequestedAmount();
            return $"Estimated total scheduled repayment incl. bank fee: {FormatMoney(estimate.estimatedTotalOwedCents)}";
        }

        public string BuildLenderStandingText()
        {
            int currentDay = GetCurrentDayIndex();
            string cautionSuffix = HasActiveLenderReapplyCooldown(currentDay)
                ? $" | Cooling off {Mathf.Max(1, GetRemainingLenderCooldownDays(currentDay))}d"
                : string.Empty;
            return $"Lender standing: {Mathf.RoundToInt(LenderTrust01 * 100f)}%{cautionSuffix}";
        }

        public string BuildPanelStatusText()
        {
            SanitizeRuntime();
            if (HasPendingApplication)
            {
                return BuildPendingReviewPanelText(application);
            }

            if (activeLoan != null && activeLoan.status == LoanStatus.PaidOff)
            {
                return $"Loan paid off.\n{LastRepaymentSummary}";
            }

            if (activeLoan != null && activeLoan.status == LoanStatus.Recovered)
            {
                return $"Loan resolved by recovery.\n{LastRecoverySummary}";
            }

            if (activeLoan != null && activeLoan.status == LoanStatus.Defaulted)
            {
                return $"Loan defaulted.\n{LastRecoverySummary}";
            }

            if (application != null && application.status == BankLoanApplicationStatus.Approved)
            {
                string firstDue = activeLoan != null ? FormatDate(activeLoan.nextPaymentDueDate) : "scheduled cadence";
                return $"{application.decisionSummary}\nFunds have been added to owner cash.\nFirst payment due: {firstDue}\n{LastRepaymentSummary}";
            }

            if (application != null && application.status == BankLoanApplicationStatus.Declined)
            {
                return $"Loan Declined\n{LastDecisionSummary}";
            }

            int currentDay = GetCurrentDayIndex();
            if (HasActiveLenderReapplyCooldown(currentDay))
            {
                return BuildReapplyCooldownStatus(currentDay);
            }

            return LastStatusSummary;
        }

        public string BuildActiveLoanText()
        {
            if (activeLoan == null)
            {
                return "Active loan: none";
            }

            LoanPaymentDue next = GetNextUnpaidPayment(activeLoan);
            string feeSuffix = next != null && DoesPaymentIncludeOriginationFee(activeLoan.schedule, next) ? " incl. bank fee" : string.Empty;
            string nextLine = next != null
                ? $"Next payment: {FormatMoney(next.totalDueCents)}{feeSuffix} due {FormatDate(next.dueDate)}"
                : "Next payment: none";
            string workoutSuffix = activeLoan.workoutCount > 0 ? $" | Workout {activeLoan.workoutCount}" : string.Empty;
            return $"Active loan: {FormatLoanPurpose(activeLoan.purpose)} | {activeLoan.status} | Remaining principal {FormatMoney(activeLoan.remainingPrincipalCents)} | Missed {ConsecutiveMissedPayments}{workoutSuffix}\n{nextLine}";
        }

        private void Awake()
        {
            AutoWire();
            SanitizeRuntime();
        }

        private void OnEnable()
        {
            AutoWire();
            SubscribeToTime();
        }

        private void OnDisable()
        {
            UnsubscribeFromTime();
        }

        private void AutoWire()
        {
            timeManager ??= TimeManager.Instance != null ? TimeManager.Instance : FindAnyObjectByType<TimeManager>();
            storeRuntime ??= FindAnyObjectByType<GeneralStoreRuntimeManager>();
            acquisitionMarket ??= FindAnyObjectByType<AcquisitionMarketManager>();
            sharedBusinessRuntime ??= FindAnyObjectByType<SharedBusinessRuntimeManager>();
            playerPortfolio ??= FindAnyObjectByType<PlayerPortfolioManager>();
        }

        private void SubscribeToTime()
        {
            if (subscribedToTime || timeManager == null)
            {
                return;
            }

            timeManager.DayChanged += OnDayChanged;
            subscribedToTime = true;
        }

        private void UnsubscribeFromTime()
        {
            if (!subscribedToTime || timeManager == null)
            {
                subscribedToTime = false;
                return;
            }

            timeManager.DayChanged -= OnDayChanged;
            subscribedToTime = false;
        }

        private void OnDayChanged(SimulationDateChangedContext context)
        {
            ProcessDay(GetAbsoluteDayIndexFromContext(context));
        }

        private void ResolvePendingApplication(int absoluteDayIndex)
        {
            if (!HasPendingApplication || absoluteDayIndex < application.decisionDayIndex)
            {
                return;
            }

            if (HasActiveLoan)
            {
                DeclinePendingApplicationForUnresolvedDebt(absoluteDayIndex);
                return;
            }

            BankLoanUnderwritingResult result = EvaluateUnderwriting(application.requestedAmountCents);
            application.decision = result.decision;
            application.reasonCodes = new List<FinancingReasonCode>(result.reasonCodes);
            application.decisionDate = BuildDate(absoluteDayIndex);
            application.reviewBand = result.reviewBand;
            application.scheduledReviewDays = Mathf.Max(1, result.reviewDays);
            application.reviewContext = result.reviewContext;
            if (result.Approved)
            {
                ApproveApplication(result, absoluteDayIndex);
                return;
            }

            application.status = BankLoanApplicationStatus.Declined;
            application.decisionSummary = "Loan Declined";
            application.declineReason = result.declineReason;
            ApplyDeclinedApplicationSetbackMemory(absoluteDayIndex, result);
            lastStatusSummary = "Loan Declined. The bank has declined your application.";
            lastDecisionSummary = AppendCooldownContext(BuildDeclineDecisionMessage(result), BuildReapplyWindowSentence(absoluteDayIndex));
        }

        private void ApproveApplication(BankLoanUnderwritingResult result, int absoluteDayIndex)
        {
            string loanId = BuildLoanId("bank_loan", absoluteDayIndex);
            LoanPaymentSchedule schedule = scheduleBuilder.Build(
                loanId,
                result.term,
                BuildDate(absoluteDayIndex),
                FirstPaymentOffsetDays,
                GetDaysPerMonth());

            activeLoan = new LoanContract
            {
                loanId = loanId,
                lenderId = lender.Sanitized().lenderId,
                purpose = LoanPurposeRules.Sanitize(result.purpose, LoanPurpose.WorkingCapital),
                status = LoanStatus.Active,
                termStructure = result.term,
                remainingPrincipalCents = result.term.principalCents,
                nextPaymentDueDate = schedule.payments.Count > 0 ? schedule.payments[0].dueDate : BuildDate(absoluteDayIndex + FirstPaymentOffsetDays),
                schedule = schedule,
                workoutCount = 0,
                lastWorkoutDayIndex = -1
            };

            application.status = BankLoanApplicationStatus.Approved;
            application.purpose = LoanPurposeRules.Sanitize(result.purpose, LoanPurpose.WorkingCapital);
            application.decision = result.decision;
            application.decisionSummary = BuildApprovalSummary(result.reviewBand);
            application.declineReason = string.Empty;
            application.reviewBand = result.reviewBand;
            application.scheduledReviewDays = Mathf.Max(1, result.reviewDays);
            application.reviewContext = result.reviewContext;
            ClearLenderSetbackMemory();
            consecutiveMissedPayments = 0;
            lastMissedPaymentDayIndex = -1;
            AddOwnerCashToPortfolio(result.term.principalCents, "approved bank loan");
            lastStatusSummary = $"{application.decisionSummary}. Funds have been added to owner cash.";
            lastDecisionSummary = BuildApprovalDecisionMessage(result, activeLoan.nextPaymentDueDate);
            lastRepaymentSummary = BuildFirstPaymentSummary(activeLoan);
        }

        private void ProcessRepayment(int absoluteDayIndex)
        {
            if (!IsRepayableDebt(activeLoan) || activeLoan.schedule == null || activeLoan.schedule.payments == null)
            {
                return;
            }

            LoanPaymentDue payment = GetOldestDuePayment(activeLoan, absoluteDayIndex);
            if (payment == null)
            {
                return;
            }

            int availableCash = GetOwnerCashCents();
            if (availableCash >= payment.totalDueCents
                && playerPortfolio != null
                && TrySpendOwnerCashFromPortfolio(payment.totalDueCents, "bank loan payment"))
            {
                bool wasLate = payment.status == LoanPaymentStatus.Late || absoluteDayIndex > payment.dueDayIndex;
                payment.paidCents = payment.totalDueCents;
                payment.status = LoanPaymentStatus.Paid;
                activeLoan.remainingPrincipalCents = Mathf.Max(0, activeLoan.remainingPrincipalCents - Mathf.Max(0, payment.principalCents));
                consecutiveMissedPayments = 0;
                lastMissedPaymentDayIndex = -1;
                activeLoan.status = LoanStatus.Active;
                UpdateNextPaymentDueDate();
                if (!wasLate)
                {
                    ApplyReputationEvent(ReputationEventType.LoanPaidOnTime, 0.45f, "Bank loan payment made on schedule.", absoluteDayIndex);
                }

                string feeSuffix = DoesPaymentIncludeOriginationFee(activeLoan.schedule, payment) ? " including bank fee" : string.Empty;
                lastRepaymentSummary = wasLate
                    ? $"Late bank loan payment caught up: {FormatMoney(payment.totalDueCents)}{feeSuffix}."
                    : $"Bank loan payment made: {FormatMoney(payment.totalDueCents)}{feeSuffix}.";
                lastStatusSummary = lastRepaymentSummary;
                MarkPaidOffIfComplete();
                return;
            }

            payment.status = LoanPaymentStatus.Late;
            if (lastMissedPaymentDayIndex < 0 || absoluteDayIndex >= lastMissedPaymentDayIndex + RepeatMissedPaymentIntervalDays)
            {
                consecutiveMissedPayments++;
                lifetimeMissedPayments++;
                lastMissedPaymentDayIndex = absoluteDayIndex;
                activeLoan.status = LoanStatus.Delinquent;
                ApplyReputationEvent(ReputationEventType.LoanPaymentLate, GetLateSeverity(), "Bank loan payment missed.", absoluteDayIndex);
                lastRepaymentSummary = BuildDelinquencySummary(payment);
                lastStatusSummary = lastRepaymentSummary;
            }

            if (consecutiveMissedPayments >= DefaultMissedPaymentCount)
            {
                if (!TryApplyDelinquencyWorkout(absoluteDayIndex))
                {
                    DefaultActiveLoan(absoluteDayIndex);
                }
            }
        }

        // Workouts are deliberately narrow: one short revised note when the file still looks recoverable, otherwise default logic stands.
        private bool TryApplyDelinquencyWorkout(int absoluteDayIndex)
        {
            if (activeLoan == null
                || activeLoan.status == LoanStatus.Defaulted
                || activeLoan.status == LoanStatus.Recovered
                || activeLoan.status == LoanStatus.PaidOff
                || activeLoan.workoutCount >= MaximumWorkoutCountPerLoan)
            {
                return false;
            }

            LoanPaymentDue overduePayment = GetOldestDuePayment(activeLoan, absoluteDayIndex);
            if (overduePayment == null)
            {
                return false;
            }

            int weeklyNetCashFlowCents = GetWeeklyNetCashFlowCents();
            int ownerCashCents = GetOwnerCashCents();
            bool hasCollateralSupport = HasWorkoutCollateralSupport();
            bool showsTemporaryRecoveryPath = weeklyNetCashFlowCents > 0 || ownerCashCents >= Mathf.Max(WorkoutCashCureFloorCents, overduePayment.totalDueCents / 2);
            if (LenderTrust01 < MinimumWorkoutLenderTrust01 || !hasCollateralSupport || !showsTemporaryRecoveryPath)
            {
                return false;
            }

            int capitalizedInterestCents = CalculatePastDueInterestCents(activeLoan, absoluteDayIndex);
            int revisedPrincipalCents = Mathf.Max(0, activeLoan.remainingPrincipalCents + capitalizedInterestCents);
            if (revisedPrincipalCents <= 0)
            {
                return false;
            }

            int remainingMonths = GetRemainingScheduledMonths(activeLoan);
            LoanTermStructure revisedTerm = activeLoan.termStructure.Sanitized();
            revisedTerm.principalCents = revisedPrincipalCents;
            revisedTerm.termMonths = Mathf.Max(MinimumWorkoutTermMonths, remainingMonths + WorkoutTermExtensionMonths);
            revisedTerm.amortizationMonths = revisedTerm.termMonths;
            revisedTerm.annualInterestRateBps = Mathf.Max(0, revisedTerm.annualInterestRateBps + WorkoutRatePenaltyBps);
            revisedTerm.originationFeeCents = 0;
            revisedTerm.balloonPaymentCents = 0;
            revisedTerm = revisedTerm.Sanitized();

            // A workout replaces the delinquent cadence with one revised schedule so later repayment logic still reads one authority.
            LoanPaymentSchedule revisedSchedule = scheduleBuilder.Build(
                activeLoan.loanId,
                revisedTerm,
                BuildDate(absoluteDayIndex),
                FirstPaymentOffsetDays,
                GetDaysPerMonth());
            if (revisedSchedule == null || revisedSchedule.payments == null || revisedSchedule.payments.Count == 0)
            {
                return false;
            }

            activeLoan.termStructure = revisedTerm;
            activeLoan.remainingPrincipalCents = revisedPrincipalCents;
            activeLoan.nextPaymentDueDate = revisedSchedule.payments[0].dueDate;
            activeLoan.schedule = revisedSchedule;
            activeLoan.status = LoanStatus.Active;
            activeLoan.workoutCount++;
            activeLoan.lastWorkoutDayIndex = absoluteDayIndex;
            ApplyLenderSetbackMemory(absoluteDayIndex, 10, 0.22f, "The bank granted a short workout and will review fresh borrowing conservatively for a little while.");
            consecutiveMissedPayments = 0;
            lastMissedPaymentDayIndex = -1;
            ApplyReputationEvent(ReputationEventType.LoanPaymentLate, 0.65f, "Bank granted a short workout after repeated delinquency.", absoluteDayIndex);
            string capitalizedSummary = capitalizedInterestCents > 0
                ? $" Past-due interest of {FormatMoney(capitalizedInterestCents)} was rolled into the revised note."
                : string.Empty;
            lastRepaymentSummary = $"Workout granted: bank reset the note over {revisedTerm.termMonths} months at {FormatRateBps(revisedTerm.annualInterestRateBps)}. Next payment due {FormatDate(activeLoan.nextPaymentDueDate)}.{capitalizedSummary}";
            lastStatusSummary = lastRepaymentSummary;
            return true;
        }

        private void DefaultActiveLoan(int absoluteDayIndex)
        {
            if (activeLoan == null || activeLoan.status == LoanStatus.Defaulted || activeLoan.status == LoanStatus.Recovered)
            {
                return;
            }

            activeLoan.status = LoanStatus.Defaulted;
            defaultCount++;
            priorFailedFinancingCount += 2;
            ApplyLenderSetbackMemory(absoluteDayIndex, 42, 0.58f, "Recent loan default keeps the bank in a cautious posture until the recovery dust settles.");
            ApplyReputationEvent(ReputationEventType.LoanDefaulted, 1f, "Bank loan defaulted after repeated missed payments.", absoluteDayIndex);

            int excludedBuildingId = GetStoreBuildingId();
            if (TryRecoverDefaultedLoanCollateral(excludedBuildingId, out string recoveredLabel, out int recoveredValueCents, out string recoveryMessage))
            {
                bool resolved = ApplyDefaultRecoveryCredit(recoveredValueCents);
                string resolution = resolved
                    ? "Defaulted debt resolved through forced recovery."
                    : $"Remaining unresolved principal {FormatMoney(activeLoan.remainingPrincipalCents)}.";
                lastRecoverySummary = $"Forced recovery: {recoveredLabel} recovered against the defaulted bank loan. Estimated value {FormatMoney(recoveredValueCents)}. {resolution} {recoveryMessage}";
            }
            else
            {
                lastRecoverySummary = "Forced recovery attempted, but no recoverable player holding was available for seizure. Defaulted debt remains unresolved.";
            }

            lastRepaymentSummary = activeLoan.status == LoanStatus.Recovered
                ? "Defaulted bank loan resolved through forced recovery."
                : "Loan defaulted after repeated missed payments.";
            lastStatusSummary = lastRecoverySummary;
        }

        private bool ApplyDefaultRecoveryCredit(int recoveredValueCents)
        {
            if (activeLoan == null)
            {
                return false;
            }

            int credit = Mathf.Max(0, recoveredValueCents);
            activeLoan.remainingPrincipalCents = Mathf.Max(0, activeLoan.remainingPrincipalCents - credit);
            if (activeLoan.remainingPrincipalCents > 0)
            {
                activeLoan.status = LoanStatus.Defaulted;
                return false;
            }

            activeLoan.status = LoanStatus.Recovered;
            consecutiveMissedPayments = 0;
            lastMissedPaymentDayIndex = -1;
            MarkUnpaidPaymentsSkipped(activeLoan);
            return true;
        }

        private void DeclinePendingApplicationForUnresolvedDebt(int absoluteDayIndex)
        {
            application.status = BankLoanApplicationStatus.Declined;
            application.decision = FinancingDecision.Declined;
            application.reasonCodes = new List<FinancingReasonCode> { FinancingReasonCode.HeavyDebtBurden };
            application.decisionDate = BuildDate(absoluteDayIndex);
            application.decisionSummary = "Loan Declined";
            application.declineReason = "unresolved bank debt";
            lastStatusSummary = BuildDebtBlockedMessage("loan approval");
            lastDecisionSummary = "The bank has declined your application. Reason: unresolved bank debt.";
        }

        private BankLoanUnderwritingResult EvaluateUnderwriting(int principalCents)
        {
            int cashCents = GetOwnerCashCents();
            int weeklyNetCashFlowCents = GetWeeklyNetCashFlowCents();
            int currentDay = GetCurrentDayIndex();
            return EvaluateWorkingCapitalApplication(
                principalCents,
                cashCents,
                GetOwnedAssetValueCents(),
                weeklyNetCashFlowCents,
                HasActiveLoan ? ActiveDebtWeeklyEquivalentPaymentCents : 0,
                LenderTrust01,
                priorFailedFinancingCount,
                defaultCount,
                HasActiveLoan,
                lender,
                currentDay,
                application != null ? application.reapplyEarliestDayIndex : -1,
                application != null ? application.lenderCaution01 : 0f);
        }

        public static BankLoanUnderwritingResult EvaluateWorkingCapitalApplication(
            int requestedPrincipalCents,
            int cashCents,
            int ownedAssetValueCents,
            int weeklyNetCashFlowCents,
            int existingDebtPaymentCents,
            float lenderTrust01,
            int priorFailedFinancingCount,
            int priorDefaultCount,
            bool hasActiveBankLoan,
            LenderProfile lenderProfile,
            int currentDayIndex,
            int lenderCooldownUntilDayIndex,
            float lenderCaution01)
        {
            LenderProfile sanitizedLender = lenderProfile.Sanitized();
            float trust = Mathf.Clamp01(lenderTrust01);
            int cautionDaysRemaining = GetRemainingCooldownDays(currentDayIndex, lenderCooldownUntilDayIndex);
            float activeLenderCaution01 = cautionDaysRemaining > 0 ? Mathf.Clamp01(lenderCaution01) : 0f;
            float effectiveTrust = Mathf.Clamp01(trust - activeLenderCaution01 * 0.16f);
            int effectivePriorFailedFinancingCount = priorFailedFinancingCount + (cautionDaysRemaining > 0 ? Mathf.Max(1, Mathf.CeilToInt(activeLenderCaution01 * 2f)) : 0);
            int principal = Mathf.Max(0, requestedPrincipalCents);
            LoanTermStructure term = BuildTerm(principal, sanitizedLender, effectiveTrust);
            int estimatedPayment = PaymentScheduleBuilder.EstimatePaymentCents(term);
            LoanPaymentSchedule estimateSchedule = new PaymentScheduleBuilder().Build(
                "bank_loan_estimate",
                term,
                new SimulationDate(0, 0, 1, 1, 1, 1, 1),
                FirstPaymentOffsetDays);

            int safeCash = Mathf.Max(0, cashCents);
            int safeAssetValue = Mathf.Max(0, ownedAssetValueCents);
            int safeWeeklyNetCashFlow = Mathf.Max(0, weeklyNetCashFlowCents);
            int existingDebtPayment = Mathf.Max(0, existingDebtPaymentCents);
            int principalCapacity = CalculateWorkingCapitalPrincipalCapacityCents(safeCash, safeAssetValue, effectiveTrust, sanitizedLender);
            int weeklyPaymentCapacity = CalculateWorkingCapitalWeeklyPaymentCapacityCents(safeCash, safeWeeklyNetCashFlow, existingDebtPayment, sanitizedLender);
            if (activeLenderCaution01 > 0f)
            {
                principalCapacity = Mathf.RoundToInt(principalCapacity * Mathf.Lerp(1f, 0.82f, activeLenderCaution01));
                weeklyPaymentCapacity = Mathf.RoundToInt(weeklyPaymentCapacity * Mathf.Lerp(1f, 0.86f, activeLenderCaution01));
            }

            float lenderTrustFloor01 = Mathf.Clamp01(GetWorkingCapitalTrustFloor01(sanitizedLender) + activeLenderCaution01 * 0.08f);
            List<FinancingReasonCode> reasons = new();

            if (principal <= 0)
            {
                reasons.Add(FinancingReasonCode.OutsideLenderPolicy);
            }

            if (!sanitizedLender.AllowsPurpose(LoanPurpose.WorkingCapital))
            {
                reasons.Add(FinancingReasonCode.OutsideLenderPolicy);
            }

            if (hasActiveBankLoan)
            {
                reasons.Add(FinancingReasonCode.HeavyDebtBurden);
            }

            if (effectiveTrust < lenderTrustFloor01)
            {
                reasons.Add(FinancingReasonCode.LowLenderTrust);
            }

            if (effectivePriorFailedFinancingCount >= 2 || priorDefaultCount >= 2)
            {
                reasons.Add(FinancingReasonCode.FailedClosingHistory);
            }

            if (cautionDaysRemaining > 0)
            {
                reasons.Add(FinancingReasonCode.RecentLenderSetback);
            }

            if (principal > principalCapacity)
            {
                reasons.Add(FinancingReasonCode.ExceedsLoanToValue);
            }

            if (estimatedPayment > weeklyPaymentCapacity)
            {
                reasons.Add(existingDebtPayment > 0 ? FinancingReasonCode.HeavyDebtBurden : FinancingReasonCode.WeakCashFlow);
            }

            bool approved = reasons.Count == 0;
            float principalUtilization01 = principalCapacity > 0 ? Mathf.Clamp01((float)principal / Mathf.Max(1, principalCapacity)) : 1f;
            float paymentUtilization01 = weeklyPaymentCapacity > 0 ? Mathf.Clamp01((float)estimatedPayment / Mathf.Max(1, weeklyPaymentCapacity)) : 1f;
            BankLoanReviewBand reviewBand = DetermineReviewBand(
                approved,
                effectiveTrust,
                principalUtilization01,
                paymentUtilization01,
                effectivePriorFailedFinancingCount,
                priorDefaultCount,
                sanitizedLender);
            int reviewDelayDays = GetReviewDelayDays(reviewBand, sanitizedLender) + (cautionDaysRemaining > 0 ? 1 : 0);
            int reviewDays = Mathf.Max(1, 1 + reviewDelayDays);

            if (approved)
            {
                reasons.Add(FinancingReasonCode.WithinLenderPolicy);
                if (effectiveTrust >= Mathf.Max(0.55f, lenderTrustFloor01 + 0.1f))
                {
                    reasons.Add(FinancingReasonCode.StrongReputation);
                }

                if (safeWeeklyNetCashFlow > 0)
                {
                    reasons.Add(FinancingReasonCode.PositiveCashFlow);
                }

                if (safeAssetValue > 0)
                {
                    reasons.Add(FinancingReasonCode.StrongCollateral);
                }
            }

            string reviewContext = BuildReviewContext(
                sanitizedLender,
                reviewBand,
                approved,
                effectiveTrust,
                principalUtilization01,
                paymentUtilization01,
                lenderTrustFloor01,
                cautionDaysRemaining);

            return new BankLoanUnderwritingResult
            {
                decision = approved ? FinancingDecision.Approved : FinancingDecision.Declined,
                purpose = LoanPurpose.WorkingCapital,
                term = term,
                estimatedPaymentCents = estimatedPayment,
                estimatedWeeklyEquivalentPaymentCents = PaymentScheduleBuilder.ToWeeklyEquivalentCents(term, estimatedPayment),
                estimatedPrincipalCents = term.principalCents,
                estimatedInterestCents = Mathf.Max(0, estimateSchedule.totalInterestCents),
                estimatedOriginationFeeCents = Mathf.Max(0, term.originationFeeCents),
                estimatedTotalOwedCents = PaymentScheduleBuilder.CalculateTotalBorrowerCostCents(term, estimateSchedule),
                principalCapacityCents = principalCapacity,
                weeklyPaymentCapacityCents = weeklyPaymentCapacity,
                reasonCodes = Distinct(reasons),
                summary = cautionDaysRemaining > 0
                    ? "Working-capital file is temporarily restricted after a recent lender setback"
                    : BuildUnderwritingSummary(approved, reviewBand),
                declineReason = approved ? string.Empty : BuildDeclineReason(reasons),
                reviewBand = reviewBand,
                reviewDelayDays = reviewDelayDays,
                reviewDays = reviewDays,
                reviewContext = reviewContext
            };
        }

        private void RefreshEstimatedApplication()
        {
            if (HasPendingApplication)
            {
                return;
            }

            LoanTermStructure term = BuildTerm(RequestedAmountCents);
            application ??= new BankLoanApplicationState();
            application.requestedAmountCents = RequestedAmountCents;
            application.purpose = LoanPurpose.WorkingCapital;
            application.estimatedTerm = term;
            application.estimatedSchedule = scheduleBuilder.Build(
                BuildLoanId("estimate", GetCurrentDayIndex()),
                term,
                GetCurrentDate(),
                FirstPaymentOffsetDays,
                GetDaysPerMonth());
        }

        private LoanTermStructure BuildTerm(int principalCents)
        {
            return BuildTerm(principalCents, lender, LenderTrust01);
        }

        private LoanTermStructure BuildAcquisitionRequestedTerm()
        {
            return new LoanTermStructure
            {
                principalCents = 0,
                downPaymentCents = 0,
                annualInterestRateBps = 0,
                termMonths = 60,
                amortizationMonths = 60,
                repaymentFrequency = RepaymentFrequency.Weekly,
                originationFeeCents = 0,
                balloonPaymentCents = 0
            };
        }

        private static LoanPurpose InferAcquisitionLoanPurpose(LoanCollateralKind collateralKind)
        {
            return collateralKind == LoanCollateralKind.OperatingBusiness || collateralKind == LoanCollateralKind.Inventory
                ? LoanPurpose.BusinessAcquisition
                : LoanPurpose.PropertyPurchase;
        }

        private static string FormatLoanPurpose(LoanPurpose purpose)
        {
            string display = LoanPurposeRules.GetDisplayName(purpose);
            return string.IsNullOrWhiteSpace(display)
                ? "loan"
                : char.ToUpperInvariant(display[0]) + display.Substring(1);
        }

        private static CollateralProfile BuildDefaultCollateralProfile(int collateralValueCents, LoanCollateralKind collateralKind, string collateralId)
        {
            float liquidity01;
            float condition01;

            switch (collateralKind)
            {
                case LoanCollateralKind.Land:
                    liquidity01 = 0.62f;
                    condition01 = 0.9f;
                    break;
                case LoanCollateralKind.ImprovedProperty:
                    liquidity01 = 0.56f;
                    condition01 = 0.78f;
                    break;
                case LoanCollateralKind.OperatingBusiness:
                    liquidity01 = 0.42f;
                    condition01 = 0.68f;
                    break;
                case LoanCollateralKind.Inventory:
                    liquidity01 = 0.35f;
                    condition01 = 0.72f;
                    break;
                default:
                    liquidity01 = 0f;
                    condition01 = 0f;
                    break;
            }

            return new CollateralProfile
            {
                collateralId = collateralId ?? string.Empty,
                kind = collateralKind,
                estimatedValueCents = Mathf.Max(0, collateralValueCents),
                lienPriority = 1,
                liquidity01 = liquidity01,
                condition01 = condition01,
                encumberedValueCents = 0
            };
        }

        private int GetWeeklyNetCashFlowCents()
        {
            int lastWeeklyDistributionCents = GetPortfolioLastWeeklyDistributionCents();
            if (lastWeeklyDistributionCents > 0)
            {
                return lastWeeklyDistributionCents;
            }

            return GetStoreEstimatedWeeklyNetCashFlowCents();
        }

        private static LoanTermStructure BuildTerm(int principalCents, LenderProfile lenderProfile, float trust01)
        {
            LenderProfile sanitized = lenderProfile.Sanitized();
            float conservatism01 = GetLenderConservatism01(sanitized);
            int trustAdjustmentBps = Mathf.RoundToInt(Mathf.Lerp(250f, -100f, Mathf.Clamp01(trust01)));
            int postureAdjustmentBps = Mathf.RoundToInt(Mathf.Lerp(-40f, 85f, conservatism01));
            int rateAdjustmentBps = trustAdjustmentBps + postureAdjustmentBps;
            return new LoanTermStructure
            {
                principalCents = Mathf.Max(0, principalCents),
                downPaymentCents = 0,
                annualInterestRateBps = Mathf.Max(0, sanitized.baseAnnualInterestRateBps + rateAdjustmentBps),
                termMonths = LoanTermMonths,
                amortizationMonths = LoanTermMonths,
                repaymentFrequency = RepaymentFrequency.Weekly,
                originationFeeCents = 0,
                balloonPaymentCents = 0
            };
        }

        private int GetOwnedAssetValueCents()
        {
            if (acquisitionMarket == null)
            {
                return 0;
            }

            int total = 0;
            IEnumerable appreciations = GetOwnedLandAppreciationsEnumerable();
            if (appreciations == null)
            {
                return 0;
            }

            foreach (object state in appreciations)
            {
                if (state == null)
                {
                    continue;
                }

                int currentEstimatedValueCents = GetIntMemberValue(state, "currentEstimatedValueCents", "CurrentEstimatedValueCents");
                int totalBasisCents = GetIntMemberValue(state, "TotalBasisCents", "totalBasisCents");
                total += Mathf.Max(currentEstimatedValueCents, totalBasisCents);
            }

            return Mathf.Max(0, total);
        }

        private void UpdateNextPaymentDueDate()
        {
            LoanPaymentDue next = GetNextUnpaidPayment(activeLoan);
            if (activeLoan != null && next != null)
            {
                activeLoan.nextPaymentDueDate = next.dueDate;
            }
        }

        private bool HasWorkoutCollateralSupport()
        {
            if (activeLoan == null)
            {
                return false;
            }

            if (GetOwnedAssetValueCents() > 0)
            {
                return true;
            }

            if (activeLoan.collateralIds == null)
            {
                return false;
            }

            for (int i = 0; i < activeLoan.collateralIds.Count; i++)
            {
                if (!string.IsNullOrWhiteSpace(activeLoan.collateralIds[i]))
                {
                    return true;
                }
            }

            return false;
        }

        private static int CalculatePastDueInterestCents(LoanContract loan, int absoluteDayIndex)
        {
            if (loan == null || loan.schedule == null || loan.schedule.payments == null)
            {
                return 0;
            }

            int total = 0;
            for (int i = 0; i < loan.schedule.payments.Count; i++)
            {
                LoanPaymentDue payment = loan.schedule.payments[i];
                if (IsPaymentOutstanding(payment) && payment.dueDayIndex <= absoluteDayIndex)
                {
                    total += Mathf.Max(0, payment.interestCents);
                }
            }

            return total;
        }

        private static int GetRemainingScheduledMonths(LoanContract loan)
        {
            if (loan == null || loan.schedule == null || loan.schedule.payments == null)
            {
                return MinimumWorkoutTermMonths;
            }

            int remainingPaymentCount = 0;
            for (int i = 0; i < loan.schedule.payments.Count; i++)
            {
                if (IsPaymentOutstanding(loan.schedule.payments[i]))
                {
                    remainingPaymentCount++;
                }
            }

            if (remainingPaymentCount <= 0)
            {
                return MinimumWorkoutTermMonths;
            }

            if (loan.termStructure.repaymentFrequency == RepaymentFrequency.Monthly)
            {
                return remainingPaymentCount;
            }

            return Mathf.Max(1, Mathf.CeilToInt(remainingPaymentCount * 12f / 52f));
        }

        private void MarkPaidOffIfComplete()
        {
            if (activeLoan == null || activeLoan.schedule == null || activeLoan.schedule.payments == null)
            {
                return;
            }

            for (int i = 0; i < activeLoan.schedule.payments.Count; i++)
            {
                if (IsPaymentOutstanding(activeLoan.schedule.payments[i]))
                {
                    return;
                }
            }

            activeLoan.status = LoanStatus.PaidOff;
            activeLoan.remainingPrincipalCents = 0;
            consecutiveMissedPayments = 0;
            lastMissedPaymentDayIndex = -1;
            lastRepaymentSummary = "Bank loan paid off.";
            lastStatusSummary = lastRepaymentSummary;
        }

        private static bool IsUnresolvedDebt(LoanContract loan)
        {
            return loan != null
                && (loan.status == LoanStatus.Active
                    || loan.status == LoanStatus.Delinquent
                    || loan.status == LoanStatus.Defaulted);
        }

        private static bool IsRepayableDebt(LoanContract loan)
        {
            return loan != null
                && (loan.status == LoanStatus.Active || loan.status == LoanStatus.Delinquent);
        }

        private static void MarkUnpaidPaymentsSkipped(LoanContract loan)
        {
            if (loan == null || loan.schedule == null || loan.schedule.payments == null)
            {
                return;
            }

            for (int i = 0; i < loan.schedule.payments.Count; i++)
            {
                LoanPaymentDue payment = loan.schedule.payments[i];
                if (IsPaymentOutstanding(payment))
                {
                    payment.status = LoanPaymentStatus.Skipped;
                }
            }
        }

        private static LoanPaymentDue GetOldestDuePayment(LoanContract loan, int absoluteDayIndex)
        {
            if (loan == null || loan.schedule == null || loan.schedule.payments == null)
            {
                return null;
            }

            for (int i = 0; i < loan.schedule.payments.Count; i++)
            {
                LoanPaymentDue payment = loan.schedule.payments[i];
                if (IsPaymentOutstanding(payment) && payment.dueDayIndex <= absoluteDayIndex)
                {
                    return payment;
                }
            }

            return null;
        }

        private static LoanPaymentDue GetNextUnpaidPayment(LoanContract loan)
        {
            if (loan == null || loan.schedule == null || loan.schedule.payments == null)
            {
                return null;
            }

            for (int i = 0; i < loan.schedule.payments.Count; i++)
            {
                LoanPaymentDue payment = loan.schedule.payments[i];
                if (IsPaymentOutstanding(payment))
                {
                    return payment;
                }
            }

            return null;
        }

        private static bool IsPaymentOutstanding(LoanPaymentDue payment)
        {
            return payment != null
                && payment.status != LoanPaymentStatus.Paid
                && payment.status != LoanPaymentStatus.Skipped;
        }

        private void ApplyReputationEvent(ReputationEventType eventType, float severity01, string note, int absoluteDayIndex)
        {
            reputation ??= new PlayerReputationState(0.55f, lender.Sanitized().trust01, 0.5f, 0.5f, 0.55f);
            reputationApplier.Apply(reputation, new ReputationEvent
            {
                eventType = eventType,
                sourceSystem = "Bank Loan",
                counterpartyId = lender.Sanitized().lenderId,
                businessId = GetCurrentBusinessInstanceId(),
                assetId = activeLoan != null ? activeLoan.loanId : string.Empty,
                date = BuildDate(absoluteDayIndex),
                absoluteDayIndex = absoluteDayIndex,
                severity01 = Mathf.Clamp01(severity01),
                confidence01 = 1f,
                note = note ?? string.Empty
            });
        }

        private float GetLateSeverity()
        {
            return Mathf.Clamp01(0.35f + Mathf.Max(0, consecutiveMissedPayments - 1) * 0.15f);
        }

        private string BuildDelinquencySummary(LoanPaymentDue payment)
        {
            string amount = payment != null ? FormatMoney(payment.totalDueCents) : "the scheduled payment";
            if (consecutiveMissedPayments >= FinalWarningMissedPaymentCount)
            {
                return $"Final recovery warning: {amount} remains unpaid. Another missed review will trigger forced recovery.";
            }

            if (consecutiveMissedPayments >= 2)
            {
                return $"Repeat delinquency: {amount} remains unpaid. Lender standing has fallen.";
            }

            return $"Missed payment warning: {amount} was not available for the scheduled bank loan payment.";
        }

        private static List<FinancingReasonCode> Distinct(List<FinancingReasonCode> reasonCodes)
        {
            List<FinancingReasonCode> distinct = new();
            if (reasonCodes == null)
            {
                return distinct;
            }

            for (int i = 0; i < reasonCodes.Count; i++)
            {
                FinancingReasonCode code = reasonCodes[i];
                if (code != FinancingReasonCode.None && !distinct.Contains(code))
                {
                    distinct.Add(code);
                }
            }

            return distinct;
        }

        private static string BuildPendingReviewStatus(BankLoanReviewBand reviewBand, SimulationDate decisionDate, string reviewContext)
        {
            return AppendReviewContextSentence(
                $"Loan Application Submitted. Status: Pending {DescribeReviewBand(reviewBand)} Review. Decision Expected: {FormatDate(decisionDate)}.",
                reviewContext);
        }

        private static string BuildPendingReviewDecisionMessage(BankLoanReviewBand reviewBand, SimulationDate decisionDate, string reviewContext)
        {
            string baseMessage = reviewBand switch
            {
                BankLoanReviewBand.Routine => $"Your request has been sent to the bank for routine review. Return on {FormatDate(decisionDate)} for their answer.",
                BankLoanReviewBand.Watchlist => $"Your request has been sent to the bank for watchlist review because it sits near the bank's ordinary comfort range. Return on {FormatDate(decisionDate)} for their answer.",
                _ => $"Your request has been sent to the bank for significant review because it requires senior scrutiny. Return on {FormatDate(decisionDate)} for their answer."
            };

            return AppendReviewContextSentence(baseMessage, reviewContext);
        }

        private static string BuildPendingReviewPanelText(BankLoanApplicationState state)
        {
            if (state == null)
            {
                return "Loan Application Submitted\nStatus: Pending Review";
            }

            return $"Loan Application Submitted\nPurpose: {FormatLoanPurpose(state.purpose)}\nStatus: Pending {DescribeReviewBand(state.reviewBand)} Review\nDecision Expected: {FormatDate(state.decisionDate)}\n{BuildPendingReviewDecisionMessage(state.reviewBand, state.decisionDate, state.reviewContext)}";
        }

        private static string BuildApprovalSummary(BankLoanReviewBand reviewBand)
        {
            return reviewBand switch
            {
                BankLoanReviewBand.Routine => "Loan Approved",
                BankLoanReviewBand.Watchlist => "Loan Approved After Watchlist Review",
                _ => "Loan Approved After Significant Review"
            };
        }

        private static string BuildUnderwritingSummary(bool approved, BankLoanReviewBand reviewBand)
        {
            if (!approved)
            {
                return reviewBand == BankLoanReviewBand.Significant
                    ? "Application does not fit local bank policy after significant review."
                    : "Application does not fit local bank policy.";
            }

            return reviewBand switch
            {
                BankLoanReviewBand.Routine => "Application fits routine local bank policy.",
                BankLoanReviewBand.Watchlist => "Application is bankable but should move through watchlist review before funding.",
                _ => "Application is bankable but should move through significant review before funding."
            };
        }

        private static string BuildApprovalDecisionMessage(BankLoanUnderwritingResult result, SimulationDate firstPaymentDueDate)
        {
            if (result == null)
            {
                return $"Loan Approved. First payment due: {FormatDate(firstPaymentDueDate)}.";
            }

            return AppendReviewContextSentence(
                $"{BuildApprovalSummary(result.reviewBand)} for {FormatLoanPurpose(result.purpose)}. First payment due: {FormatDate(firstPaymentDueDate)}.",
                result.reviewContext);
        }

        private static string BuildDeclineDecisionMessage(BankLoanUnderwritingResult result)
        {
            if (result == null)
            {
                return "Application does not fit local bank policy.";
            }

            string summary = AppendReviewContextSentence(result.summary, result.reviewContext);
            return string.IsNullOrWhiteSpace(result.declineReason)
                ? summary
                : $"{summary} Reason: {result.declineReason}.";
        }

        // Review bands keep ordinary files moving while routing thinner files through a stricter local-bank lane.
        // They are intentionally heuristic so lender posture can shape scrutiny without a second review subsystem.
        private static BankLoanReviewBand DetermineReviewBand(
            bool approved,
            float trust01,
            float principalUtilization01,
            float paymentUtilization01,
            int priorFailedFinancingCount,
            int priorDefaultCount,
            LenderProfile lenderProfile)
        {
            float conservatism01 = GetLenderConservatism01(lenderProfile);
            float routineTrustFloor01 = Mathf.Lerp(0.4f, 0.52f, conservatism01);
            float routinePrincipalCap01 = Mathf.Lerp(0.7f, 0.54f, conservatism01);
            float routinePaymentCap01 = Mathf.Lerp(0.64f, 0.5f, conservatism01);
            float significantTrustFloor01 = Mathf.Lerp(0.24f, 0.34f, conservatism01);
            float significantPrincipalFloor01 = Mathf.Lerp(0.9f, 0.78f, conservatism01);
            float significantPaymentFloor01 = Mathf.Lerp(0.86f, 0.74f, conservatism01);

            if (!approved)
            {
                if (principalUtilization01 > significantPrincipalFloor01
                    || paymentUtilization01 > significantPaymentFloor01
                    || trust01 < significantTrustFloor01
                    || priorDefaultCount > 0)
                {
                    return BankLoanReviewBand.Significant;
                }

                return BankLoanReviewBand.Watchlist;
            }

            bool routineSafe = trust01 >= routineTrustFloor01
                && priorFailedFinancingCount <= 0
                && priorDefaultCount <= 0
                && principalUtilization01 <= routinePrincipalCap01
                && paymentUtilization01 <= routinePaymentCap01;

            if (routineSafe)
            {
                return BankLoanReviewBand.Routine;
            }

            bool significant = trust01 < significantTrustFloor01
                || priorDefaultCount > 0
                || principalUtilization01 >= significantPrincipalFloor01
                || paymentUtilization01 >= significantPaymentFloor01;

            return significant
                ? BankLoanReviewBand.Significant
                : BankLoanReviewBand.Watchlist;
        }

        private static int GetReviewDelayDays(BankLoanReviewBand reviewBand, LenderProfile lenderProfile)
        {
            int baseDelay = reviewBand switch
            {
                BankLoanReviewBand.Routine => 0,
                BankLoanReviewBand.Watchlist => 1,
                _ => 2
            };

            float conservatism01 = GetLenderConservatism01(lenderProfile);
            int lenderAdjustment = conservatism01 >= 0.62f ? 1 : conservatism01 <= 0.25f ? -1 : 0;
            int delay = baseDelay + lenderAdjustment;
            int minimumDelay = reviewBand == BankLoanReviewBand.Routine ? 0 : 1;
            return Mathf.Max(minimumDelay, delay);
        }

        private static int CalculateWorkingCapitalPrincipalCapacityCents(int safeCashCents, int safeAssetValueCents, float trust01, LenderProfile lenderProfile)
        {
            // Working-capital files should treat owned assets as support, not as if the bank were taking the full portfolio at face value.
            float conservatism01 = GetLenderConservatism01(lenderProfile);
            float cashAdvanceMultiple = Mathf.Lerp(2.55f, 1.25f, conservatism01) * Mathf.Lerp(0.9f, 1.1f, Mathf.Clamp01(trust01));
            float assetAdvanceRatio = Mathf.Lerp(0.42f, 0.2f, conservatism01);
            assetAdvanceRatio *= Mathf.Lerp(0.9f, 1.05f, lenderProfile.maxLoanToValue01);
            assetAdvanceRatio *= 1f - lenderProfile.collateralHaircut01 * 0.55f;
            return Mathf.Max(
                MinimumPrincipalCapacityCents,
                Mathf.RoundToInt(safeCashCents * cashAdvanceMultiple + safeAssetValueCents * assetAdvanceRatio));
        }

        private static int CalculateWorkingCapitalWeeklyPaymentCapacityCents(int safeCashCents, int safeWeeklyNetCashFlowCents, int existingDebtPaymentCents, LenderProfile lenderProfile)
        {
            float conservatism01 = GetLenderConservatism01(lenderProfile);
            float cashSupportShare = Mathf.Lerp(0.1f, 0.06f, conservatism01);
            float cashFlowSupportShare = Mathf.Lerp(0.4f, 0.28f, conservatism01);
            int grossSupportCents = Mathf.Max(
                Mathf.RoundToInt(safeCashCents * cashSupportShare),
                Mathf.RoundToInt(safeWeeklyNetCashFlowCents * cashFlowSupportShare));
            int netSupportCents = Mathf.Max(0, grossSupportCents - Mathf.Max(0, existingDebtPaymentCents));
            float coverageRatio = Mathf.Max(1.05f, lenderProfile.minimumCashFlowCoverageRatio);
            return Mathf.Max(0, Mathf.FloorToInt(netSupportCents / coverageRatio));
        }


        private static int GetRemainingCooldownDays(int currentDayIndex, int lenderCooldownUntilDayIndex)
        {
            if (lenderCooldownUntilDayIndex < 0)
            {
                return 0;
            }

            return Mathf.Max(0, lenderCooldownUntilDayIndex - Mathf.Max(0, currentDayIndex));
        }

        private static float GetWorkingCapitalTrustFloor01(LenderProfile lenderProfile)
        {
            float conservatism01 = GetLenderConservatism01(lenderProfile);
            return Mathf.Clamp01(Mathf.Max(0.18f, lenderProfile.minimumReputation01 * Mathf.Lerp(0.55f, 0.9f, conservatism01)));
        }

        private static float GetLenderConservatism01(LenderProfile lenderProfile)
        {
            // This keeps lender personality inside existing profile data instead of introducing a second underwriting authority.
            LenderProfile sanitized = lenderProfile.Sanitized();
            float riskReserve01 = 1f - sanitized.riskTolerance01;
            float coverageTightness01 = Mathf.InverseLerp(1.05f, 1.3f, sanitized.minimumCashFlowCoverageRatio);
            float haircutTightness01 = Mathf.InverseLerp(0.08f, 0.24f, sanitized.collateralHaircut01);
            float leverageTightness01 = 1f - Mathf.InverseLerp(0.65f, 0.85f, sanitized.maxLoanToValue01);
            return Mathf.Clamp01(riskReserve01 * 0.45f + coverageTightness01 * 0.25f + haircutTightness01 * 0.15f + leverageTightness01 * 0.15f);
        }

        private static string BuildReviewContext(
            LenderProfile lenderProfile,
            BankLoanReviewBand reviewBand,
            bool approved,
            float trust01,
            float principalUtilization01,
            float paymentUtilization01,
            float lenderTrustFloor01,
            int cautionDaysRemaining)
        {
            string lenderName = string.IsNullOrWhiteSpace(lenderProfile.displayName) ? "The bank" : lenderProfile.displayName;
            string posture = DescribeLenderWorkingCapitalPosture(lenderProfile);

            if (cautionDaysRemaining > 0)
            {
                return $"{lenderName} is still cooling off for about {Mathf.Max(1, cautionDaysRemaining)} days after a recent setback, so it is not taking a fresh working-capital file yet.";
            }

            if (!approved)
            {
                if (trust01 < lenderTrustFloor01)
                {
                    return $"{lenderName} is lending from a {posture} posture and lender confidence is still below its comfort floor.";
                }

                if (principalUtilization01 > 0.95f || paymentUtilization01 > 0.95f)
                {
                    return $"{lenderName} is lending from a {posture} posture and this file runs past its ordinary working-capital capacity.";
                }

                return $"{lenderName} is lending from a {posture} posture and this file sits outside current local bank comfort.";
            }

            return reviewBand switch
            {
                BankLoanReviewBand.Routine => $"{lenderName} is lending from a {posture} posture and this file sits comfortably inside routine policy.",
                BankLoanReviewBand.Watchlist => $"{lenderName} is lending from a {posture} posture, so this file stays in watchlist review until the bank is satisfied with its headroom.",
                _ => $"{lenderName} is lending from a {posture} posture and wants senior scrutiny before funding this file."
            };
        }

        private static string DescribeLenderWorkingCapitalPosture(LenderProfile lenderProfile)
        {
            float conservatism01 = GetLenderConservatism01(lenderProfile);
            if (conservatism01 >= 0.62f)
            {
                return "conservative local bank";
            }

            if (conservatism01 <= 0.25f)
            {
                return "growth-minded local bank";
            }

            return "balanced local bank";
        }

        private static string AppendReviewContextSentence(string message, string reviewContext)
        {
            if (string.IsNullOrWhiteSpace(reviewContext))
            {
                return message ?? string.Empty;
            }

            if (string.IsNullOrWhiteSpace(message))
            {
                return reviewContext;
            }

            string trimmed = message.TrimEnd();
            if (!trimmed.EndsWith("."))
            {
                trimmed += ".";
            }

            return $"{trimmed} Underwriter note: {reviewContext}";
        }

        private static string DescribeReviewBand(BankLoanReviewBand reviewBand)
        {
            return reviewBand switch
            {
                BankLoanReviewBand.Routine => "Routine",
                BankLoanReviewBand.Watchlist => "Watchlist",
                _ => "Significant"
            };
        }

        private static string BuildDeclineReason(List<FinancingReasonCode> reasonCodes)
        {
            if (reasonCodes == null || reasonCodes.Count == 0)
            {
                return "requested amount too high";
            }

            if (reasonCodes.Contains(FinancingReasonCode.RecentLenderSetback))
            {
                return "bank asks for time before a new file";
            }

            if (reasonCodes.Contains(FinancingReasonCode.ExceedsLoanToValue))
            {
                return "requested amount too high";
            }

            if (reasonCodes.Contains(FinancingReasonCode.LowLenderTrust))
            {
                return "lender confidence too low";
            }

            if (reasonCodes.Contains(FinancingReasonCode.FailedClosingHistory))
            {
                return "recent failed financing history";
            }

            if (reasonCodes.Contains(FinancingReasonCode.HeavyDebtBurden) || reasonCodes.Contains(FinancingReasonCode.WeakCashFlow))
            {
                return "existing debt burden too high";
            }

            return "outside lender policy";
        }

        private static string BuildAcquisitionReadyMessage(FinancingApprovalResult approval)
        {
            if (approval?.Offer == null)
            {
                return "Acquisition financing could not be evaluated.";
            }

            LoanOffer offer = approval.Offer;
            string feeNote = offer.EstimatedOriginationFeeCents > 0
                ? $" Bank fee {FormatMoney(offer.EstimatedOriginationFeeCents)} is collected through the first scheduled payment."
                : string.Empty;
            return $"Acquisition financing ready to close for {FormatLoanPurpose(offer.purpose).ToLowerInvariant()}. Principal {FormatMoney(offer.EstimatedPrincipalCents)}. Offer good until {FormatDate(offer.expiresOnDate)}.{feeNote}";
        }

        private static string BuildFirstPaymentSummary(LoanContract loan)
        {
            if (loan == null)
            {
                return "First payment due: not scheduled.";
            }

            LoanPaymentDue firstPayment = loan.schedule?.payments?.FirstOrDefault(IsPaymentOutstanding);
            if (firstPayment == null)
            {
                return "First payment due: not scheduled.";
            }

            string feeSuffix = DoesPaymentIncludeOriginationFee(loan.schedule, firstPayment) ? " including bank fee" : string.Empty;
            return $"First payment due: {FormatMoney(firstPayment.totalDueCents)}{feeSuffix} on {FormatDate(firstPayment.dueDate)}.";
        }

        private static bool DoesPaymentIncludeOriginationFee(LoanPaymentSchedule schedule, LoanPaymentDue payment)
        {
            if (schedule == null || payment == null || schedule.originationFeeCents <= 0)
            {
                return false;
            }

            int rowOverage = Mathf.Max(0, payment.totalDueCents - Mathf.Max(0, payment.principalCents) - Mathf.Max(0, payment.interestCents));
            return rowOverage > 0;
        }

        private static string BuildAcquisitionDecisionMessage(FinancingApprovalResult approval)
        {
            if (approval == null)
            {
                return "Acquisition financing could not be evaluated.";
            }

            string gapSummary = BuildAcquisitionGapSummary(approval, 2);
            string conditionSummary = BuildConditionSummary(approval.RequiredConditions, 2);
            string reasonLead = BuildAcquisitionReasonLead(approval.ReasonCodes, approval.Decision);
            string offerExpiry = approval.HasOffer ? $" Offer good until {FormatDate(approval.Offer.expiresOnDate)}." : string.Empty;
            if (approval.Decision == FinancingDecision.ConditionallyApproved)
            {
                if (!string.IsNullOrWhiteSpace(gapSummary))
                {
                    return $"Acquisition financing conditionally approved. Still missing: {gapSummary}.{offerExpiry}";
                }

                if (!string.IsNullOrWhiteSpace(reasonLead))
                {
                    return $"Acquisition financing conditionally approved. Main bank concern: {reasonLead}.{offerExpiry}";
                }

                return string.IsNullOrWhiteSpace(conditionSummary)
                    ? $"Acquisition financing conditionally approved. Bank conditions remain unresolved before funding.{offerExpiry}"
                    : $"Acquisition financing conditionally approved. {conditionSummary}{offerExpiry}";
            }

            if (approval.Decision == FinancingDecision.Declined)
            {
                if (!string.IsNullOrWhiteSpace(gapSummary))
                {
                    return $"Acquisition financing declined. Still missing: {gapSummary}.{offerExpiry}";
                }

                if (!string.IsNullOrWhiteSpace(reasonLead))
                {
                    return $"Acquisition financing declined. Main bank concern: {reasonLead}.{offerExpiry}";
                }

                return string.IsNullOrWhiteSpace(conditionSummary)
                    ? $"Acquisition financing declined.{offerExpiry}"
                    : $"Acquisition financing declined. {conditionSummary}{offerExpiry}";
            }

            return $"Acquisition financing approved.{offerExpiry}";
        }

        private static string DescribeAcquisitionDecision(FinancingDecision decision)
        {
            return decision switch
            {
                FinancingDecision.Approved => "Acquisition financing approved",
                FinancingDecision.ConditionallyApproved => "Acquisition financing conditionally approved",
                FinancingDecision.Declined => "Acquisition financing declined",
                _ => "Acquisition financing review"
            };
        }

        private static string BuildAcquisitionReasonLead(IReadOnlyList<FinancingReasonCode> reasonCodes, FinancingDecision decision)
        {
            if (reasonCodes == null || reasonCodes.Count == 0)
            {
                return string.Empty;
            }

            if (reasonCodes.Contains(FinancingReasonCode.RecentLenderSetback))
            {
                return decision == FinancingDecision.ConditionallyApproved
                    ? "recent lender setback is still cooling off"
                    : "recent lender setback is still blocking a fresh file";
            }

            if (reasonCodes.Contains(FinancingReasonCode.ExceedsLoanToValue) || reasonCodes.Contains(FinancingReasonCode.MissingCollateral))
            {
                return "collateral support is not strong enough yet";
            }

            if (reasonCodes.Contains(FinancingReasonCode.FailedClosingHistory))
            {
                return "recent failed financing history remains too fresh";
            }

            if (reasonCodes.Contains(FinancingReasonCode.LowLenderTrust))
            {
                return "lender standing is still too weak";
            }

            if (reasonCodes.Contains(FinancingReasonCode.WeakOperationalRecord))
            {
                return "operating record still needs to strengthen";
            }

            if (reasonCodes.Contains(FinancingReasonCode.WeakReputation))
            {
                return "borrower standing is still below policy";
            }

            if (reasonCodes.Contains(FinancingReasonCode.HeavyDebtBurden) || reasonCodes.Contains(FinancingReasonCode.WeakCashFlow))
            {
                return "weekly cash support is still too thin";
            }

            if (reasonCodes.Contains(FinancingReasonCode.OutsideLenderPolicy))
            {
                return "the file sits outside current lender policy";
            }

            return string.Empty;
        }

        private static string BuildConditionSummary(IReadOnlyList<string> conditions, int maxConditions)
        {
            if (conditions == null || conditions.Count == 0 || maxConditions <= 0)
            {
                return string.Empty;
            }

            int count = Mathf.Min(maxConditions, conditions.Count);
            List<string> trimmed = new List<string>(count);
            for (int i = 0; i < count; i++)
            {
                if (!string.IsNullOrWhiteSpace(conditions[i]))
                {
                    trimmed.Add(conditions[i]);
                }
            }

            return trimmed.Count > 0 ? string.Join(" ", trimmed) : string.Empty;
        }

        private static string BuildAcquisitionGapSummary(FinancingApprovalResult approval, int maxItems)
        {
            if (approval == null || maxItems <= 0)
            {
                return string.Empty;
            }

            List<string> gaps = new List<string>(maxItems);
            AddGapText(gaps, approval.AdditionalDownPaymentNeededCents > 0, $"down payment +{FormatMoney(approval.AdditionalDownPaymentNeededCents)}", maxItems);
            AddGapText(gaps, approval.PrincipalSupportShortfallCents > 0, $"support gap +{FormatMoney(approval.PrincipalSupportShortfallCents)}", maxItems);
            AddGapText(gaps, approval.WeeklyCashFlowShortfallCents > 0, $"weekly free cash +{FormatMoney(approval.WeeklyCashFlowShortfallCents)}", maxItems);
            AddGapText(gaps, approval.ReputationShortfall01 > 0f, $"borrower standing +{Mathf.CeilToInt(approval.ReputationShortfall01 * 100f)} pts", maxItems);
            AddGapText(gaps, approval.LenderTrustShortfall01 > 0f, $"lender standing +{Mathf.CeilToInt(approval.LenderTrustShortfall01 * 100f)} pts", maxItems);
            return gaps.Count > 0 ? string.Join(" | ", gaps) : string.Empty;
        }

        private static void AddGapText(List<string> gaps, bool include, string text, int maxItems)
        {
            if (!include || gaps == null || gaps.Count >= maxItems || string.IsNullOrWhiteSpace(text))
            {
                return;
            }

            gaps.Add(text);
        }

        private static string DescribeRepaymentFrequency(RepaymentFrequency frequency)
        {
            return frequency == RepaymentFrequency.Monthly ? "monthly" : "weekly";
        }

        private static string FormatRateBps(int annualInterestRateBps)
        {
            return $"{Mathf.Max(0, annualInterestRateBps) / 100f:0.00}%";
        }

        private void SanitizeRuntime()
        {
            requestedAmountCents = Mathf.Max(0, requestedAmountCents);
            lender = lender.Sanitized();
            application = SanitizeApplicationState(application);
            activeLoan = SanitizeLoanContract(activeLoan);
            reputation ??= new PlayerReputationState(0.55f, lender.Sanitized().trust01, 0.5f, 0.5f, 0.55f);
            reputation.Clamp();
            consecutiveMissedPayments = Mathf.Max(0, consecutiveMissedPayments);
            lifetimeMissedPayments = Mathf.Max(0, lifetimeMissedPayments);
            defaultCount = Mathf.Max(0, defaultCount);
            priorFailedFinancingCount = Mathf.Max(0, priorFailedFinancingCount);
            lastMissedPaymentDayIndex = Mathf.Max(-1, lastMissedPaymentDayIndex);
            lastStatusSummary ??= string.Empty;
            lastDecisionSummary ??= string.Empty;
            lastRepaymentSummary ??= string.Empty;
            lastRecoverySummary ??= string.Empty;

            if (IsRepayableDebt(activeLoan))
            {
                EnsureActiveLoanHasSchedule();
                UpdateNextPaymentDueDate();
            }
        }


        private static LoanPurpose SanitizePurposeForDebtState(LoanPurpose purpose, LoanContract loan, BankLoanApplicationState state)
        {
            LoanPurpose sanitized = LoanPurposeRules.Sanitize(purpose, LoanPurpose.WorkingCapital);
            bool looksLikeAcquisition = (loan != null && !string.IsNullOrWhiteSpace(loan.loanId) && loan.loanId.IndexOf("acquisition", StringComparison.OrdinalIgnoreCase) >= 0)
                || (state != null && !string.IsNullOrWhiteSpace(state.applicationId) && state.applicationId.IndexOf("acquisition", StringComparison.OrdinalIgnoreCase) >= 0);
            bool looksLikeWorkingCapital = (loan != null && !string.IsNullOrWhiteSpace(loan.loanId) && loan.loanId.IndexOf("bank_loan", StringComparison.OrdinalIgnoreCase) >= 0)
                || (state != null && state.status != BankLoanApplicationStatus.Approved && state.requestedAmountCents > 0)
                || (loan != null && loan.collateralIds != null && loan.collateralIds.Contains("owner_cash_flow"));

            // Older saves predated explicit purposes, so enum default 0 could mean a bank-panel working-capital file rather than a true acquisition.
            if (sanitized == LoanPurpose.Acquisition)
            {
                return looksLikeAcquisition && !looksLikeWorkingCapital ? LoanPurpose.PropertyPurchase : LoanPurpose.WorkingCapital;
            }

            return sanitized;
        }

        private BankLoanApplicationState SanitizeApplicationState(BankLoanApplicationState state)
        {
            state ??= new BankLoanApplicationState();
            state.applicationId ??= string.Empty;
            state.status = Enum.IsDefined(typeof(BankLoanApplicationStatus), state.status) ? state.status : BankLoanApplicationStatus.None;
            state.decision = Enum.IsDefined(typeof(FinancingDecision), state.decision) ? state.decision : FinancingDecision.EstimateOnly;
            state.reviewBand = Enum.IsDefined(typeof(BankLoanReviewBand), state.reviewBand) ? state.reviewBand : BankLoanReviewBand.Routine;
            state.requestedAmountCents = Mathf.Max(0, state.requestedAmountCents);
            state.purpose = SanitizePurposeForDebtState(state.purpose, activeLoan, state);
            state.submittedDayIndex = Mathf.Max(-1, state.submittedDayIndex);
            state.scheduledReviewDays = Mathf.Max(1, state.scheduledReviewDays);
            state.decisionDayIndex = Mathf.Max(-1, state.decisionDayIndex);

            if (state.status == BankLoanApplicationStatus.PendingReview)
            {
                if (state.submittedDayIndex < 0)
                {
                    state.submittedDayIndex = Mathf.Max(0, GetCurrentDayIndex());
                    state.submittedDate = BuildDate(state.submittedDayIndex);
                }

                if (state.decisionDayIndex < state.submittedDayIndex)
                {
                    state.decisionDayIndex = state.submittedDayIndex + state.scheduledReviewDays;
                    state.decisionDate = BuildDate(state.decisionDayIndex);
                }

                state.decision = FinancingDecision.EstimateOnly;
            }
            else if (state.status == BankLoanApplicationStatus.Approved && state.decision == FinancingDecision.EstimateOnly)
            {
                state.decision = FinancingDecision.Approved;
            }
            else if (state.status == BankLoanApplicationStatus.Declined)
            {
                state.decision = FinancingDecision.Declined;
            }

            state.estimatedTerm = state.estimatedTerm.Sanitized();
            state.estimatedSchedule = SanitizePaymentSchedule(state.estimatedSchedule, state.estimatedTerm, state.applicationId, rebuildMissing: false);
            state.reasonCodes = SanitizeReasonCodes(state.reasonCodes);
            state.decisionSummary ??= string.Empty;
            state.declineReason ??= string.Empty;
            state.reviewContext ??= string.Empty;
            state.reapplyEarliestDayIndex = Mathf.Max(-1, state.reapplyEarliestDayIndex);
            if (state.reapplyEarliestDayIndex >= 0)
            {
                state.reapplyEarliestDate = BuildDate(state.reapplyEarliestDayIndex);
            }

            state.lenderCaution01 = Mathf.Clamp01(state.lenderCaution01);
            state.lenderMemorySummary ??= string.Empty;
            return state;
        }

        private LoanContract SanitizeLoanContract(LoanContract loan)
        {
            if (loan == null)
            {
                return null;
            }

            loan.loanId = string.IsNullOrWhiteSpace(loan.loanId) ? BuildLoanId("restored_loan", Mathf.Max(0, GetCurrentDayIndex())) : loan.loanId.Trim();
            loan.lenderId = string.IsNullOrWhiteSpace(loan.lenderId) ? lender.Sanitized().lenderId : loan.lenderId.Trim();
            loan.purpose = SanitizePurposeForDebtState(loan.purpose, loan, application);
            loan.status = Enum.IsDefined(typeof(LoanStatus), loan.status) ? loan.status : LoanStatus.Active;
            loan.termStructure = loan.termStructure.Sanitized();
            loan.collateralIds = SanitizeCollateralIds(loan.collateralIds);
            loan.schedule = SanitizePaymentSchedule(loan.schedule, loan.termStructure, loan.loanId, rebuildMissing: IsRepayableDebt(loan));
            int principalCeilingCents = Mathf.Max(loan.termStructure.principalCents, loan.schedule != null ? Mathf.Max(loan.schedule.principalCents, loan.schedule.totalPrincipalCents) : 0);
            loan.remainingPrincipalCents = principalCeilingCents > 0
                ? Mathf.Clamp(loan.remainingPrincipalCents, 0, principalCeilingCents)
                : Mathf.Max(0, loan.remainingPrincipalCents);
            loan.workoutCount = Mathf.Max(0, loan.workoutCount);
            loan.lastWorkoutDayIndex = Mathf.Max(-1, loan.lastWorkoutDayIndex);

            if (loan.status == LoanStatus.PaidOff || loan.status == LoanStatus.Recovered)
            {
                loan.remainingPrincipalCents = 0;
            }

            LoanPaymentDue next = GetNextUnpaidPayment(loan);
            if (next != null)
            {
                loan.nextPaymentDueDate = next.dueDate;
            }
            else if (loan.status == LoanStatus.Active || loan.status == LoanStatus.Delinquent)
            {
                loan.status = loan.remainingPrincipalCents > 0 ? LoanStatus.Active : LoanStatus.PaidOff;
            }

            return loan;
        }

        private void EnsureActiveLoanHasSchedule()
        {
            if (!IsRepayableDebt(activeLoan))
            {
                return;
            }

            activeLoan.schedule = SanitizePaymentSchedule(activeLoan.schedule, activeLoan.termStructure, activeLoan.loanId, rebuildMissing: true);
        }

        private LoanPaymentSchedule SanitizePaymentSchedule(LoanPaymentSchedule schedule, LoanTermStructure term, string loanId, bool rebuildMissing)
        {
            LoanTermStructure sanitizedTerm = term.Sanitized();
            if (schedule == null)
            {
                return rebuildMissing && sanitizedTerm.principalCents > 0
                    ? scheduleBuilder.Build(string.IsNullOrWhiteSpace(loanId) ? BuildLoanId("restored_schedule", GetCurrentDayIndex()) : loanId, sanitizedTerm, GetCurrentDate(), FirstPaymentOffsetDays, GetDaysPerMonth())
                    : null;
            }

            schedule.loanId = string.IsNullOrWhiteSpace(schedule.loanId)
                ? (string.IsNullOrWhiteSpace(loanId) ? BuildLoanId("restored_schedule", GetCurrentDayIndex()) : loanId)
                : schedule.loanId.Trim();
            schedule.principalCents = Mathf.Max(0, schedule.principalCents <= 0 ? sanitizedTerm.principalCents : schedule.principalCents);
            schedule.originationFeeCents = Mathf.Max(0, schedule.originationFeeCents <= 0 ? sanitizedTerm.originationFeeCents : schedule.originationFeeCents);
            schedule.payments ??= new List<LoanPaymentDue>();
            if (rebuildMissing && schedule.payments.Count == 0 && sanitizedTerm.principalCents > 0)
            {
                return scheduleBuilder.Build(schedule.loanId, sanitizedTerm, GetCurrentDate(), FirstPaymentOffsetDays, GetDaysPerMonth());
            }

            for (int i = schedule.payments.Count - 1; i >= 0; i--)
            {
                if (schedule.payments[i] == null)
                {
                    schedule.payments.RemoveAt(i);
                }
            }

            for (int i = 0; i < schedule.payments.Count; i++)
            {
                LoanPaymentDue payment = schedule.payments[i];
                payment.paymentNumber = payment.paymentNumber <= 0 ? i + 1 : payment.paymentNumber;
                payment.principalCents = Mathf.Max(0, payment.principalCents);
                payment.interestCents = Mathf.Max(0, payment.interestCents);
                int baseDueCents = payment.principalCents + payment.interestCents;
                payment.totalDueCents = Mathf.Max(baseDueCents, payment.totalDueCents);
                payment.paidCents = Mathf.Clamp(payment.paidCents, 0, payment.totalDueCents);
                payment.status = Enum.IsDefined(typeof(LoanPaymentStatus), payment.status) ? payment.status : LoanPaymentStatus.Scheduled;
                if (payment.status == LoanPaymentStatus.Paid && payment.paidCents < payment.totalDueCents)
                {
                    payment.status = payment.paidCents > 0 ? LoanPaymentStatus.PartiallyPaid : LoanPaymentStatus.Scheduled;
                }
                else if (payment.status == LoanPaymentStatus.PartiallyPaid && payment.paidCents <= 0)
                {
                    payment.status = LoanPaymentStatus.Scheduled;
                }
            }

            int unembeddedFeeCents = PaymentScheduleBuilder.GetUnembeddedOriginationFeeCents(sanitizedTerm, schedule);
            if (unembeddedFeeCents > 0)
            {
                LoanPaymentDue feeTarget = schedule.payments.FirstOrDefault(IsPaymentOutstanding);
                if (feeTarget != null)
                {
                    feeTarget.totalDueCents += unembeddedFeeCents;
                }
            }

            RefreshScheduleTotals(schedule);
            return schedule;
        }

        private static void RefreshScheduleTotals(LoanPaymentSchedule schedule)
        {
            if (schedule == null)
            {
                return;
            }

            int totalPrincipal = 0;
            int totalInterest = 0;
            int totalPayment = 0;
            if (schedule.payments != null)
            {
                for (int i = 0; i < schedule.payments.Count; i++)
                {
                    LoanPaymentDue payment = schedule.payments[i];
                    if (payment == null)
                    {
                        continue;
                    }

                    totalPrincipal += Mathf.Max(0, payment.principalCents);
                    totalInterest += Mathf.Max(0, payment.interestCents);
                    totalPayment += Mathf.Max(0, payment.totalDueCents);
                }
            }

            schedule.totalPrincipalCents = totalPrincipal > 0 ? totalPrincipal : Mathf.Max(0, schedule.totalPrincipalCents);
            schedule.totalInterestCents = totalInterest > 0 ? totalInterest : Mathf.Max(0, schedule.totalInterestCents);
            schedule.totalPaymentCents = totalPayment > 0 ? totalPayment : Mathf.Max(0, schedule.totalPaymentCents);
        }

        private static List<string> SanitizeCollateralIds(List<string> collateralIds)
        {
            if (collateralIds == null || collateralIds.Count == 0)
            {
                return new List<string>();
            }

            return collateralIds
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .Select(id => id.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private static List<FinancingReasonCode> SanitizeReasonCodes(List<FinancingReasonCode> reasonCodes)
        {
            if (reasonCodes == null || reasonCodes.Count == 0)
            {
                return new List<FinancingReasonCode>();
            }

            List<FinancingReasonCode> sanitized = new List<FinancingReasonCode>();
            for (int i = 0; i < reasonCodes.Count; i++)
            {
                if (Enum.IsDefined(typeof(FinancingReasonCode), reasonCodes[i]) && !sanitized.Contains(reasonCodes[i]))
                {
                    sanitized.Add(reasonCodes[i]);
                }
            }

            return sanitized;
        }

        private static int GetWeeklyEquivalentDebtPaymentCents(LoanContract loan)
        {
            LoanPaymentDue next = GetNextUnpaidPayment(loan);
            return loan == null || next == null ? 0 : PaymentScheduleBuilder.ToWeeklyEquivalentCents(loan.termStructure, next.totalDueCents);
        }

        private bool HasActiveLenderReapplyCooldown(int currentDayIndex)
        {
            return GetRemainingLenderCooldownDays(currentDayIndex) > 0;
        }

        private int GetRemainingLenderCooldownDays(int currentDayIndex)
        {
            if (application == null || application.reapplyEarliestDayIndex < 0)
            {
                return 0;
            }

            return Mathf.Max(0, application.reapplyEarliestDayIndex - Mathf.Max(0, currentDayIndex));
        }

        // Lender setback memory is intentionally short-lived and local to this bank lane.
        // It gives failed files, workouts, and defaults a believable cooling-off tail without creating a second credit authority.
        private void ApplyLenderSetbackMemory(int currentDayIndex, int cooldownDays, float caution01, string memorySummary)
        {
            application ??= new BankLoanApplicationState();
            int safeCurrentDay = Mathf.Max(0, currentDayIndex);
            int safeCooldownDays = Mathf.Max(0, cooldownDays);
            int newEarliestDay = safeCurrentDay + safeCooldownDays;
            application.reapplyEarliestDayIndex = Mathf.Max(application.reapplyEarliestDayIndex, newEarliestDay);
            application.reapplyEarliestDate = BuildDate(application.reapplyEarliestDayIndex);
            application.lenderCaution01 = Mathf.Max(application.lenderCaution01, Mathf.Clamp01(caution01));
            if (!string.IsNullOrWhiteSpace(memorySummary))
            {
                application.lenderMemorySummary = memorySummary.Trim();
            }
        }

        private void ClearLenderSetbackMemory()
        {
            application ??= new BankLoanApplicationState();
            application.reapplyEarliestDayIndex = -1;
            application.reapplyEarliestDate = default;
            application.lenderCaution01 = 0f;
            application.lenderMemorySummary = string.Empty;
        }

        private void ApplyDeclinedApplicationSetbackMemory(int currentDayIndex, BankLoanUnderwritingResult result)
        {
            if (result == null)
            {
                return;
            }

            GetDeclinedApplicationSetbackBaseline(result.reviewBand, out int cooldownDays, out float caution01);

            if (result.reasonCodes != null && result.reasonCodes.Contains(FinancingReasonCode.LowLenderTrust))
            {
                caution01 = Mathf.Max(caution01, 0.24f);
                cooldownDays = Mathf.Max(cooldownDays, 10);
            }

            if (result.reasonCodes != null && result.reasonCodes.Contains(FinancingReasonCode.FailedClosingHistory))
            {
                caution01 = Mathf.Max(caution01, 0.32f);
                cooldownDays = Mathf.Max(cooldownDays, 14);
            }

            ApplyLenderSetbackMemory(currentDayIndex, cooldownDays, caution01, "The bank wants some distance after the declined file before it reviews a fresh working-capital request.");
        }

        private static void GetDeclinedApplicationSetbackBaseline(BankLoanReviewBand reviewBand, out int cooldownDays, out float caution01)
        {
            switch (reviewBand)
            {
                case BankLoanReviewBand.Significant:
                    cooldownDays = 12;
                    caution01 = 0.3f;
                    break;
                case BankLoanReviewBand.Watchlist:
                    cooldownDays = 8;
                    caution01 = 0.2f;
                    break;
                default:
                    cooldownDays = 5;
                    caution01 = 0.12f;
                    break;
            }
        }

        private string BuildReapplyCooldownBlockedMessage(int currentDayIndex)
        {
            string memorySummary = application != null ? application.lenderMemorySummary : string.Empty;
            string earliestDate = application != null ? FormatDate(application.reapplyEarliestDate) : "a later date";
            string baseMessage = $"The bank is not ready to review a fresh working-capital file yet. Reapply after {earliestDate}.";
            return AppendCooldownContext(baseMessage, string.IsNullOrWhiteSpace(memorySummary) ? string.Empty : $" {memorySummary}");
        }

        private string BuildReapplyCooldownStatus(int currentDayIndex)
        {
            string baseMessage = BuildReapplyCooldownBlockedMessage(currentDayIndex);
            int daysRemaining = Mathf.Max(1, GetRemainingLenderCooldownDays(currentDayIndex));
            return $"{baseMessage}\nCooling-off window: {daysRemaining} day(s) remaining.";
        }

        private string BuildReapplyWindowSentence(int currentDayIndex)
        {
            if (!HasActiveLenderReapplyCooldown(currentDayIndex) || application == null)
            {
                return string.Empty;
            }

            return $" Reapply after {FormatDate(application.reapplyEarliestDate)}.";
        }

        private static string AppendCooldownContext(string message, string extraSentence)
        {
            if (string.IsNullOrWhiteSpace(extraSentence))
            {
                return message ?? string.Empty;
            }

            string trimmed = (message ?? string.Empty).TrimEnd();
            if (!trimmed.EndsWith("."))
            {
                trimmed += ".";
            }

            return $"{trimmed}{extraSentence}";
        }

        private string BuildDebtBlockedMessage(string actionLabel)
        {
            string action = string.IsNullOrWhiteSpace(actionLabel) ? "new borrowing" : actionLabel;
            if (activeLoan != null && activeLoan.status == LoanStatus.Defaulted)
            {
                return $"Defaulted bank debt must be resolved before {action}.";
            }

            return $"Existing bank debt must be settled before {action}.";
        }

        private int GetCurrentDayIndex()
        {
            object currentDate = GetTimeManagerCurrentDateObject();
            return currentDate != null ? GetAbsoluteDayIndex(currentDate) : 0;
        }

        private SimulationDate GetCurrentDate()
        {
            object currentDate = GetTimeManagerCurrentDateObject();
            return currentDate is SimulationDate simulationDate ? simulationDate : BuildDate(0);
        }

        private int GetDaysPerMonth()
        {
            object settings = GetTimeSettingsObject();
            if (settings == null)
            {
                return 28;
            }

            int daysPerMonth = GetIntMemberValue(settings, "DaysPerMonth", "daysPerMonth");
            return daysPerMonth > 0 ? daysPerMonth : 28;
        }

        private SimulationDate BuildDate(int absoluteDayIndex)
        {
            object settings = GetTimeSettingsObject();
            if (settings != null)
            {
                MethodInfo buildDateMethod = settings.GetType().GetMethod("BuildDateFromAbsoluteDay", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (buildDateMethod != null)
                {
                    object builtDate = buildDateMethod.Invoke(settings, new object[] { absoluteDayIndex });
                    if (builtDate is SimulationDate simulationDate)
                    {
                        return simulationDate;
                    }
                }
            }

            int clampedDay = Mathf.Max(0, absoluteDayIndex);
            int dayOfWeekIndex = clampedDay % 7;
            int monthIndex = clampedDay / 28;
            int dayOfMonth = clampedDay % 28 + 1;
            int week = clampedDay / 7 + 1;
            int weekOfMonth = (dayOfMonth - 1) / 7 + 1;
            int month = monthIndex % 12 + 1;
            int year = monthIndex / 12 + 1;
            return new SimulationDate(clampedDay, dayOfWeekIndex, dayOfMonth, week, weekOfMonth, month, year);
        }

        private object GetTimeSettingsObject()
        {
            return GetMemberValue(timeManager, "Settings", "settings");
        }

        private object GetTimeManagerCurrentDateObject()
        {
            return GetMemberValue(timeManager, "CurrentDate", "currentDate");
        }

        private int GetAbsoluteDayIndexFromContext(object context)
        {
            object currentDate = GetMemberValue(context, "CurrentDate", "currentDate");
            return currentDate != null ? GetAbsoluteDayIndex(currentDate) : GetCurrentDayIndex();
        }

        private static int GetAbsoluteDayIndex(object dateLike)
        {
            return GetIntMemberValue(dateLike, "AbsoluteDayIndex", "absoluteDayIndex", "AbsoluteDay", "absoluteDay");
        }

        private int GetOwnerCashCents()
        {
            return Mathf.Max(0, GetIntMemberValue(playerPortfolio, "OwnerCashCents", "ownerCashCents"));
        }

        private int GetPortfolioLastWeeklyDistributionCents()
        {
            return Mathf.Max(0, GetIntMemberValue(playerPortfolio, "LastWeeklyDistributionCents", "lastWeeklyDistributionCents"));
        }

        private int GetStoreEstimatedWeeklyNetCashFlowCents()
        {
            return Mathf.Max(0, GetIntMemberValue(storeRuntime, "EstimatedWeeklyNetCashFlowCents", "estimatedWeeklyNetCashFlowCents"));
        }

        private int GetStoreBuildingId()
        {
            int buildingId = GetIntMemberValue(storeRuntime, "StoreBuildingId", "storeBuildingId");
            return buildingId > 0 ? buildingId : -1;
        }

        private string GetCurrentBusinessInstanceId()
        {
            object currentBusiness = GetMemberValue(storeRuntime, "CurrentBusiness", "currentBusiness");
            object instanceId = GetMemberValue(currentBusiness, "InstanceId", "instanceId");
            return instanceId?.ToString() ?? string.Empty;
        }

        private IEnumerable GetOwnedLandAppreciationsEnumerable()
        {
            object appreciations = GetMemberValue(acquisitionMarket, "OwnedLandAppreciations", "ownedLandAppreciations");
            return appreciations as IEnumerable;
        }

        private bool TryRecoverDefaultedLoanCollateral(int excludedBuildingId, out string recoveredLabel, out int recoveredValueCents, out string recoveryMessage)
        {
            recoveredLabel = string.Empty;
            recoveredValueCents = 0;
            recoveryMessage = string.Empty;
            if (acquisitionMarket == null || activeLoan == null)
            {
                return false;
            }

            MethodInfo method = acquisitionMarket.GetType().GetMethod(
                "TryRecoverDefaultedLoanCollateral",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (method == null)
            {
                return false;
            }

            object[] args = new object[] { activeLoan, excludedBuildingId, null, 0, null };
            object invocationResult = method.Invoke(acquisitionMarket, args);
            bool success = invocationResult is bool boolResult && boolResult;
            if (!success)
            {
                return false;
            }

            recoveredLabel = args[2]?.ToString() ?? string.Empty;
            recoveredValueCents = args[3] is int value ? value : 0;
            recoveryMessage = args[4]?.ToString() ?? string.Empty;
            return true;
        }

        private void AddOwnerCashToPortfolio(int amountCents, string reason)
        {
            if (playerPortfolio == null || amountCents <= 0)
            {
                return;
            }

            MethodInfo method = playerPortfolio.GetType().GetMethod(
                "AddOwnerCash",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                null,
                new[] { typeof(int), typeof(string) },
                null);
            method?.Invoke(playerPortfolio, new object[] { amountCents, reason ?? string.Empty });
        }

        private bool TrySpendOwnerCashFromPortfolio(int amountCents, string reason)
        {
            if (playerPortfolio == null || amountCents <= 0)
            {
                return false;
            }

            Type portfolioType = playerPortfolio.GetType();
            MethodInfo[] methods = portfolioType.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            for (int i = 0; i < methods.Length; i++)
            {
                MethodInfo method = methods[i];
                if (method == null || method.Name != "TrySpendOwnerCash")
                {
                    continue;
                }

                ParameterInfo[] parameters = method.GetParameters();
                if (parameters.Length == 2
                    && parameters[0].ParameterType == typeof(int)
                    && parameters[1].ParameterType == typeof(string))
                {
                    object result = method.Invoke(playerPortfolio, new object[] { amountCents, reason ?? string.Empty });
                    return result is bool boolResult && boolResult;
                }

                if (parameters.Length == 3
                    && parameters[0].ParameterType == typeof(int)
                    && parameters[1].ParameterType == typeof(string)
                    && parameters[2].IsOut)
                {
                    Type outElementType = parameters[2].ParameterType.GetElementType();
                    object defaultOutValue = outElementType != null && outElementType.IsValueType
                        ? Activator.CreateInstance(outElementType)
                        : null;
                    object[] args = new object[] { amountCents, reason ?? string.Empty, defaultOutValue };
                    object result = method.Invoke(playerPortfolio, args);
                    return result is bool boolResult && boolResult;
                }
            }

            return false;
        }

        private static object GetMemberValue(object target, params string[] memberNames)
        {
            if (target == null || memberNames == null)
            {
                return null;
            }

            Type type = target.GetType();
            for (int i = 0; i < memberNames.Length; i++)
            {
                string memberName = memberNames[i];
                if (string.IsNullOrWhiteSpace(memberName))
                {
                    continue;
                }

                PropertyInfo property = type.GetProperty(memberName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (property != null && property.CanRead)
                {
                    return property.GetValue(target, null);
                }

                FieldInfo field = type.GetField(memberName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (field != null)
                {
                    return field.GetValue(target);
                }
            }

            return null;
        }

        private static int GetIntMemberValue(object target, params string[] memberNames)
        {
            object value = GetMemberValue(target, memberNames);
            return value is int intValue ? intValue : 0;
        }

        private static string BuildLoanId(string prefix, int absoluteDayIndex)
        {
            return $"{prefix}_{Mathf.Max(0, absoluteDayIndex):000000}";
        }

        private static string FormatMoney(int cents)
        {
            return $"${Mathf.Max(0, cents) / 100f:0.00}";
        }

        private static string FormatDate(SimulationDate date)
        {
            return date.ToString();
        }
    }
}
