using System;
using System.Collections.Generic;
using LandLedgers.Primitives;
using LandLedgers.World.Property;
using UnityEngine;

namespace LandLedgers.Economy.Financing
{
    /// <summary>NX-3B: foreclosure lifecycle.</summary>
    public enum ForeclosureStage
    {
        Unspecified = 0,
        NoticeIssued = 1,   // borrower notified, cure period running
        SaleScheduled = 2,  // auction announced
        Sold = 3,           // auction held, buyer took title subject to redemption
        Redeemed = 4,       // borrower redeemed within the window — title restored
        Closed = 5,         // redemption expired or surplus/deficiency settled
        VoluntarySurrendered = 6,
    }

    /// <summary>NX-3B: one named bid at a foreclosure auction. No anonymous bidders.</summary>
    [Serializable]
    public sealed class ForeclosureBid
    {
        public string BidderName = string.Empty;
        public int AmountCents;

        public ForeclosureBid() { }
    }

    /// <summary>NX-3B: one mortgage foreclosure case.</summary>
    [Serializable]
    public sealed class ForeclosureCase
    {
        public string CaseId = string.Empty;
        public string MortgageInstrumentId = string.Empty;
        public string PropertyId = string.Empty;
        public string BorrowerName = string.Empty;
        public string LenderName = string.Empty;
        public int DebtOwedCents;      // principal + accrued per the instrument terms
        public ForeclosureStage Stage = ForeclosureStage.Unspecified;
        public int NoticeDayIndex = -1;
        public int SaleDayIndex = -1;
        public string BuyerName = string.Empty;
        public int SalePriceCents;
        public int SaleCostsCents;
        public int SurplusToBorrowerCents;
        public int DeficiencyCents;
        public int RedemptionDeadlineDayIndex = -1;
        public int SurrenderDayIndex = -1;
        public bool SurrenderSatisfiesDebt;

        public ForeclosureCase() { }
    }

    /// <summary>
    /// NX-3B: mortgage foreclosure as an honest legal process. Historical
    /// (1870s western territories): judicial foreclosure sale at public
    /// auction, with a redemption anchor — the canon cites "a one-year
    /// redemption anchor after foreclosure sale" as the strong territorial
    /// anchor (Canon §17 / territorial framework), exact procedure
    /// compressible for gameplay.
    ///
    /// Proceeds waterfall (1870s commercial practice): sale costs first, then
    /// the foreclosing mortgage, then junior liens in priority order, then
    /// surplus to the borrower. A shortfall becomes a deficiency claim per the
    /// instrument terms — recorded, never wished away. Title moves through the
    /// T2F authority on CourtOrder basis; redemption restores it.
    /// </summary>
    public sealed class ForeclosureService
    {
        /// <summary>TUNING: days of notice before the auction (calibration).</summary>
        public const int NoticePeriodDays = 30;

        /// <summary>
        /// Canon anchor: one-year redemption after foreclosure sale. The
        /// borrower (or their heirs) can reclaim the property by paying the
        /// sale price plus costs within the window.
        /// </summary>
        public const int RedemptionWindowDays = 365;

        private readonly Dictionary<string, ForeclosureCase> cases =
            new Dictionary<string, ForeclosureCase>(StringComparer.Ordinal);
        private readonly List<string> diagnostics = new List<string>();
        private readonly FinancialObligationAuthority financialAuthority;

        public ForeclosureService(FinancialObligationAuthority financialAuthority = null)
        {
            this.financialAuthority = financialAuthority;
        }

        public IReadOnlyList<string> Diagnostics => diagnostics;
        public IReadOnlyCollection<ForeclosureCase> Cases => cases.Values;

        /// <summary>Opens a foreclosure case on a defaulted mortgage.</summary>
        public ForeclosureCase RecordDefault(
            EntityIdRegistry ids, MortgageDeed mortgage, int debtOwedCents,
            int dayIndex, List<string> diag = null)
        {
            diag = diag ?? diagnostics;
            if (mortgage == null)
            {
                diag.Add("ForeclosureService.RecordDefault: a mortgage instrument is required.");
                return null;
            }
            if (string.IsNullOrWhiteSpace(mortgage.PropertyId))
            {
                diag.Add("ForeclosureService.RecordDefault: the mortgage names no property — nothing to foreclose.");
                return null;
            }
            if (debtOwedCents <= 0)
            {
                diag.Add("ForeclosureService.RecordDefault: the debt owed must be positive and stated from the instrument terms.");
                return null;
            }
            var kase = new ForeclosureCase
            {
                CaseId = ids != null ? ids.Allocate(EntityKind.Contract).ToString() : Guid.NewGuid().ToString("N"),
                MortgageInstrumentId = mortgage.InstrumentId.ToString(),
                PropertyId = mortgage.PropertyId,
                BorrowerName = mortgage.BorrowerName,
                LenderName = mortgage.LenderName,
                DebtOwedCents = debtOwedCents,
            };
            cases[kase.CaseId] = kase;
            if (financialAuthority != null && !string.IsNullOrWhiteSpace(mortgage.ObligationId))
                financialAuthority.MarkDelinquent(mortgage.ObligationId);
            diag.Add($"ForeclosureService: case {kase.CaseId} opened — '{mortgage.LenderName}' foreclosing on '{mortgage.PropertyId}' ({debtOwedCents}c owed).");
            return kase;
        }

        /// <summary>Issues the foreclosure notice — the cure period starts.</summary>
        public string IssueNotice(ForeclosureCase kase, int dayIndex, List<string> diag = null)
        {
            diag = diag ?? diagnostics;
            if (kase == null) return "ForeclosureService.IssueNotice: a case is required.";
            if (kase.Stage != ForeclosureStage.Unspecified)
                return $"ForeclosureService.IssueNotice: case {kase.CaseId} is already {kase.Stage}.";
            kase.Stage = ForeclosureStage.NoticeIssued;
            kase.NoticeDayIndex = dayIndex;
            diag.Add($"ForeclosureService: notice issued on '{kase.PropertyId}' day {dayIndex} — " +
                $"'{kase.BorrowerName}' has {NoticePeriodDays} days to cure before auction.");
            return null;
        }

        /// <summary>
        /// The borrower cures the default during the notice period — case
        /// closed, no sale. The caller moves the actual money; this records it.
        /// </summary>
        public string RecordCure(ForeclosureCase kase, int amountPaidCents, int dayIndex, List<string> diag = null)
        {
            diag = diag ?? diagnostics;
            if (kase == null) return "ForeclosureService.RecordCure: a case is required.";
            if (kase.Stage != ForeclosureStage.NoticeIssued)
                return $"ForeclosureService.RecordCure: case {kase.CaseId} is {kase.Stage}, not in notice.";
            if (amountPaidCents < kase.DebtOwedCents)
                return $"ForeclosureService.RecordCure: {amountPaidCents}c does not cure {kase.DebtOwedCents}c owed.";
            kase.Stage = ForeclosureStage.Closed;
            diag.Add($"ForeclosureService: case {kase.CaseId} cured day {dayIndex} — {amountPaidCents}c paid, no sale.");
            return null;
        }

        /// <summary>
        /// Conducts the auction after the notice period. Bidders are named;
        /// the highest bid wins. Proceeds waterfall: sale costs → foreclosing
        /// mortgage → junior liens in the caller's priority order → surplus to
        /// the borrower. A shortfall becomes a deficiency claim against the
        /// borrower per the instrument terms. Title transfers on CourtOrder
        /// basis, subject to the redemption window.
        /// </summary>
        public string ConductSale(
            ForeclosureCase kase, List<ForeclosureBid> bids, List<PropertyLien> juniorLiens,
            int saleCostsCents, int dayIndex, EntityIdRegistry ids, TitleAuthority titles,
            CreditRegistry credit, List<string> diag = null)
        {
            diag = diag ?? diagnostics;
            if (kase == null) return "ForeclosureService.ConductSale: a case is required.";
            if (kase.Stage != ForeclosureStage.NoticeIssued && kase.Stage != ForeclosureStage.SaleScheduled)
                return $"ForeclosureService.ConductSale: case {kase.CaseId} is {kase.Stage} — issue notice first.";
            if (dayIndex < kase.NoticeDayIndex + NoticePeriodDays)
                return $"ForeclosureService.ConductSale: notice period runs to day {kase.NoticeDayIndex + NoticePeriodDays} — the auction cannot precede it.";
            if (bids == null || bids.Count == 0)
            {
                diag.Add($"ForeclosureService: no bidders at the '{kase.PropertyId}' auction day {dayIndex} — sale failed, case held open. No phantom buyer invented.");
                return $"ForeclosureService.ConductSale: no bidders — no sale faked.";
            }

            ForeclosureBid winner = null;
            foreach (ForeclosureBid bid in bids)
            {
                if (bid == null || string.IsNullOrWhiteSpace(bid.BidderName) || bid.AmountCents <= 0)
                {
                    diag.Add("ForeclosureService: an anonymous or non-positive bid was offered and rejected.");
                    continue;
                }
                if (winner == null || bid.AmountCents > winner.AmountCents) winner = bid;
            }
            if (winner == null)
                return "ForeclosureService.ConductSale: no valid bids — no sale faked.";

            kase.BuyerName = winner.BidderName;
            kase.SalePriceCents = winner.AmountCents;
            kase.SaleCostsCents = Math.Max(0, saleCostsCents);
            kase.SaleDayIndex = dayIndex;

            int remaining = winner.AmountCents - kase.SaleCostsCents;
            diag.Add($"ForeclosureService: '{kase.PropertyId}' sold to '{winner.BidderName}' for {winner.AmountCents}c (costs {kase.SaleCostsCents}c).");

            // 1. The foreclosing mortgage.
            int toMortgage = Math.Min(remaining, kase.DebtOwedCents);
            remaining -= toMortgage;
            diag.Add($"ForeclosureService: mortgage satisfied {toMortgage}c of {kase.DebtOwedCents}c owed.");

            // 2. Junior liens in priority order.
            if (juniorLiens != null)
            {
                foreach (PropertyLien lien in juniorLiens)
                {
                    if (lien == null || remaining <= 0) break;
                    if (lien.Status != CreditInstrumentStatus.Active) continue;
                    int toLien = Math.Min(remaining, lien.AmountCents);
                    remaining -= toLien;
                    diag.Add($"ForeclosureService: junior lien '{lien.InstrumentId}' ({lien.ClaimantName}) paid {toLien}c of {lien.AmountCents}c.");
                    if (toLien >= lien.AmountCents && credit != null)
                        credit.SatisfyInstrument(lien.InstrumentId.ToString(), diag);
                }
            }

            // 3. Surplus to the borrower; 4. deficiency recorded.
            if (remaining > 0)
            {
                kase.SurplusToBorrowerCents = remaining;
                diag.Add($"ForeclosureService: surplus {remaining}c to borrower '{kase.BorrowerName}'.");
            }
            int mortgageShortfall = kase.DebtOwedCents - toMortgage;
            if (mortgageShortfall > 0)
            {
                kase.DeficiencyCents = mortgageShortfall;
                diag.Add($"ForeclosureService: deficiency {mortgageShortfall}c against '{kase.BorrowerName}' per the instrument terms — recorded, not wished away.");
                if (financialAuthority != null && ids != null && !string.IsNullOrWhiteSpace(GetMortgageObligationId(credit, kase.MortgageInstrumentId)))
                {
                    FinancialObligation predecessor = financialAuthority.Find(GetMortgageObligationId(credit, kase.MortgageInstrumentId));
                    financialAuthority.ApplyPayment(predecessor.ObligationId, toMortgage, dayIndex,
                        kase.BorrowerName, kase.LenderName, kase.CaseId);
                    FinancialObligation recovery = financialAuthority.Refinance(ids,
                        new[] { predecessor.ObligationId }, kase.BorrowerName, kase.LenderName,
                        mortgageShortfall, dayIndex, "foreclosure deficiency claim", "enforcement recovery");
                    if (recovery != null)
                        diag.Add($"ForeclosureService: shared recovery obligation {recovery.ObligationId} records the deficiency without duplicating the mortgage principal.");
                }
                else if (credit != null && ids != null)
                {
                    credit.IssuePromissoryNote(ids, kase.BorrowerName, kase.LenderName, mortgageShortfall,
                        $"deficiency from foreclosure sale of '{kase.PropertyId}' (case {kase.CaseId})",
                        dayIndex, null, diag);
                }
            }
            else if (financialAuthority != null && credit != null && !string.IsNullOrWhiteSpace(GetMortgageObligationId(credit, kase.MortgageInstrumentId)))
            {
                string obligationId = GetMortgageObligationId(credit, kase.MortgageInstrumentId);
                financialAuthority.ApplyPayment(obligationId, toMortgage, dayIndex,
                    kase.BorrowerName, kase.LenderName, kase.CaseId);
            }

            if (credit != null)
                credit.SatisfyInstrument(kase.MortgageInstrumentId, diag);

            if (titles != null)
            {
                string problem = titles.TransferTitle(ids, kase.PropertyId, kase.BuyerName,
                    TitleBasis.CourtOrder, kase.CaseId, dayIndex,
                    $"foreclosure sale — subject to {RedemptionWindowDays}-day redemption", diag);
                if (problem != null) return problem;
            }

            kase.Stage = ForeclosureStage.Sold;
            kase.RedemptionDeadlineDayIndex = dayIndex + RedemptionWindowDays;
            diag.Add($"ForeclosureService: case {kase.CaseId} sold — redemption open to day {kase.RedemptionDeadlineDayIndex} (one-year territorial anchor).");
            return null;
        }

        /// <summary>
        /// Records a negotiated voluntary surrender without treating delivery
        /// of collateral as automatic full satisfaction. The agreement/law
        /// decides whether a remaining deficiency exists.
        /// </summary>
        public string RecordVoluntarySurrender(ForeclosureCase kase, int dayIndex,
            bool satisfiesDebt, List<string> diag = null)
        {
            diag = diag ?? diagnostics;
            if (kase == null) return "ForeclosureService.RecordVoluntarySurrender: a case is required.";
            if (kase.Stage != ForeclosureStage.NoticeIssued && kase.Stage != ForeclosureStage.SaleScheduled)
                return $"ForeclosureService.RecordVoluntarySurrender: case {kase.CaseId} is {kase.Stage}.";
            kase.Stage = ForeclosureStage.VoluntarySurrendered;
            kase.SurrenderDayIndex = dayIndex;
            kase.SurrenderSatisfiesDebt = satisfiesDebt;
            if (satisfiesDebt) kase.DeficiencyCents = 0;
            diag.Add($"ForeclosureService: voluntary surrender of '{kase.PropertyId}' day {dayIndex}; debt satisfaction {(satisfiesDebt ? "agreed in full" : "not agreed in full") }.");
            return null;
        }

        private string GetMortgageObligationId(CreditRegistry credit, string instrumentId)
        {
            if (credit == null || string.IsNullOrWhiteSpace(instrumentId)) return string.Empty;
            foreach (MortgageDeed mortgage in credit.CaptureSaveDto().Mortgages)
                if (mortgage != null && mortgage.InstrumentId.ToString() == instrumentId)
                    return mortgage.ObligationId;
            return string.Empty;
        }

        /// <summary>
        /// The borrower redeems within the window by paying the sale price
        /// plus costs — title restored through the T2F chain.
        /// </summary>
        public string Redeem(ForeclosureCase kase, int amountPaidCents, int dayIndex,
            EntityIdRegistry ids, TitleAuthority titles, List<string> diag = null)
        {
            diag = diag ?? diagnostics;
            if (kase == null) return "ForeclosureService.Redeem: a case is required.";
            if (kase.Stage != ForeclosureStage.Sold)
                return $"ForeclosureService.Redeem: case {kase.CaseId} is {kase.Stage} — nothing to redeem.";
            if (dayIndex > kase.RedemptionDeadlineDayIndex)
                return $"ForeclosureService.Redeem: redemption expired day {kase.RedemptionDeadlineDayIndex}.";
            int required = kase.SalePriceCents + kase.SaleCostsCents;
            if (amountPaidCents < required)
                return $"ForeclosureService.Redeem: {amountPaidCents}c does not meet the {required}c redemption price.";
            if (titles != null)
            {
                string problem = titles.TransferTitle(ids, kase.PropertyId, kase.BorrowerName,
                    TitleBasis.CourtOrder, kase.CaseId, dayIndex, "redemption after foreclosure sale", diag);
                if (problem != null) return problem;
            }
            kase.Stage = ForeclosureStage.Redeemed;
            diag.Add($"ForeclosureService: '{kase.BorrowerName}' redeemed '{kase.PropertyId}' day {dayIndex} for {amountPaidCents}c — title restored.");
            return null;
        }

        /// <summary>Closes the case once redemption has expired and proceeds are settled.</summary>
        public string CloseCase(ForeclosureCase kase, int dayIndex, List<string> diag = null)
        {
            diag = diag ?? diagnostics;
            if (kase == null) return "ForeclosureService.CloseCase: a case is required.";
            if (kase.Stage == ForeclosureStage.Sold && dayIndex <= kase.RedemptionDeadlineDayIndex)
                return $"ForeclosureService.CloseCase: redemption still open to day {kase.RedemptionDeadlineDayIndex}.";
            if (kase.Stage != ForeclosureStage.Sold && kase.Stage != ForeclosureStage.Redeemed && kase.Stage != ForeclosureStage.Closed)
                return $"ForeclosureService.CloseCase: case {kase.CaseId} is {kase.Stage} — nothing to close.";
            kase.Stage = ForeclosureStage.Closed;
            diag.Add($"ForeclosureService: case {kase.CaseId} closed day {dayIndex}.");
            return null;
        }

        public ForeclosureCase Get(string caseId)
        {
            return cases.TryGetValue(caseId, out ForeclosureCase kase) ? kase : null;
        }
    }
}
