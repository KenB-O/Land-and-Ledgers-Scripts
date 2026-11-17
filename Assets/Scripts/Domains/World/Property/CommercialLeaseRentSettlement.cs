using System;
using System.Collections.Generic;
using LandLedgers.Economy.Creation;
using LandLedgers.Economy.Financing;
using LandLedgers.Population;
using LandLedgers.Primitives;

namespace LandLedgers.World.Property
{
    /// <summary>B2: the outcome of one rent settlement pass.</summary>
    [Serializable]
    public sealed class CommercialLeaseRentSettlementResult
    {
        public int PeriodsSettled;
        public int PeriodsPaidInFull;
        public int PeriodsInArrears;
        public int TotalPaidCents;
        public int TotalArrearsCents;
        public List<string> Notes = new List<string>();

        public CommercialLeaseRentSettlementResult() { }
    }

    /// <summary>
    /// B2 (universal business campaign 2026-11-17): monthly commercial
    /// rent settlement. Rent falls due every <see
    /// cref="CommercialLeaseCalibration.RentPeriodDays"/> days from the
    /// lease start; period 0 is the first month (prepaid at signing by
    /// the driver's deposit-plus-first-rent transfer, so the settlement
    /// normally starts at period 1). Each due period is settled exactly
    /// once — paid in full from the business's real cash, paid in part
    /// with the remainder booked as a real Payable, or booked in full as
    /// a Payable when the business cannot pay or the landlord has no
    /// household ledger to receive it. A period whose arrears cannot be
    /// booked (no obligations authority) is NOT settled and is retried
    /// next pass, so rent is never silently dropped. Money is never spent
    /// before its destination is known: the landlord ledger is resolved
    /// before the business cash is touched.
    ///
    /// Arrears mirror the tenancy pattern (TenancyPaymentService): the
    /// Payable names the lease agreement, so the deposit refund on lease
    /// end can check for unsettled rent.
    /// </summary>
    public sealed class CommercialLeaseRentSettlement
    {
        /// <summary>
        /// Settles every due rent period on every Active lease. Returns
        /// the pass outcome; the market's agreements carry the settled
        /// periods, so a second call in the same week is a no-op.
        /// </summary>
        public CommercialLeaseRentSettlementResult SettleDue(
            CommercialLeaseMarket market,
            INpcBusinessCashPort cash,
            Func<int, HouseholdLedger> landlordLedgerByPersonId,
            FinancialObligationAuthority obligations,
            EntityIdRegistry ids,
            int dayIndex,
            List<string> diag)
        {
            var result = new CommercialLeaseRentSettlementResult();
            if (market == null)
            {
                result.Notes.Add("CommercialLeaseRent: no lease market — nothing settled.");
                diag?.Add("CommercialLeaseRent: no lease market — nothing settled.");
                return result;
            }

            var agreements = new List<CommercialLeaseAgreement>(market.Agreements);
            foreach (CommercialLeaseAgreement agreement in agreements)
            {
                if (agreement == null || agreement.Status != CommercialLeaseAgreementStatus.Active)
                    continue;
                if (string.IsNullOrWhiteSpace(agreement.BusinessInstanceId))
                {
                    result.Notes.Add($"CommercialLeaseRent: lease '{agreement.AgreementId}' has no business — rent not settled.");
                    continue;
                }

                int period = agreement.LastSettledPeriodIndex + 1;
                while (agreement.StartDayIndex + period * CommercialLeaseCalibration.RentPeriodDays <= dayIndex)
                {
                    if (!SettlePeriod(agreement, period, cash, landlordLedgerByPersonId,
                        obligations, ids, dayIndex, result, diag))
                    {
                        // The period could not be settled (arrears
                        // unbookable) — stop here so the next pass retries
                        // this period instead of skipping past it.
                        break;
                    }

                    period++;
                }
            }

            return result;
        }

        /// <summary>Settles one period. True when the period is settled (paid or booked as arrears).</summary>
        private bool SettlePeriod(CommercialLeaseAgreement agreement, int period,
            INpcBusinessCashPort cash,
            Func<int, HouseholdLedger> landlordLedgerByPersonId,
            FinancialObligationAuthority obligations,
            EntityIdRegistry ids,
            int dayIndex,
            CommercialLeaseRentSettlementResult result,
            List<string> diag)
        {
            int rent = agreement.MonthlyRentCents;
            int dueDay = agreement.StartDayIndex + period * CommercialLeaseCalibration.RentPeriodDays;
            if (rent <= 0)
            {
                result.Notes.Add($"CommercialLeaseRent: lease '{agreement.AgreementId}' period {period} has no rent ({rent}c) — marked settled, nothing moved.");
                agreement.LastSettledPeriodIndex = period;
                result.PeriodsSettled++;
                return true;
            }

            // Destination first: the landlord ledger is resolved before
            // the business cash is touched, so money is never spent into
            // a void.
            HouseholdLedger landlordLedger = landlordLedgerByPersonId != null
                ? landlordLedgerByPersonId(agreement.LandlordPersonId)
                : null;
            int cashOnHand = cash != null ? cash.ReadCashCents(agreement.BusinessInstanceId) : 0;
            bool arrearsNeeded = landlordLedger == null || cashOnHand < rent;
            if (arrearsNeeded && (obligations == null || ids == null))
            {
                string reason = landlordLedger == null
                    ? $"landlord P{agreement.LandlordPersonId} has no household ledger to receive it"
                    : $"business cash {cashOnHand}c is below the {rent}c rent";
                result.Notes.Add($"CommercialLeaseRent: lease '{agreement.AgreementId}' period {period} ({rent}c, due day {dueDay}) " +
                    $"NOT settled — {reason} — and no obligations authority is available to book the arrears. " +
                    "Nothing moved; the period will be retried next pass.");
                diag?.Add($"CommercialLeaseRent: lease '{agreement.AgreementId}' period {period} arrears unbookable (no obligations authority) — nothing moved.");
                return false;
            }

            int paid = 0;
            if (landlordLedger != null && cashOnHand > 0)
            {
                int toPay = Math.Min(cashOnHand, rent);
                string spendRefusal = cash != null
                    ? cash.Spend(agreement.BusinessInstanceId, dayIndex, toPay,
                        $"commercial rent {agreement.AgreementId} period {period}")
                    : "no business cash port.";
                if (spendRefusal != null)
                {
                    result.Notes.Add($"CommercialLeaseRent: lease '{agreement.AgreementId}' period {period} spend refused ({spendRefusal}) — " +
                        "the full rent is booked as arrears.");
                    diag?.Add($"CommercialLeaseRent: lease '{agreement.AgreementId}' period {period} spend refused — arrears booked instead.");
                }
                else
                {
                    string inflowRefusal = landlordLedger.RecordInflow(dayIndex, toPay,
                        HouseholdIncomeSource.CommercialRentReceived, agreement.AgreementId,
                        $"commercial rent for building {agreement.BuildingId} via {agreement.AgreementId}, period {period}",
                        "business:" + agreement.BusinessInstanceId);
                    if (inflowRefusal != null)
                    {
                        // Cannot happen with the fixed arguments above
                        // (positive amount, named source, non-empty
                        // reason); if it ever does, the spend already
                        // happened and the inflow failed — say so loudly
                        // instead of hiding a conservation break.
                        result.Notes.Add($"CommercialLeaseRent: CONSERVATION BREAK — lease '{agreement.AgreementId}' period {period}: " +
                            $"{toPay}c left business '{agreement.BusinessInstanceId}' but the landlord inflow was refused ({inflowRefusal}). " +
                            "The payment is recorded here and nowhere else; a human must reconcile it.");
                        diag?.Add($"CommercialLeaseRent: CONSERVATION BREAK on lease '{agreement.AgreementId}' period {period} — {toPay}c spent, landlord inflow refused.");
                        paid = toPay;
                    }
                    else
                    {
                        paid = toPay;
                    }
                }
            }

            int remainder = rent - paid;
            if (remainder > 0)
            {
                FinancialObligation arrears = obligations.Create(
                    ids, FinancialObligationKind.Payable,
                    debtor: "business:" + agreement.BusinessInstanceId,
                    creditor: agreement.LandlordHolderId,
                    principalCents: remainder,
                    dayIndex: dueDay,
                    terms: $"commercial rent arrears — lease {agreement.AgreementId}, building {agreement.BuildingId}, " +
                           $"period {period} due day {dueDay}; negotiated arrears, Canon XXXI Part VI / Canon 12.7H; " +
                           "no automatic eviction (enforcement is its own process)",
                    purpose: $"commercial rent arrears: {agreement.AgreementId} period {period}",
                    agreementId: agreement.AgreementId);
                if (arrears == null)
                {
                    result.Notes.Add($"CommercialLeaseRent: lease '{agreement.AgreementId}' period {period} arrears ({remainder}c) " +
                        "could NOT be booked (authority refused) — nothing moved for this period; it will be retried next pass.");
                    diag?.Add($"CommercialLeaseRent: lease '{agreement.AgreementId}' period {period} arrears unbookable — nothing moved.");
                    return false;
                }

                agreement.RentPayableIds.Add(arrears.ObligationId);
                result.PeriodsInArrears++;
                result.TotalArrearsCents += remainder;
                result.Notes.Add($"CommercialLeaseRent: lease '{agreement.AgreementId}' period {period} ({rent}c, due day {dueDay}): " +
                    $"paid {paid}c, arrears {remainder}c booked as '{arrears.ObligationId}'.");
            }
            else
            {
                result.PeriodsPaidInFull++;
                result.Notes.Add($"CommercialLeaseRent: lease '{agreement.AgreementId}' period {period} ({rent}c, due day {dueDay}) paid in full.");
            }

            result.TotalPaidCents += paid;
            result.PeriodsSettled++;
            agreement.LastSettledPeriodIndex = period;
            return true;
        }
    }
}
