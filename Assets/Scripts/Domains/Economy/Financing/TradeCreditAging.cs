using System;
using System.Collections.Generic;
using UnityEngine;

namespace LandLedgers.Economy.Financing
{
    /// <summary>Phase E (E1): arrears aging buckets for trade credit (Tech 6.4).</summary>
    public enum TradeCreditAgingBucket
    {
        Current = 0,
        PastDue1To30 = 1,
        PastDue31To60 = 2,
        PastDue61To90 = 3,
        PastDueOver90 = 4,
    }

    /// <summary>Phase E (E1): collection stages for a trade-credit invoice (Tech 6.4).</summary>
    public enum TradeCreditCollectionStage
    {
        Current = 0,
        ReminderIssued = 1,
        FormalDemand = 2,
        InCollection = 3,
        Defaulted = 4,
    }

    /// <summary>
    /// Phase E: the terms of one trade-credit invoice. The obligation itself
    /// lives in <see cref="FinancialObligationAuthority"/>; this record is the
    /// Tech 6.4 terms/aging/collection view over it — never a second balance.
    /// </summary>
    [Serializable]
    public sealed class TradeCreditInvoiceTerms
    {
        public string ObligationId = string.Empty;
        public string Debtor = string.Empty;
        public string Creditor = string.Empty;
        public int IssuedDayIndex;
        /// <summary>Net payment terms in days (e.g. 30 for net-30).</summary>
        public int NetDays = 30;
        public int LastEvaluatedDayIndex = -1;
        public TradeCreditCollectionStage Stage = TradeCreditCollectionStage.Current;
        public List<string> History = new List<string>();

        public TradeCreditInvoiceTerms() { }

        public int DueDayIndex => IssuedDayIndex + Math.Max(0, NetDays);
    }

    /// <summary>
    /// Phase E (E1): the trade-credit runtime per Tech 6.4 — invoice terms,
    /// arrears aging and collection stages over the shared obligation
    /// authority. It escalates through the authority's own status transitions
    /// (MarkDue / MarkDelinquent / MarkDefaulted) and logs every stage change;
    /// it never invents debt or writes balances.
    /// </summary>
    public sealed class TradeCreditBook
    {
        /// <summary>TUNING: days past due that trigger each collection stage (Tech 6.4; canon sets none).</summary>
        public int ReminderAfterDaysPastDue = 1;
        public int DemandAfterDaysPastDue = 31;
        public int CollectionAfterDaysPastDue = 61;
        public int DefaultAfterDaysPastDue = 121;

        private readonly Dictionary<string, TradeCreditInvoiceTerms> invoices =
            new Dictionary<string, TradeCreditInvoiceTerms>(StringComparer.Ordinal);
        private readonly List<string> diagnostics = new List<string>();

        public IReadOnlyList<string> Diagnostics => diagnostics;
        public IReadOnlyCollection<TradeCreditInvoiceTerms> Invoices => invoices.Values;

        /// <summary>Registers terms for an existing trade-credit obligation.</summary>
        public string RegisterTerms(FinancialObligationAuthority authority, string obligationId,
            int netDays, int dayIndex, List<string> diag)
        {
            diag = diag ?? diagnostics;
            FinancialObligation obligation = authority?.Find(obligationId);
            if (obligation == null)
            {
                diag.Add($"TradeCreditBook: cannot register terms — unknown obligation '{obligationId}'.");
                return $"Unknown obligation '{obligationId}'.";
            }
            if (obligation.Kind != FinancialObligationKind.TradeCredit)
            {
                diag.Add($"TradeCreditBook: obligation '{obligationId}' is {obligation.Kind}, not trade credit — refusing.");
                return $"Obligation '{obligationId}' is not trade credit.";
            }
            if (invoices.ContainsKey(obligationId))
            {
                diag.Add($"TradeCreditBook: terms for '{obligationId}' are already registered — no duplicate registration.");
                return null;
            }
            var terms = new TradeCreditInvoiceTerms
            {
                ObligationId = obligationId,
                Debtor = obligation.Debtor,
                Creditor = obligation.Creditor,
                IssuedDayIndex = obligation.IssuedDayIndex,
                NetDays = Math.Max(1, netDays),
                LastEvaluatedDayIndex = dayIndex,
            };
            terms.History.Add($"day {dayIndex}: terms registered — net {terms.NetDays}, due day {terms.DueDayIndex}.");
            invoices[obligationId] = terms;
            diag.Add($"TradeCreditBook: '{obligationId}' terms registered — {obligation.Debtor} owes {obligation.Creditor}, net {terms.NetDays} (due day {terms.DueDayIndex}).");
            return null;
        }

        public TradeCreditAgingBucket AgingBucket(string obligationId, int dayIndex)
        {
            if (!invoices.TryGetValue(obligationId ?? string.Empty, out TradeCreditInvoiceTerms terms))
                return TradeCreditAgingBucket.Current;
            int daysPastDue = dayIndex - terms.DueDayIndex;
            if (daysPastDue <= 0) return TradeCreditAgingBucket.Current;
            if (daysPastDue <= 30) return TradeCreditAgingBucket.PastDue1To30;
            if (daysPastDue <= 60) return TradeCreditAgingBucket.PastDue31To60;
            if (daysPastDue <= 90) return TradeCreditAgingBucket.PastDue61To90;
            return TradeCreditAgingBucket.PastDueOver90;
        }

        /// <summary>
        /// Ages every registered invoice to the given day: escalates the
        /// obligation status through the shared authority and advances the
        /// collection stage, logging each change. Idempotent per day.
        /// </summary>
        public void EvaluateDay(FinancialObligationAuthority authority, int dayIndex,
            CreditEventLog events, List<string> diag)
        {
            diag = diag ?? diagnostics;
            if (authority == null) { diag.Add("TradeCreditBook: no obligation authority — nothing aged."); return; }
            foreach (TradeCreditInvoiceTerms terms in invoices.Values)
            {
                if (terms == null || terms.LastEvaluatedDayIndex >= dayIndex) continue;
                terms.LastEvaluatedDayIndex = dayIndex;
                FinancialObligation obligation = authority.Find(terms.ObligationId);
                if (obligation == null || obligation.Settled) continue;

                int daysPastDue = dayIndex - terms.DueDayIndex;
                if (daysPastDue <= 0) continue;

                TradeCreditAgingBucket bucket = AgingBucket(terms.ObligationId, dayIndex);
                events?.Record(dayIndex, CreditEventKind.TradeCreditAged, terms.Debtor, terms.Creditor,
                    obligation.TotalOutstandingCents,
                    $"trade credit '{terms.ObligationId}' aged to {bucket} ({daysPastDue} days past due, {obligation.TotalOutstandingCents}c outstanding).",
                    terms.ObligationId);

                TradeCreditCollectionStage target = terms.Stage;
                if (daysPastDue >= DefaultAfterDaysPastDue) target = TradeCreditCollectionStage.Defaulted;
                else if (daysPastDue >= CollectionAfterDaysPastDue) target = TradeCreditCollectionStage.InCollection;
                else if (daysPastDue >= DemandAfterDaysPastDue) target = TradeCreditCollectionStage.FormalDemand;
                else if (daysPastDue >= ReminderAfterDaysPastDue) target = TradeCreditCollectionStage.ReminderIssued;

                if (target > terms.Stage)
                {
                    terms.Stage = target;
                    terms.History.Add($"day {dayIndex}: collection stage advanced to {target} ({daysPastDue} days past due).");
                    diag.Add($"TradeCreditBook: '{terms.ObligationId}' collection stage → {target} ({daysPastDue}dpd).");
                    events?.Record(dayIndex, CreditEventKind.CollectionStageChanged, terms.Debtor, terms.Creditor,
                        obligation.TotalOutstandingCents,
                        $"trade credit '{terms.ObligationId}' collection stage → {target}.",
                        terms.ObligationId);
                }

                if (target >= TradeCreditCollectionStage.ReminderIssued
                    && obligation.Status == FinancialObligationStatus.Active)
                    authority.MarkDue(terms.ObligationId);
                if (target >= TradeCreditCollectionStage.InCollection
                    && obligation.Status != FinancialObligationStatus.Delinquent
                    && obligation.Status != FinancialObligationStatus.Defaulted)
                {
                    authority.MarkDelinquent(terms.ObligationId);
                    events?.Record(dayIndex, CreditEventKind.DelinquencyMarked, terms.Debtor, terms.Creditor,
                        obligation.TotalOutstandingCents,
                        $"trade credit '{terms.ObligationId}' marked delinquent at {daysPastDue} days past due.",
                        terms.ObligationId);
                }
                if (target >= TradeCreditCollectionStage.Defaulted
                    && obligation.Status != FinancialObligationStatus.Defaulted)
                {
                    authority.MarkDefaulted(terms.ObligationId);
                    diag.Add($"TradeCreditBook: '{terms.ObligationId}' marked defaulted at {daysPastDue} days past due.");
                }
            }
        }

        public TradeCreditInvoiceTerms FindTerms(string obligationId)
        {
            return !string.IsNullOrWhiteSpace(obligationId) && invoices.TryGetValue(obligationId, out TradeCreditInvoiceTerms terms)
                ? terms : null;
        }

        #region Save / Load
        [Serializable]
        public sealed class TradeCreditBookSaveDto
        {
            public List<TradeCreditInvoiceTerms> Invoices = new List<TradeCreditInvoiceTerms>();
        }

        public TradeCreditBookSaveDto CaptureSaveDto()
        {
            var dto = new TradeCreditBookSaveDto();
            foreach (TradeCreditInvoiceTerms terms in invoices.Values)
                if (terms != null) dto.Invoices.Add(terms);
            return dto;
        }

        public void LoadFromSaveDto(TradeCreditBookSaveDto dto)
        {
            invoices.Clear();
            if (dto?.Invoices == null) return;
            foreach (TradeCreditInvoiceTerms terms in dto.Invoices)
            {
                if (terms == null || string.IsNullOrWhiteSpace(terms.ObligationId)) continue;
                if (invoices.ContainsKey(terms.ObligationId))
                {
                    diagnostics.Add($"LoadFromSaveDto: duplicate terms for '{terms.ObligationId}' skipped.");
                    continue;
                }
                invoices[terms.ObligationId] = terms;
            }
        }
        #endregion
    }
}
