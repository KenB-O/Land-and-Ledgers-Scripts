using System;
using System.Collections.Generic;
using LandLedgers.Economy.Financing;
using LandLedgers.Population;
using LandLedgers.Primitives;

namespace LandLedgers.World.Property
{
    /// <summary>
    /// Pass III Worker B: cash target for a rent/board payee that is a
    /// business (or government account). Production implements this against
    /// the business cash authority (BusinessRuntimeState.AddCashCents —
    /// all business-cash mutations live there); tests fake it. Mirrors the
    /// boarding-house IRentCashSink pattern: the payment loop never touches
    /// business cash directly.
    /// </summary>
    public interface ITenancyPayeeCashSink
    {
        string PayeeHolderId { get; }
        string DisplayName { get; }
        void RecordCashInflow(int cents, string label, int dayIndex);
    }

    /// <summary>
    /// Pass III Worker B: resolves where a tenancy's rent goes. The payee
    /// named on TenancyPaymentTerms is a person/business/government id;
    /// game wiring (Codex) implements this directory — e.g. person id →
    /// active household via HouseholdMembershipRegistry, business id →
    /// the business cash sink. The payment loop refuses to debit the tenant
    /// when the payee cannot be resolved: money must never leave a household
    /// with nowhere legitimate to land.
    /// </summary>
    public interface ITenancyPayeeDirectory
    {
        /// <summary>Household id of a person payee, or -1 when not a household payee.</summary>
        int FindPayeeHouseholdId(string payeeHolderId);
        /// <summary>Cash sink of a business/government payee, or null.</summary>
        ITenancyPayeeCashSink FindPayeeBusinessSink(string payeeHolderId);
    }

    /// <summary>
    /// Pass III Worker B: read model for one settled tenancy period —
    /// compact enough for later Unity UI, source-backed (agreement id,
    /// period, day, cents, obligation refs).
    /// </summary>
    [Serializable]
    public sealed class TenancySettlementResult
    {
        public string AgreementId = string.Empty;
        public int PeriodIndex;
        public int DueDay;
        public int TenantHouseholdId = -1;
        public string PayeeHolderId = string.Empty;
        public long AmountDueCents;
        public long PaidCents;
        public RentObligationStatus Status = RentObligationStatus.Current;
        public string ArrearsObligationId = string.Empty;
        /// <summary>
        /// settled | settled-partial | settled-overdue | already-settled |
        /// skipped-no-tenant | skipped-no-payee | skipped-tenancy-ended
        /// </summary>
        public string Outcome = string.Empty;

        public TenancySettlementResult() { }
    }

    /// <summary>
    /// Pass III Worker B: the general rent/board payment loop. Turns
    /// TenancyPaymentTerms attached to PropertyAgreement records into due
    /// events on a daily drive:
    ///
    ///   tenant HouseholdLedger.RecordOutflow → payee credit
    ///   (payee household ledger inflow, or business cash sink)
    ///   → unpaid remainder becomes a REAL liability through
    ///   FinancialObligationAuthority (Kind=Payable), never informal debt.
    ///
    /// Canon grounding:
    /// - CAN-ORIG Rev XXXI Part VI §§6.1–6.8: housing/tenancy obligations are
    ///   real, payable from actual cash; no automatic eviction on missed rent
    ///   (legal enforcement is its own legitimate process).
    /// - CAN-ORIG Rev XXXI Part IV §4.6: employment, housing, tenancy and
    ///   ownership are distinct relationships — a tenancy never grants
    ///   ownership or residence beyond its own terms.
    /// - Preserved Rev XXIII §1.1: one authority per consequential fact —
    ///   there is NO second rent-cash authority here; HouseholdLedger is the
    ///   single cash writer and FinancialObligationAuthority the single debt
    ///   authority.
    /// - Canon §12.7H (per existing RentCollectionService): rent is an actual
    ///   household obligation paid from actual cash or negotiated arrears.
    ///
    /// Idempotency: a period is keyed (AgreementId, PeriodIndex) and never
    /// settled twice, including across save/load. Conservation: tenant out ==
    /// payee in, cent for cent; the payee is resolved BEFORE any debit.
    /// </summary>
    public sealed class TenancyPaymentService
    {
        private readonly Dictionary<string, RentObligationState> obligations =
            new Dictionary<string, RentObligationState>(StringComparer.Ordinal);
        private readonly List<TenancySettlementResult> recentResults = new List<TenancySettlementResult>();
        private readonly List<string> diagnostics = new List<string>();

        public IReadOnlyList<string> Diagnostics => diagnostics;
        public IReadOnlyList<TenancySettlementResult> RecentResults => recentResults;

        private static string ObligationKey(string agreementId, int periodIndex)
        {
            return agreementId + ":" + periodIndex;
        }

        /// <summary>
        /// Daily driver entry. Game wiring (Codex) calls this once per
        /// simulated day with the real authorities. Settles every unsettled
        /// period whose due day has arrived, oldest first (catch-up safe).
        /// Returns one result per agreement-period considered.
        /// </summary>
        public List<TenancySettlementResult> SettleDueRents(
            HousingAuthority housing,
            int dayIndex,
            HouseholdLedgerRegistry ledgers,
            ITenancyPayeeDirectory payees,
            FinancialObligationAuthority obligationAuthority,
            EntityIdRegistry ids,
            List<string> diag)
        {
            diag = diag ?? diagnostics;
            var results = new List<TenancySettlementResult>();
            if (housing == null || ledgers == null || payees == null || obligationAuthority == null || ids == null)
            {
                diag.Add("TenancyPaymentService.SettleDueRents: authorities missing — nothing settled, no payment faked.");
                return results;
            }

            foreach (PropertyAgreement agreement in housing.AgreementsWithPaymentTerms())
            {
                TenancyPaymentTerms terms = agreement.PaymentTerms;
                // Walk periods in order; stop at the first period not yet due
                // or past the tenancy's end.
                for (int periodIndex = 0; ; periodIndex++)
                {
                    int dueDay = terms.StartDay + periodIndex * terms.PeriodDays + (terms.DueDayOfPeriod - 1);
                    if (agreement.EndDayIndex >= 0 && dueDay > agreement.EndDayIndex)
                        break; // tenancy ended — later periods never come due
                    if (dueDay > dayIndex)
                        break; // not due yet
                    string key = ObligationKey(agreement.AgreementId, periodIndex);
                    if (obligations.ContainsKey(key))
                    {
                        results.Add(new TenancySettlementResult
                        {
                            AgreementId = agreement.AgreementId,
                            PeriodIndex = periodIndex,
                            DueDay = dueDay,
                            Outcome = "already-settled",
                        });
                        continue;
                    }
                    results.Add(SettleAgreementPeriod(
                        housing, agreement, terms, periodIndex, dueDay, dayIndex,
                        ledgers, payees, obligationAuthority, ids, diag));
                }
            }
            return results;
        }

        private TenancySettlementResult SettleAgreementPeriod(
            HousingAuthority housing,
            PropertyAgreement agreement,
            TenancyPaymentTerms terms,
            int periodIndex,
            int dueDay,
            int dayIndex,
            HouseholdLedgerRegistry ledgers,
            ITenancyPayeeDirectory payees,
            FinancialObligationAuthority obligationAuthority,
            EntityIdRegistry ids,
            List<string> diag)
        {
            var result = new TenancySettlementResult
            {
                AgreementId = agreement.AgreementId,
                PeriodIndex = periodIndex,
                DueDay = dueDay,
                PayeeHolderId = terms.PayeeHolderId ?? string.Empty,
                AmountDueCents = terms.AmountCents,
            };

            // 1. Resolve the tenant household: explicit id wins; otherwise the
            // single current occupant household of the agreement's space.
            // Zero or several households = ambiguous: charge nothing, say so.
            int tenantHouseholdId = ResolveTenantHousehold(housing, agreement, terms, diag);
            if (tenantHouseholdId < 0)
            {
                result.Outcome = "skipped-no-tenant";
                return result;
            }
            result.TenantHouseholdId = tenantHouseholdId;

            // 2. Resolve the payee BEFORE any money moves. An unresolvable
            // payee means no debit — conservation first.
            int payeeHouseholdId = payees.FindPayeeHouseholdId(terms.PayeeHolderId);
            ITenancyPayeeCashSink payeeSink = payeeHouseholdId < 0
                ? payees.FindPayeeBusinessSink(terms.PayeeHolderId)
                : null;
            string payeeDisplay = payeeHouseholdId >= 0
                ? $"H{payeeHouseholdId}"
                : payeeSink != null ? payeeSink.DisplayName : terms.PayeeHolderId;
            if (payeeHouseholdId < 0 && payeeSink == null)
            {
                diag.Add($"TenancyPaymentService: payee '{terms.PayeeHolderId}' for '{agreement.AgreementId}' period {periodIndex} " +
                         "could not be resolved — no debit made, no payment faked.");
                result.Outcome = "skipped-no-payee";
                return result;
            }

            // 3. Debit the tenant for what the household actually holds
            // (partial payment tolerance); the remainder becomes real arrears.
            HouseholdLedger tenantLedger = ledgers.GetOrCreate(tenantHouseholdId);
            long balanceCents = tenantLedger.GetBalanceCents();
            long paidCents = Math.Min(balanceCents, terms.AmountCents);
            string purpose = $"rent/board: {agreement.Kind} {agreement.AgreementId} period {periodIndex} (due day {dueDay})";
            if (paidCents > 0)
            {
                string refusal = tenantLedger.RecordOutflow(dayIndex, (int)paidCents, purpose, payeeDisplay);
                if (refusal != null)
                {
                    // Defensive: paidCents was capped at the balance, so this
                    // should not happen. Never credit what was not debited.
                    diag.Add($"TenancyPaymentService: UNEXPECTED tenant debit refusal for H{tenantHouseholdId} " +
                             $"({paidCents}c): {refusal} — nothing credited, full amount booked as arrears.");
                    paidCents = 0;
                }
            }

            // 4. Credit the payee through the single cash path for its kind.
            if (paidCents > 0)
            {
                if (payeeHouseholdId >= 0)
                {
                    HouseholdLedger payeeLedger = ledgers.GetOrCreate(payeeHouseholdId);
                    HouseholdIncomeSource source = string.Equals(agreement.Kind, "boarding", StringComparison.OrdinalIgnoreCase)
                        ? HouseholdIncomeSource.BoardingIncome
                        : HouseholdIncomeSource.OtherDocumented;
                    string inflowRefusal = payeeLedger.RecordInflow(
                        dayIndex, (int)paidCents, source, agreement.AgreementId,
                        $"rent/board received — {agreement.Kind} {agreement.AgreementId} period {periodIndex} from H{tenantHouseholdId}",
                        $"H{tenantHouseholdId}");
                    if (inflowRefusal != null)
                        diag.Add($"TenancyPaymentService: UNEXPECTED payee credit refusal for H{payeeHouseholdId}: {inflowRefusal}.");
                }
                else
                {
                    payeeSink.RecordCashInflow((int)paidCents,
                        $"rent/board from H{tenantHouseholdId} ({agreement.Kind} {agreement.AgreementId} period {periodIndex})",
                        dayIndex);
                }
            }

            // 5. Unpaid remainder becomes a real liability — never informal debt.
            long remainderCents = terms.AmountCents - paidCents;
            string arrearsObligationId = string.Empty;
            if (remainderCents > 0)
            {
                FinancialObligation obligation = obligationAuthority.Create(
                    ids, FinancialObligationKind.Payable,
                    debtor: $"household:{tenantHouseholdId}",
                    creditor: terms.PayeeHolderId,
                    principalCents: (int)remainderCents,
                    dayIndex: dueDay,
                    terms: $"rent/board arrears — {agreement.Kind} agreement {agreement.AgreementId}, period {periodIndex} due day {dueDay}; " +
                           "negotiated arrears, Canon XXXI Part VI / Canon 12.7H; no automatic eviction (enforcement is its own process)",
                    purpose: $"rent/board arrears: {agreement.AgreementId} period {periodIndex}",
                    agreementId: agreement.AgreementId);
                if (obligation != null)
                {
                    arrearsObligationId = obligation.ObligationId;
                }
                else
                {
                    diag.Add($"TenancyPaymentService: FAILED to book arrears for H{tenantHouseholdId} ({remainderCents}c) — " +
                             "obligation authority refused. The debt is logged here, not lost.");
                }
            }

            RentObligationStatus status = remainderCents <= 0
                ? RentObligationStatus.Current
                : (paidCents > 0 ? RentObligationStatus.Partial : RentObligationStatus.Overdue);

            var state = new RentObligationState
            {
                AgreementId = agreement.AgreementId,
                Terms = terms,
                DueDay = dueDay,
                AmountDueCents = terms.AmountCents,
                PaidCents = paidCents,
                Status = status,
                PeriodIndex = periodIndex,
                ArrearsObligationId = arrearsObligationId,
            };
            obligations[ObligationKey(agreement.AgreementId, periodIndex)] = state;

            result.PaidCents = paidCents;
            result.Status = status;
            result.ArrearsObligationId = arrearsObligationId;
            result.Outcome = remainderCents <= 0 ? "settled"
                : (paidCents > 0 ? "settled-partial" : "settled-overdue");
            recentResults.Add(result);
            diag.Add($"TenancyPaymentService: '{agreement.AgreementId}' period {periodIndex} (due day {dueDay}) — " +
                     $"H{tenantHouseholdId} paid {paidCents}c of {terms.AmountCents}c to {payeeDisplay} " +
                     (remainderCents > 0 ? $"(arrears {remainderCents}c, obligation '{arrearsObligationId}') " : "(paid in full) ") +
                     $"— tenant out == payee in, cent for cent.");
            return result;
        }

        /// <summary>
        /// Tenant resolution. The explicit TenantHouseholdId on the terms wins
        /// (the agreement names its parties). Otherwise the single current
        /// occupant household of the agreement's space — or, for a
        /// building/parcel-level agreement, the single distinct household
        /// occupying its spaces. Zero or several households is ambiguous:
        /// returns -1 and the period is skipped, never invented.
        /// </summary>
        private int ResolveTenantHousehold(
            HousingAuthority housing, PropertyAgreement agreement, TenancyPaymentTerms terms, List<string> diag)
        {
            if (terms.TenantHouseholdId >= 0)
                return terms.TenantHouseholdId;

            string targetId = agreement.SpaceOrParcelId ?? string.Empty;
            var householdIds = new HashSet<int>();
            AccommodationSpace space = housing.FindSpace(targetId);
            if (space != null)
            {
                foreach (ResidentialOccupancy occ in housing.CurrentOccupants(space.SpaceId))
                    householdIds.Add(occ.HouseholdId);
            }
            else
            {
                Building building = housing.FindBuilding(targetId);
                if (building == null)
                {
                    diag.Add($"TenancyPaymentService: agreement '{agreement.AgreementId}' targets unknown space/building '{targetId}' " +
                             "and names no tenant household — skipped, no charge invented.");
                    return -1;
                }
                foreach (AccommodationSpace s in housing.SpacesForBuilding(building.BuildingId))
                {
                    foreach (ResidentialOccupancy occ in housing.CurrentOccupants(s.SpaceId))
                        householdIds.Add(occ.HouseholdId);
                }
            }

            if (householdIds.Count == 1)
            {
                foreach (int id in householdIds) return id;
            }
            diag.Add($"TenancyPaymentService: agreement '{agreement.AgreementId}' resolves to {householdIds.Count} occupant households " +
                     "and names no tenant — ambiguous, skipped, no charge invented.");
            return -1;
        }

        /// <summary>Read side: the recorded obligation state for one period, if settled.</summary>
        public RentObligationState FindObligation(string agreementId, int periodIndex)
        {
            if (string.IsNullOrWhiteSpace(agreementId)) return null;
            obligations.TryGetValue(ObligationKey(agreementId, periodIndex), out RentObligationState state);
            return state;
        }

        #region Save / Load
        [Serializable]
        public sealed class TenancyPaymentSaveDto
        {
            public List<RentObligationState> Obligations = new List<RentObligationState>();
        }

        public TenancyPaymentSaveDto CaptureSaveDto()
        {
            return new TenancyPaymentSaveDto
            {
                Obligations = new List<RentObligationState>(obligations.Values),
            };
        }

        /// <summary>Clear-then-load: restored state replaces, never merges.</summary>
        public void LoadFromSaveDto(TenancyPaymentSaveDto dto)
        {
            obligations.Clear();
            recentResults.Clear();
            if (dto == null || dto.Obligations == null) return;
            foreach (RentObligationState state in dto.Obligations)
            {
                if (state == null || string.IsNullOrWhiteSpace(state.AgreementId) || state.PeriodIndex < 0) continue;
                obligations[ObligationKey(state.AgreementId, state.PeriodIndex)] = state;
            }
        }
        #endregion
    }
}
