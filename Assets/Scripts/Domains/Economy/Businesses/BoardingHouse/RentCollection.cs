using System;
using System.Collections.Generic;
using LandLedgers.Economy.Financing;
using LandLedgers.Population;
using LandLedgers.Primitives;

namespace LandLedgers.Economy.Businesses.BoardingHouse
{
    /// <summary>
    /// Phase D (Real People): the business-cash side of rent settlement. The
    /// runtime adapter implements this against
    /// <c>BusinessRuntimeState.AddCashCents</c> (all business-cash mutations
    /// live in that class); tests fake it. Keeps the boarding package
    /// decoupled from the business runtime for harness runs.
    /// </summary>
    public interface IRentCashSink
    {
        string BusinessInstanceId { get; }
        string BusinessName { get; }
        int CashCents { get; }
        void RecordCashInflow(int cents, string label, int dayIndex);
    }

    /// <summary>Phase D: arrears lifecycle. Append-only.</summary>
    public enum RentArrearsStatus
    {
        Open = 0,
        Repaid = 1,
        Evicted = 2, // tenant removed while still owing (the obligation survives)
    }

    /// <summary>
    /// Phase D: one boarder's rent arrears — a REAL obligation, not informal
    /// debt (Canon §12.7H: "Rent is an actual household obligation paid from
    /// actual cash or negotiated arrears"). The obligation lives in
    /// <see cref="FinancialObligationAuthority"/>; this record is the
    /// boarding-house view of it.
    /// </summary>
    [Serializable]
    public sealed class RentArrearsRecord
    {
        public string ArrearsId = string.Empty;
        public int PersonId = -1;
        public int HouseholdId = -1;
        public string BusinessInstanceId = string.Empty;
        public int DueDayIndex;
        public int OriginalCentsOwed;
        public int CentsPaid;
        public string ObligationId = string.Empty;
        public int LastEvaluatedDayIndex;
        public RentArrearsStatus Status = RentArrearsStatus.Open;

        public int OutstandingCents => Math.Max(0, OriginalCentsOwed - CentsPaid);

        public RentArrearsRecord() { }
    }

    /// <summary>Phase D: eviction-notice lifecycle. Append-only.</summary>
    public enum EvictionNoticeStatus
    {
        Issued = 0,
        Withdrawn = 1,
        Executed = 2,
    }

    /// <summary>
    /// Phase D: an eviction notice — eviction is a PROCESS with notice, never
    /// an instant removal. Canon §12.7G: the exact eviction/removal process
    /// remains a Dakota/South Dakota legal research hold, so every notice
    /// carries <see cref="LegalProcessProvisional"/> — no harsh mechanics
    /// are invented here. Minimum notice period is TUNING (canon sets none).
    /// </summary>
    [Serializable]
    public sealed class EvictionNotice
    {
        public string NoticeId = string.Empty;
        public int PersonId = -1;
        public int HouseholdId = -1;
        public string BusinessInstanceId = string.Empty;
        public int IssuedDayIndex;
        public int EffectiveDayIndex;
        public string Reason = string.Empty;
        public EvictionNoticeStatus Status = EvictionNoticeStatus.Issued;
        public bool LegalProcessProvisional = true;

        public EvictionNotice() { }
    }

    /// <summary>
    /// Phase D: rent collection — wires
    /// <see cref="BoardingHouseBoarderRegister.RentDueWeekly"/> and
    /// <see cref="BoardingHouseBoarderRegister.RentDueMonthly"/> into the
    /// real authorities: tenant <see cref="HouseholdLedger.RecordOutflow"/>
    /// → boarding-house cash inflow. Insufficient funds become real
    /// obligations (negotiated arrears, Canon §12.7H), never informal debt.
    /// Ledger conservation: tenant out == business in, cent for cent.
    /// </summary>
    public sealed class RentCollectionService
    {
        /// <summary>TUNING: minimum eviction notice period in days (canon sets none).</summary>
        public int MinimumEvictionNoticeDays { get; set; } = 14;

        private readonly Dictionary<string, RentArrearsRecord> arrears =
            new Dictionary<string, RentArrearsRecord>(StringComparer.Ordinal);
        private readonly List<EvictionNotice> notices = new List<EvictionNotice>();
        private readonly List<string> diagnostics = new List<string>();
        private int sequence;

        public IReadOnlyList<string> Diagnostics => diagnostics;
        public IReadOnlyList<EvictionNotice> Notices => notices;

        public List<RentArrearsRecord> OpenArrears()
        {
            var result = new List<RentArrearsRecord>();
            foreach (RentArrearsRecord record in arrears.Values)
                if (record != null && record.Status == RentArrearsStatus.Open)
                    result.Add(record);
            return result;
        }

        public RentArrearsRecord FindArrears(string arrearsId)
        {
            if (string.IsNullOrWhiteSpace(arrearsId)) return null;
            arrears.TryGetValue(arrearsId, out RentArrearsRecord record);
            return record;
        }

        /// <summary>
        /// Phase D: settles one settlement run's dues. Full payment moves
        /// ledger → business cash. Partial payment (Canon §12.7G tolerance)
        /// moves what the household holds and books the remainder as a real
        /// arrears obligation. Nothing is invented: a household that cannot
        /// pay owes — it does not vanish.
        /// </summary>
        public void SettleDues(
            List<BoarderRentDue> dues,
            IRentCashSink business,
            HouseholdLedgerRegistry ledgers,
            HouseholdMembershipRegistry memberships,
            FinancialObligationAuthority obligations,
            EntityIdRegistry ids,
            int dayIndex,
            List<string> diag)
        {
            diag = diag ?? diagnostics;
            if (dues == null || business == null || ledgers == null || memberships == null
                || obligations == null || ids == null)
            {
                diag.Add("RentCollectionService.SettleDues: authorities missing — nothing settled.");
                return;
            }

            foreach (BoarderRentDue due in dues)
            {
                if (due == null || due.CentsDue <= 0) continue;
                int householdId = memberships.GetActiveHouseholdId(due.PersonId);
                if (householdId < 0)
                {
                    diag.Add($"RentCollectionService: P{due.PersonId} has no active household — rent due '{due.Label}' cannot be settled, no payment faked.");
                    continue;
                }

                HouseholdLedger ledger = ledgers.GetOrCreate(householdId);
                int balanceBefore = ledger.GetBalanceCents();
                string purpose = $"rent: {due.Label}";
                string refusal = ledger.RecordOutflow(dayIndex, due.CentsDue, purpose, business.BusinessName);
                if (refusal == null)
                {
                    business.RecordCashInflow(due.CentsDue, $"rent from H{householdId} ({due.Label})", dayIndex);
                    diag.Add($"RentCollectionService: H{householdId} paid {due.CentsDue}c rent to '{business.BusinessName}' " +
                        $"(ledger {balanceBefore}c → {ledger.GetBalanceCents()}c; business +{due.CentsDue}c — conserved).");
                    continue;
                }

                // Insufficient funds: pay what the household holds, book the rest as real arrears.
                int paidNow = 0;
                if (balanceBefore > 0)
                {
                    paidNow = balanceBefore;
                    string partialRefusal = ledger.RecordOutflow(dayIndex, paidNow, purpose + " (partial)", business.BusinessName);
                    if (partialRefusal == null)
                    {
                        business.RecordCashInflow(paidNow, $"partial rent from H{householdId} ({due.Label})", dayIndex);
                        diag.Add($"RentCollectionService: H{householdId} paid {paidNow}c of {due.CentsDue}c rent (partial — Canon 12.7G tolerance).");
                    }
                    else
                    {
                        paidNow = 0;
                    }
                }

                int remainder = due.CentsDue - paidNow;
                if (remainder > 0)
                    BookArrears(due, householdId, business, paidNow, remainder, obligations, ids, dayIndex, diag);
            }
        }

        private void BookArrears(
            BoarderRentDue due, int householdId, IRentCashSink business,
            int paidNow, int remainder,
            FinancialObligationAuthority obligations, EntityIdRegistry ids,
            int dayIndex, List<string> diag)
        {
            FinancialObligation obligation = obligations.Create(
                ids, FinancialObligationKind.Payable,
                debtor: $"household:{householdId}",
                creditor: $"business:{business.BusinessInstanceId}",
                principalCents: remainder,
                dayIndex: dayIndex,
                terms: "boarding rent arrears — negotiated arrears, Canon 12.7H; follows the tenancy/legal process, not merchant-credit rules",
                purpose: due.Label,
                agreementId: due.Label);
            if (obligation == null)
            {
                diag.Add($"RentCollectionService: FAILED to book arrears for H{householdId} ({remainder}c) — obligation authority refused. The debt is logged here, not lost.");
                return;
            }

            var record = new RentArrearsRecord
            {
                ArrearsId = $"arr-{sequence++}",
                PersonId = due.PersonId,
                HouseholdId = householdId,
                BusinessInstanceId = business.BusinessInstanceId,
                DueDayIndex = dayIndex,
                OriginalCentsOwed = due.CentsDue,
                CentsPaid = paidNow,
                ObligationId = obligation.ObligationId,
                LastEvaluatedDayIndex = dayIndex,
                Status = RentArrearsStatus.Open,
            };
            arrears[record.ArrearsId] = record;
            diag.Add($"RentCollectionService: H{householdId} owes {remainder}c rent arrears " +
                $"(record '{record.ArrearsId}', real obligation '{obligation.ObligationId}' — not informal debt).");
        }

        /// <summary>
        /// Phase D: a household pays down arrears from real cash. Cash moves
        /// ledger → business; the obligation is paid down through the shared
        /// authority (one payment writer).
        /// </summary>
        public string PayArrears(
            string arrearsId,
            int cents,
            IRentCashSink business,
            HouseholdLedgerRegistry ledgers,
            FinancialObligationAuthority obligations,
            int dayIndex,
            List<string> diag)
        {
            diag = diag ?? diagnostics;
            RentArrearsRecord record = FindArrears(arrearsId);
            if (record == null)
                return $"RentCollectionService.PayArrears: unknown arrears '{arrearsId}'.";
            if (record.Status != RentArrearsStatus.Open)
                return $"RentCollectionService.PayArrears: arrears '{arrearsId}' is {record.Status}.";
            if (cents <= 0)
                return "RentCollectionService.PayArrears: payment must be positive.";

            int outstanding = record.OutstandingCents;
            int pay = Math.Min(cents, outstanding);
            HouseholdLedger ledger = ledgers.GetOrCreate(record.HouseholdId);
            string refusal = ledger.RecordOutflow(dayIndex, pay,
                $"rent arrears repayment '{arrearsId}'", business.BusinessName);
            if (refusal != null)
            {
                diag.Add($"RentCollectionService: arrears payment refused — {refusal}");
                return $"RentCollectionService.PayArrears refused: {refusal}";
            }
            business.RecordCashInflow(pay, $"rent arrears repayment from H{record.HouseholdId}", dayIndex);
            obligations.ApplyPayment(record.ObligationId, pay, dayIndex,
                payer: $"household:{record.HouseholdId}", payee: $"business:{record.BusinessInstanceId}",
                sourceEconomicEventId: arrearsId);
            record.CentsPaid += pay;
            record.LastEvaluatedDayIndex = dayIndex;
            if (record.OutstandingCents <= 0)
            {
                record.Status = RentArrearsStatus.Repaid;
                diag.Add($"RentCollectionService: arrears '{arrearsId}' REPAID in full by H{record.HouseholdId}.");
            }
            else
            {
                diag.Add($"RentCollectionService: arrears '{arrearsId}' paid down {pay}c — {record.OutstandingCents}c still outstanding.");
            }
            return null;
        }

        /// <summary>
        /// Phase D: issues an eviction notice — the START of a process, never
        /// the removal itself. The notice period is never shorter than
        /// <see cref="MinimumEvictionNoticeDays"/> (TUNING). The legal
        /// process is provisional (Canon §12.7G research hold).
        /// </summary>
        public EvictionNotice IssueEvictionNotice(
            int personId, int householdId, string businessInstanceId,
            string reason, int dayIndex, List<string> diag, int? noticePeriodDays = null)
        {
            diag = diag ?? diagnostics;
            int period = Math.Max(MinimumEvictionNoticeDays, noticePeriodDays ?? MinimumEvictionNoticeDays);
            var notice = new EvictionNotice
            {
                NoticeId = $"evn-{sequence++}",
                PersonId = personId,
                HouseholdId = householdId,
                BusinessInstanceId = businessInstanceId ?? string.Empty,
                IssuedDayIndex = dayIndex,
                EffectiveDayIndex = dayIndex + period,
                Reason = reason ?? string.Empty,
                Status = EvictionNoticeStatus.Issued,
                LegalProcessProvisional = true,
            };
            notices.Add(notice);
            diag.Add($"RentCollectionService: eviction notice '{notice.NoticeId}' issued to P{personId} (H{householdId}) — " +
                $"effective day {notice.EffectiveDayIndex} ({period}d notice). Legal process PROVISIONAL (Canon 12.7G research hold).");
            return notice;
        }

        /// <summary>
        /// Phase D: processes notices whose effective date has passed. A
        /// notice executes ONLY when arrears are still outstanding past the
        /// notice period — repaid arrears withdraw the notice. Removal goes
        /// through the real checkout path (<paramref name="checkoutPerson"/>
        /// returns a refusal or null); the arrears obligation SURVIVES
        /// eviction (Evicted status), it is not forgiven.
        /// </summary>
        public void ProcessEvictions(
            int dayIndex,
            Func<int, string> checkoutPerson,
            List<string> diag)
        {
            diag = diag ?? diagnostics;
            foreach (EvictionNotice notice in notices)
            {
                if (notice == null || notice.Status != EvictionNoticeStatus.Issued) continue;

                int outstanding = OutstandingArrearsFor(notice.PersonId);
                if (outstanding <= 0)
                {
                    notice.Status = EvictionNoticeStatus.Withdrawn;
                    diag.Add($"RentCollectionService: notice '{notice.NoticeId}' WITHDRAWN — P{notice.PersonId}'s arrears are repaid.");
                    continue;
                }
                if (dayIndex < notice.EffectiveDayIndex) continue;

                string refusal = checkoutPerson != null ? checkoutPerson(notice.PersonId) : null;
                if (refusal != null)
                {
                    diag.Add($"RentCollectionService: notice '{notice.NoticeId}' past effective date but checkout refused: {refusal} — the person stays, the debt stays.");
                    continue;
                }
                notice.Status = EvictionNoticeStatus.Executed;
                foreach (RentArrearsRecord record in arrears.Values)
                {
                    if (record != null && record.Status == RentArrearsStatus.Open && record.PersonId == notice.PersonId)
                        record.Status = RentArrearsStatus.Evicted;
                }
                diag.Add($"RentCollectionService: notice '{notice.NoticeId}' EXECUTED — P{notice.PersonId} removed after notice; arrears obligation survives (not forgiven).");
            }
        }

        private int OutstandingArrearsFor(int personId)
        {
            int total = 0;
            foreach (RentArrearsRecord record in arrears.Values)
            {
                if (record != null && record.Status == RentArrearsStatus.Open && record.PersonId == personId)
                    total += record.OutstandingCents;
            }
            return total;
        }

        #region Save / Load
        [Serializable]
        public sealed class RentCollectionServiceSaveDto
        {
            public List<RentArrearsRecord> Arrears = new List<RentArrearsRecord>();
            public List<EvictionNotice> Notices = new List<EvictionNotice>();
        }

        public RentCollectionServiceSaveDto CaptureSaveDto()
        {
            var dto = new RentCollectionServiceSaveDto();
            foreach (RentArrearsRecord record in arrears.Values)
                if (record != null) dto.Arrears.Add(record);
            dto.Notices.AddRange(notices);
            return dto;
        }

        public void LoadFromSaveDto(RentCollectionServiceSaveDto dto)
        {
            arrears.Clear();
            notices.Clear();
            if (dto == null) return;
            foreach (RentArrearsRecord record in dto.Arrears ?? new List<RentArrearsRecord>())
            {
                if (record == null || string.IsNullOrWhiteSpace(record.ArrearsId)) continue;
                if (arrears.ContainsKey(record.ArrearsId))
                {
                    diagnostics.Add($"LoadFromSaveDto: duplicate arrears '{record.ArrearsId}' skipped.");
                    continue;
                }
                arrears[record.ArrearsId] = record;
            }
            notices.AddRange(dto.Notices ?? new List<EvictionNotice>());
        }
        #endregion
    }
}
