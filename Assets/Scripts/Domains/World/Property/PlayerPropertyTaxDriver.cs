using System;
using System.Collections.Generic;
using LandLedgers.Civic;

namespace LandLedgers.World.Property
{
    /// <summary>
    /// P5: the live property-tax driver for player-owned parcels. The
    /// PropertyTaxService modeled levy → delinquency → lien → sale → redemption
    /// completely, but nothing in production ever instantiated or drove it — taxes
    /// were never levied (dead end P5-TAX). This driver runs on the acquisition
    /// manager's weekly tick:
    ///   - advances existing assessments day-by-day (delinquency + penalty/interest
    ///     accrual through PropertyTaxService.AdvanceDay),
    ///   - levies the annual assessment once per tax year per owned parcel, using the
    ///     parcel's live estimated value as the assessed value and the labeled
    ///     PropertyTaxCalibration rates (DESIGN FORK P5-TAX-RATE).
    ///
    /// Deliberately NOT driven: lien filing needs a production T2A CreditRegistry
    /// (none exists in production — dead end P5-LIEN, recorded as a follow-up) and
    /// the tax sale needs a real named bidder source (inventing bidders would
    /// violate upstream provenance). Delinquent assessments accrue honestly and wait
    /// for those wirings instead of being faked through.
    /// </summary>
    public sealed class PlayerPropertyTaxDriver
    {
        public delegate bool TrySpendCashDelegate(int cents, string label, out string message);

        private readonly PropertyTaxService taxes;
        private readonly Func<IReadOnlyList<int>> ownedPlotIds;
        private readonly Func<int, int> assessedValueCentsForPlot;
        private readonly TrySpendCashDelegate trySpend;
        private readonly List<string> diagnostics = new List<string>();

        public PlayerPropertyTaxDriver(
            PropertyTaxService taxes,
            Func<IReadOnlyList<int>> ownedPlotIds,
            Func<int, int> assessedValueCentsForPlot,
            TrySpendCashDelegate trySpend)
        {
            this.taxes = taxes ?? new PropertyTaxService();
            this.ownedPlotIds = ownedPlotIds;
            this.assessedValueCentsForPlot = assessedValueCentsForPlot;
            this.trySpend = trySpend;
        }

        public IReadOnlyList<string> Diagnostics => diagnostics;
        public PropertyTaxService Taxes => taxes;
        public int LastProcessedDayIndex { get; private set; }

        public void RestoreLastProcessedDayIndex(int dayIndex)
        {
            LastProcessedDayIndex = Math.Max(0, dayIndex);
        }

        /// <summary>
        /// Weekly tick: advance assessments through the elapsed days, then levy any
        /// missing annual assessments for the current tax year.
        /// </summary>
        public void ResolveWeek(int currentDayIndex)
        {
            currentDayIndex = Math.Max(0, currentDayIndex);
            for (int d = LastProcessedDayIndex + 1; d <= currentDayIndex; d++)
                taxes.AdvanceDay(d, diagnostics);
            LastProcessedDayIndex = currentDayIndex;
            LevyMissingAnnualAssessments(currentDayIndex);
        }

        private void LevyMissingAnnualAssessments(int currentDayIndex)
        {
            IReadOnlyList<int> plotIds = ownedPlotIds != null ? ownedPlotIds() : null;
            if (plotIds == null) return;
            int taxYear = PropertyTaxCalibration.TaxYearForDay(currentDayIndex);
            for (int i = 0; i < plotIds.Count; i++)
            {
                int plotId = plotIds[i];
                string parcelId = PlayerTitleBridge.ParcelKeyForPlot(plotId);
                if (taxes.HasAssessmentForParcelYear(parcelId, taxYear)) continue;
                int assessed = assessedValueCentsForPlot != null
                    ? Math.Max(0, assessedValueCentsForPlot(plotId))
                    : 0;
                int levyCents = PropertyTaxCalibration.ComputeLevyCents(assessed);
                if (levyCents <= 0)
                {
                    diagnostics.Add($"PlayerPropertyTaxDriver: no levy for '{parcelId}' in tax year {taxYear} — " +
                        $"assessed value {assessed}c yields no tax; skipped, not zero-filed.");
                    continue;
                }
                TaxAssessment assessment = taxes.LevyAssessment(null, parcelId,
                    PlayerTitleBridge.PlayerHolderName, taxYear, levyCents,
                    currentDayIndex + PropertyTaxCalibration.DueDaysAfterLevy,
                    PropertyTaxCalibration.PenaltyBpsMonthly,
                    PropertyTaxCalibration.InterestBpsAnnual,
                    diagnostics);
                if (assessment != null)
                {
                    diagnostics.Add($"PlayerPropertyTaxDriver: tax year {taxYear} levied on '{parcelId}' — " +
                        $"{levyCents}c on {assessed}c assessed (calibration rates, fork P5-TAX-RATE).");
                }
            }
        }

        /// <summary>
        /// Player-facing tax payment. Pass XI WS3 (PASS9-002): every
        /// refusal PayTax can actually give after the checks below is
        /// decided BEFORE owner cash moves — PayTax refuses only a
        /// satisfied assessment (amount and assessment checks are above),
        /// and the spend delegate has no refund, so spending first would
        /// strand the debit on a satisfied assessment. The satisfied check
        /// therefore happens before trySpend, not after it.
        /// Pass IX WS6 (A1-07/A1-11): when a civic treasury is supplied, the
        /// amount the assessment actually absorbed is credited to it as
        /// TaxReceived — before this pass the spent cash had no recipient and
        /// left the economy. The credit equals the increase in
        /// AmountPaidCents, so player debit == treasury credit exactly; when
        /// the assessment absorbs nothing, nothing is credited.
        /// The player leg has no County Tax Collector cash store in its
        /// call context, so the fee portion of a player payment still
        /// credits the treasury (the existing recipient); the fee split
        /// itself is now booked on the assessment (FeesPaidCents) so a
        /// collector split can be wired later without re-routing fees
        /// already paid. That collector wiring is the open question
        /// recorded in the WS3 report, not a silent change here.
        /// </summary>
        public bool TryPayAssessment(string assessmentId, int amountCents, int dayIndex, out string message,
            CivicTreasuryService civicTreasury = null)
        {
            message = string.Empty;
            TaxAssessment assessment = taxes.GetAssessment(assessmentId);
            if (assessment == null)
            {
                message = $"No property-tax assessment '{assessmentId}' is on the books.";
                return false;
            }
            if (amountCents <= 0)
            {
                message = "The tax payment must be positive.";
                return false;
            }
            if (assessment.Status == TaxAssessmentStatus.Satisfied)
            {
                message = $"Property-tax assessment '{assessmentId}' is already satisfied — no cash moved.";
                return false;
            }
            if (trySpend == null ||
                !trySpend(amountCents, $"Property tax {assessment.TaxYear} — {assessment.ParcelId}", out message))
                return false;
            int paidBefore = assessment.AmountPaidCents;
            string problem = taxes.PayTax(assessment, amountCents, dayIndex, diagnostics);
            if (problem != null)
            {
                message = problem;
                return false;
            }
            int absorbed = Math.Max(0, assessment.AmountPaidCents - paidBefore);
            assessment.FeesPaidCents += Math.Min(assessment.FeePortionOfPaymentCents(absorbed), absorbed);
            assessment.FeesPaidTracked = true;
            if (civicTreasury != null && absorbed > 0)
            {
                civicTreasury.TryCredit(absorbed, CivicLedgerEntryKind.TaxReceived, dayIndex,
                    $"player property tax {assessment.TaxYear} — {assessment.ParcelId}", diagnostics);
            }
            message = $"Paid {amountCents}c toward {assessment.TaxYear} property tax on '{assessment.ParcelId}' — " +
                $"{assessment.TotalOwedCents()}c still owed.";
            return true;
        }
    }
}
