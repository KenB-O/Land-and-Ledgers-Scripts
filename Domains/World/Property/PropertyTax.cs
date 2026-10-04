using System;
using System.Collections.Generic;
using LandLedgers.Economy.Financing;
using LandLedgers.Primitives;
using UnityEngine;

namespace LandLedgers.World.Property
{
    /// <summary>NX-3C: tax assessment lifecycle.</summary>
    public enum TaxAssessmentStatus
    {
        Unspecified = 0,
        Assessed = 1,    // levied, not yet due
        Delinquent = 2,  // past due — penalties and interest accruing
        LienFiled = 3,   // tax lien filed against the parcel
        Sold = 4,        // sold at tax sale (redemption may be open)
        Redeemed = 5,    // owner redeemed within the window
        Satisfied = 6,   // paid in full
    }

    /// <summary>
    /// NX-3C: one property-tax assessment. Historical: 1870s western
    /// taxation was property-based (no income tax); rates and regimes are
    /// calibration per Canon Part XV. Penalty/interest terms are recorded
    /// on the assessment as written, never invented mid-stream.
    /// </summary>
    [Serializable]
    public sealed class TaxAssessment
    {
        public string AssessmentId = string.Empty;
        public string ParcelId = string.Empty;
        public string OwnerName = string.Empty;
        public int TaxYear;
        public int AmountCents;
        public int DueDayIndex;
        public int PenaltyBpsMonthly;   // penalty rate, basis points per month, as levied
        public int InterestBpsAnnual;   // interest rate, basis points per annum, as levied
        public int PenaltiesAccruedCents;
        public int InterestAccruedCents;
        public int AmountPaidCents;
        public TaxAssessmentStatus Status = TaxAssessmentStatus.Unspecified;
        public string TaxLienInstrumentId = string.Empty;

        public TaxAssessment() { }

        public int TotalOwedCents() =>
            Math.Max(0, AmountCents + PenaltiesAccruedCents + InterestAccruedCents - AmountPaidCents);
    }

    /// <summary>NX-3C: one named bid at a tax sale. No anonymous bidders.</summary>
    [Serializable]
    public sealed class TaxSaleBid
    {
        public string BidderName = string.Empty;
        public int AmountCents;

        public TaxSaleBid() { }
    }

    /// <summary>NX-3C: one tax sale of a parcel.</summary>
    [Serializable]
    public sealed class TaxSale
    {
        public string SaleId = string.Empty;
        public string ParcelId = string.Empty;
        public string OwnerName = string.Empty;
        public string AssessmentId = string.Empty;
        public string BuyerName = string.Empty;
        public int SalePriceCents;
        public int SaleCostsCents;
        public int TaxesRecoveredCents;
        public int SurplusToOwnerCents;
        public int SaleDayIndex = -1;
        public int RedemptionDeadlineDayIndex = -1;
        public bool Redeemed;

        public TaxSale() { }
    }

    /// <summary>
    /// NX-3C: property-tax delinquency → tax sale. Canon §17.7: "Tax
    /// obligations enter a staged collection process rather than causing
    /// instant confiscation. Unpaid taxes can become delinquent, accrue
    /// penalties/interest, create liens, expose personal property to lawful
    /// collection and real property to tax sale, and create redemption/
    /// title-history consequences under the dated regime."
    ///
    /// The 1870s western pattern (historical research — calibration): annual
    /// property levy; delinquency after the due date; tax lien filed as a
    /// SENIOR lien (property taxes prime private liens); public auction;
    /// redemption window for the owner (commonly 1–3 years in western states;
    /// this service uses a stated calibration). Proceeds waterfall: taxes +
    /// penalties + costs → senior tax lien → other liens in priority → surplus
    /// to the owner. Nothing is confiscated without the staged process.
    /// </summary>
    public sealed class PropertyTaxService
    {
        /// <summary>
        /// TUNING: redemption window after a tax sale, days. Historical:
        /// western states commonly allowed 1–3 years; 2 years is the
        /// calibration here (Canon Part XV), not doctrine.
        /// </summary>
        public const int TaxRedemptionWindowDays = 730;

        private readonly Dictionary<string, TaxAssessment> assessments =
            new Dictionary<string, TaxAssessment>(StringComparer.Ordinal);
        private readonly Dictionary<string, TaxSale> sales =
            new Dictionary<string, TaxSale>(StringComparer.Ordinal);
        private readonly List<string> diagnostics = new List<string>();

        public IReadOnlyList<string> Diagnostics => diagnostics;

        /// <summary>All tax-sale ids, in conducted order.</summary>
        public IReadOnlyList<string> SaleIds
        {
            get
            {
                var ids = new List<string>();
                foreach (TaxSale sale in sales.Values) ids.Add(sale.SaleId);
                return ids;
            }
        }

        /// <summary>Levies the annual property tax on a parcel.</summary>
        public TaxAssessment LevyAssessment(
            EntityIdRegistry ids, string parcelId, string ownerName, int taxYear,
            int amountCents, int dueDayIndex, int penaltyBpsMonthly, int interestBpsAnnual,
            List<string> diag = null)
        {
            diag = diag ?? diagnostics;
            if (string.IsNullOrWhiteSpace(parcelId))
            {
                diag.Add("PropertyTaxService.LevyAssessment: a parcel is required.");
                return null;
            }
            if (string.IsNullOrWhiteSpace(ownerName))
            {
                diag.Add("PropertyTaxService.LevyAssessment: the owner must be named.");
                return null;
            }
            if (amountCents <= 0)
            {
                diag.Add("PropertyTaxService.LevyAssessment: the levy must be positive.");
                return null;
            }
            var assessment = new TaxAssessment
            {
                AssessmentId = ids != null ? ids.Allocate(EntityKind.Contract).ToString() : Guid.NewGuid().ToString("N"),
                ParcelId = parcelId,
                OwnerName = ownerName,
                TaxYear = taxYear,
                AmountCents = amountCents,
                DueDayIndex = dueDayIndex,
                PenaltyBpsMonthly = Math.Max(0, penaltyBpsMonthly),
                InterestBpsAnnual = Math.Max(0, interestBpsAnnual),
                Status = TaxAssessmentStatus.Assessed,
            };
            assessments[assessment.AssessmentId] = assessment;
            diag.Add($"PropertyTaxService: {taxYear} levy on '{parcelId}' — {amountCents}c due day {dueDayIndex} " +
                $"(penalty {penaltyBpsMonthly}bps/mo, interest {interestBpsAnnual}bps/yr as levied).");
            return assessment;
        }

        /// <summary>Records a tax payment against an assessment.</summary>
        public string PayTax(TaxAssessment assessment, int amountCents, int dayIndex, List<string> diag = null)
        {
            diag = diag ?? diagnostics;
            if (assessment == null) return "PropertyTaxService.PayTax: an assessment is required.";
            if (amountCents <= 0) return "PropertyTaxService.PayTax: the payment must be positive.";
            if (assessment.Status == TaxAssessmentStatus.Satisfied)
                return "PropertyTaxService.PayTax: this assessment is already satisfied.";
            assessment.AmountPaidCents += amountCents;
            if (assessment.TotalOwedCents() <= 0)
            {
                assessment.Status = TaxAssessmentStatus.Satisfied;
                diag.Add($"PropertyTaxService: assessment {assessment.AssessmentId} satisfied day {dayIndex}.");
            }
            else
            {
                diag.Add($"PropertyTaxService: {amountCents}c paid on assessment {assessment.AssessmentId} — {assessment.TotalOwedCents()}c still owed.");
            }
            return null;
        }

        /// <summary>
        /// Advances one day: unpaid assessments past due become delinquent and
        /// accrue penalties (monthly) and interest (daily). Staged, never instant.
        /// </summary>
        public void AdvanceDay(int dayIndex, List<string> diag = null)
        {
            diag = diag ?? diagnostics;
            foreach (TaxAssessment assessment in assessments.Values)
            {
                if (assessment.Status != TaxAssessmentStatus.Assessed &&
                    assessment.Status != TaxAssessmentStatus.Delinquent)
                    continue;
                if (assessment.TotalOwedCents() <= 0) continue;
                if (dayIndex <= assessment.DueDayIndex) continue;

                if (assessment.Status == TaxAssessmentStatus.Assessed)
                {
                    assessment.Status = TaxAssessmentStatus.Delinquent;
                    diag.Add($"PropertyTaxService: assessment {assessment.AssessmentId} DELINQUENT day {dayIndex} — staged collection begins, not confiscation (Canon §17.7).");
                }
                int daysOverdue = dayIndex - assessment.DueDayIndex;
                // Monthly penalty on the base amount; daily interest on base + penalties.
                int monthsOverdue = daysOverdue / 30;
                int expectedPenalty = (int)Math.Round(assessment.AmountCents * (assessment.PenaltyBpsMonthly / 10000.0) * monthsOverdue);
                if (expectedPenalty > assessment.PenaltiesAccruedCents)
                {
                    int delta = expectedPenalty - assessment.PenaltiesAccruedCents;
                    assessment.PenaltiesAccruedCents = expectedPenalty;
                    diag.Add($"PropertyTaxService: assessment {assessment.AssessmentId} penalty +{delta}c ({monthsOverdue} mo overdue).");
                }
                int dailyInterest = (int)Math.Round(
                    (assessment.AmountCents + assessment.PenaltiesAccruedCents) * (assessment.InterestBpsAnnual / 10000.0) / 365.0);
                if (dailyInterest > 0)
                    assessment.InterestAccruedCents += dailyInterest;
            }
        }

        /// <summary>
        /// Files a tax lien — SENIOR to private liens (1870s practice:
        /// property taxes prime private encumbrances). Recorded through the
        /// T2A credit registry as a real PropertyLien.
        /// </summary>
        public string FileTaxLien(
            TaxAssessment assessment, EntityIdRegistry ids, CreditRegistry credit,
            int dayIndex, List<string> diag = null)
        {
            diag = diag ?? diagnostics;
            if (assessment == null) return "PropertyTaxService.FileTaxLien: an assessment is required.";
            if (assessment.Status != TaxAssessmentStatus.Delinquent)
                return $"PropertyTaxService.FileTaxLien: assessment {assessment.AssessmentId} is {assessment.Status} — liens follow delinquency, not anticipation.";
            if (credit == null || ids == null)
                return "PropertyTaxService.FileTaxLien: the credit registry is required — a lien is a recorded claim.";
            PropertyLien lien = credit.FileLien(ids, "County Tax Collector", assessment.OwnerName,
                assessment.ParcelId, assessment.TotalOwedCents(),
                $"delinquent {assessment.TaxYear} property tax (senior tax lien)", dayIndex, diag);
            if (lien == null) return "PropertyTaxService.FileTaxLien: the credit registry refused the lien.";
            assessment.TaxLienInstrumentId = lien.InstrumentId.ToString();
            assessment.Status = TaxAssessmentStatus.LienFiled;
            diag.Add($"PropertyTaxService: senior tax lien filed on '{assessment.ParcelId}' for {assessment.TotalOwedCents()}c.");
            return null;
        }

        /// <summary>
        /// Conducts the tax sale at public auction. Bidders are named; the
        /// highest bid wins. Waterfall: taxes + penalties + costs → the senior
        /// tax lien → other liens in the caller's priority order → surplus to
        /// the owner. Title transfers on CourtOrder basis with the redemption
        /// window running.
        /// </summary>
        public string ConductTaxSale(
            TaxAssessment assessment, List<TaxSaleBid> bids, List<PropertyLien> otherLiens,
            int saleCostsCents, int dayIndex, EntityIdRegistry ids, TitleAuthority titles,
            CreditRegistry credit, List<string> diag = null)
        {
            diag = diag ?? diagnostics;
            if (assessment == null) return "PropertyTaxService.ConductTaxSale: an assessment is required.";
            if (assessment.Status != TaxAssessmentStatus.LienFiled)
                return $"PropertyTaxService.ConductTaxSale: assessment {assessment.AssessmentId} is {assessment.Status} — " +
                    "sale follows the staged process: levy → delinquency → lien → sale (Canon §17.7).";
            if (bids == null || bids.Count == 0)
            {
                diag.Add($"PropertyTaxService: no bidders at the '{assessment.ParcelId}' tax sale — no sale faked; the lien stands.");
                return "PropertyTaxService.ConductTaxSale: no bidders — no sale faked.";
            }

            TaxSaleBid winner = null;
            foreach (TaxSaleBid bid in bids)
            {
                if (bid == null || string.IsNullOrWhiteSpace(bid.BidderName) || bid.AmountCents <= 0)
                {
                    diag.Add("PropertyTaxService: an anonymous or non-positive bid was offered and rejected.");
                    continue;
                }
                if (winner == null || bid.AmountCents > winner.AmountCents) winner = bid;
            }
            if (winner == null)
                return "PropertyTaxService.ConductTaxSale: no valid bids — no sale faked.";

            var sale = new TaxSale
            {
                SaleId = ids != null ? ids.Allocate(EntityKind.Contract).ToString() : Guid.NewGuid().ToString("N"),
                ParcelId = assessment.ParcelId,
                OwnerName = assessment.OwnerName,
                AssessmentId = assessment.AssessmentId,
                BuyerName = winner.BidderName,
                SalePriceCents = winner.AmountCents,
                SaleCostsCents = Math.Max(0, saleCostsCents),
                SaleDayIndex = dayIndex,
                RedemptionDeadlineDayIndex = dayIndex + TaxRedemptionWindowDays,
            };
            sales[sale.SaleId] = sale;

            int remaining = winner.AmountCents - sale.SaleCostsCents;
            diag.Add($"PropertyTaxService: '{assessment.ParcelId}' sold at tax sale to '{winner.BidderName}' " +
                $"for {winner.AmountCents}c (costs {sale.SaleCostsCents}c).");

            // 1. Taxes, penalties, and costs — the senior claim.
            int taxesOwed = assessment.TotalOwedCents();
            int toTaxes = Math.Min(remaining, taxesOwed);
            remaining -= toTaxes;
            sale.TaxesRecoveredCents = toTaxes;
            assessment.AmountPaidCents += toTaxes;
            diag.Add($"PropertyTaxService: taxes recovered {toTaxes}c of {taxesOwed}c owed.");

            // 2. The senior tax lien is satisfied by the sale to the extent paid.
            if (credit != null && !string.IsNullOrEmpty(assessment.TaxLienInstrumentId) && toTaxes > 0)
                credit.SatisfyInstrument(assessment.TaxLienInstrumentId, diag);

            // 3. Other liens in priority order.
            if (otherLiens != null)
            {
                foreach (PropertyLien lien in otherLiens)
                {
                    if (lien == null || remaining <= 0) break;
                    if (lien.Status != CreditInstrumentStatus.Active) continue;
                    int toLien = Math.Min(remaining, lien.AmountCents);
                    remaining -= toLien;
                    diag.Add($"PropertyTaxService: lien '{lien.InstrumentId}' ({lien.ClaimantName}) paid {toLien}c of {lien.AmountCents}c.");
                    if (toLien >= lien.AmountCents && credit != null)
                        credit.SatisfyInstrument(lien.InstrumentId.ToString(), diag);
                }
            }

            // 4. Surplus to the owner — never kept by the county.
            if (remaining > 0)
            {
                sale.SurplusToOwnerCents = remaining;
                diag.Add($"PropertyTaxService: surplus {remaining}c to owner '{assessment.OwnerName}'.");
            }

            if (assessment.TotalOwedCents() <= 0)
                assessment.Status = TaxAssessmentStatus.Satisfied;
            else
                diag.Add($"PropertyTaxService: {assessment.TotalOwedCents()}c of taxes still unsatisfied after the sale — the claim survives, honestly recorded.");
            assessment.Status = assessment.Status == TaxAssessmentStatus.Satisfied
                ? TaxAssessmentStatus.Satisfied : TaxAssessmentStatus.Sold;

            if (titles != null)
            {
                string problem = titles.TransferTitle(ids, assessment.ParcelId, winner.BidderName,
                    TitleBasis.CourtOrder, sale.SaleId, dayIndex,
                    $"tax sale — subject to {TaxRedemptionWindowDays}-day redemption", diag);
                if (problem != null) return problem;
            }

            diag.Add($"PropertyTaxService: tax sale {sale.SaleId} complete — redemption open to day {sale.RedemptionDeadlineDayIndex}.");
            return null;
        }

        /// <summary>
        /// The owner redeems within the window by paying the sale price plus
        /// costs — title restored through the T2F chain.
        /// </summary>
        public string Redeem(
            string saleId, int amountPaidCents, int dayIndex,
            EntityIdRegistry ids, TitleAuthority titles, List<string> diag = null)
        {
            diag = diag ?? diagnostics;
            if (!sales.TryGetValue(saleId, out TaxSale sale))
                return $"PropertyTaxService.Redeem: unknown sale '{saleId}'.";
            if (sale.Redeemed) return $"PropertyTaxService.Redeem: sale '{saleId}' already redeemed.";
            if (dayIndex > sale.RedemptionDeadlineDayIndex)
                return $"PropertyTaxService.Redeem: redemption expired day {sale.RedemptionDeadlineDayIndex}.";
            int required = sale.SalePriceCents + sale.SaleCostsCents;
            if (amountPaidCents < required)
                return $"PropertyTaxService.Redeem: {amountPaidCents}c does not meet the {required}c redemption price.";
            if (titles != null)
            {
                string problem = titles.TransferTitle(ids, sale.ParcelId, sale.OwnerName,
                    TitleBasis.CourtOrder, sale.SaleId, dayIndex, "redemption after tax sale", diag);
                if (problem != null) return problem;
            }
            sale.Redeemed = true;
            diag.Add($"PropertyTaxService: '{sale.OwnerName}' redeemed '{sale.ParcelId}' day {dayIndex} for {amountPaidCents}c — title restored.");
            return null;
        }

        public TaxSale GetSale(string saleId)
        {
            return sales.TryGetValue(saleId, out TaxSale sale) ? sale : null;
        }

        #region Queries (P5: the live tax driver and read surfaces need these)

        public TaxAssessment GetAssessment(string assessmentId)
        {
            return !string.IsNullOrEmpty(assessmentId) && assessments.TryGetValue(assessmentId, out TaxAssessment a) ? a : null;
        }

        public IReadOnlyList<TaxAssessment> AllAssessments
        {
            get { return new List<TaxAssessment>(assessments.Values); }
        }

        /// <summary>True when this parcel already has an assessment for the given tax year.</summary>
        public bool HasAssessmentForParcelYear(string parcelId, int taxYear)
        {
            if (string.IsNullOrEmpty(parcelId)) return false;
            foreach (TaxAssessment assessment in assessments.Values)
            {
                if (assessment != null &&
                    string.Equals(assessment.ParcelId, parcelId, StringComparison.Ordinal) &&
                    assessment.TaxYear == taxYear)
                    return true;
            }
            return false;
        }

        #endregion

        #region Save / Load - P5: the service previously had no save DTO, so assessments
        // died with the session. Save-DTO methods live in the owning runtime class.

        [Serializable]
        public sealed class PropertyTaxSaveDto
        {
            public List<TaxAssessment> Assessments = new List<TaxAssessment>();
            public List<TaxSale> Sales = new List<TaxSale>();
        }

        public PropertyTaxSaveDto CaptureSaveDto()
        {
            return new PropertyTaxSaveDto
            {
                Assessments = new List<TaxAssessment>(assessments.Values),
                Sales = new List<TaxSale>(sales.Values),
            };
        }

        public void LoadFromSaveDto(PropertyTaxSaveDto dto)
        {
            assessments.Clear();
            sales.Clear();
            if (dto == null) return;
            if (dto.Assessments != null)
            {
                foreach (TaxAssessment assessment in dto.Assessments)
                {
                    if (assessment != null && !string.IsNullOrEmpty(assessment.AssessmentId))
                        assessments[assessment.AssessmentId] = assessment;
                }
            }
            if (dto.Sales != null)
            {
                foreach (TaxSale sale in dto.Sales)
                {
                    if (sale != null && !string.IsNullOrEmpty(sale.SaleId))
                        sales[sale.SaleId] = sale;
                }
            }
        }

        #endregion
    }
}
