using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace LandLedgers.Economy.Financing
{
    /// <summary>
    /// Read-only player-facing projection of the shared finance authority.
    /// It deliberately contains no mutation or balance calculation authority.
    /// </summary>
    public static class FinancialObligationReadModel
    {
        public static string BuildPlayerSummary(FinancialObligationAuthority authority,
            string playerId = "player", int currentDayIndex = -1, CreditOfferWorkflow offers = null)
        {
            if (authority == null) return "Shared credit records are unavailable.";
            playerId = string.IsNullOrWhiteSpace(playerId) ? "player" : playerId;
            var owed = new List<FinancialObligation>();
            var held = new List<FinancialObligation>();
            foreach (FinancialObligation obligation in authority.Obligations)
            {
                if (obligation == null || obligation.Settled) continue;
                if (Same(obligation.Debtor, playerId)) owed.Add(obligation);
                if (Same(obligation.Creditor, playerId)) held.Add(obligation);
            }

            var builder = new StringBuilder();
            builder.AppendLine("Shared credit ledger");
            builder.AppendLine($"Obligations owed: {Money(Total(owed))} | Claims held: {Money(Total(held))}");
            AppendObligations(builder, "Obligations owed", owed, authority, currentDayIndex, true);
            AppendObligations(builder, "Claims / notes held", held, authority, currentDayIndex, false);
            AppendFacilities(builder, authority, playerId);
            AppendGuaranties(builder, authority, playerId);
            AppendOffers(builder, offers, playerId);
            return builder.ToString().TrimEnd();
        }

        private static void AppendOffers(StringBuilder builder, CreditOfferWorkflow workflow, string playerId)
        {
            builder.AppendLine(); builder.AppendLine("Financing offers:");
            bool found = false;
            if (workflow != null)
            {
                foreach (CreditOffer offer in workflow.Offers)
                {
                    if (offer == null || offer.Status == CreditOfferStatus.Declined || offer.Status == CreditOfferStatus.Expired
                        || (!Same(offer.Borrower, playerId) && !Same(offer.Lender, playerId))) continue;
                    found = true;
                    builder.AppendLine($"  {offer.OfferId}: requested {Money(offer.RequestedAmountCents)} | offered {Money(offer.OfferedAmountCents)} | cash contribution {Money(offer.RequiredCashContributionCents)} | {offer.AnnualInterestRateBps / 100m:0.##}% | {offer.TermDays} days | {offer.Status}");
                    builder.AppendLine($"    Response: {DescribeOfferResponse(offer)}");
                    if (!string.IsNullOrWhiteSpace(offer.SecurityRequired)) builder.AppendLine($"    Security: {offer.SecurityRequired}");
                    if (!string.IsNullOrWhiteSpace(offer.GuarantorRequired)) builder.AppendLine($"    Guarantor: {offer.GuarantorRequired}");
                    if (!string.IsNullOrWhiteSpace(offer.Conditions)) builder.AppendLine($"    Conditions: {offer.Conditions}");
                }
            }
            if (!found) builder.AppendLine("  None recorded.");
        }

        private static string DescribeOfferResponse(CreditOffer offer)
        {
            if (offer == null) return "unavailable";
            string decision = string.IsNullOrWhiteSpace(offer.DecisionReason) ? "no decision note" : offer.DecisionReason;
            int history = offer.NegotiationHistory != null ? offer.NegotiationHistory.Count : 0;
            string expiry = offer.ExpiryDayIndex >= 0 ? $"expires day {offer.ExpiryDayIndex}" : "no expiry recorded";
            return $"{decision} | {expiry} | {history} negotiation record(s)";
        }

        private static void AppendObligations(StringBuilder builder, string heading,
            List<FinancialObligation> obligations, FinancialObligationAuthority authority,
            int currentDayIndex, bool debtorView)
        {
            builder.AppendLine();
            builder.AppendLine(heading + ":");
            if (obligations.Count == 0) { builder.AppendLine("  None recorded."); return; }
            obligations.Sort((a, b) => string.CompareOrdinal(a.ObligationId, b.ObligationId));
            foreach (FinancialObligation obligation in obligations)
            {
                string counterparty = debtorView ? obligation.Creditor : obligation.Debtor;
                string maturity = obligation.MaturityDayIndex >= 0
                    ? $"matures day {obligation.MaturityDayIndex}"
                    : obligation.IsDemandObligation || obligation.PaymentTerms?.Structure == FinancialPaymentStructure.Demand
                        ? "demand" : "maturity not set";
                builder.AppendLine($"  {obligation.Kind} {obligation.ObligationId}: {Money(obligation.TotalOutstandingCents)} to {Display(counterparty)} | {obligation.Status} | {maturity}");
                builder.AppendLine($"    Principal {Money(obligation.OutstandingPrincipalCents)} | accrued interest {Money(obligation.AccruedInterestCents)} | terms {DescribeTerms(obligation)}");
                if (obligation.SecurityInterestIds != null && obligation.SecurityInterestIds.Count > 0)
                    builder.AppendLine("    Security: " + DescribeSecurity(obligation, authority));
                if (obligation.GuarantyIds != null && obligation.GuarantyIds.Count > 0)
                    builder.AppendLine("    Guaranty: " + DescribeGuaranties(obligation, authority));
                string next = DescribeNextPayment(obligation, currentDayIndex);
                if (!string.IsNullOrEmpty(next)) builder.AppendLine("    " + next);
                if (obligation.Payments != null && obligation.Payments.Count > 0)
                {
                    FinancialPaymentRecord last = obligation.Payments[obligation.Payments.Count - 1];
                    builder.AppendLine($"    Payment history: {obligation.Payments.Count} recorded; last {Money(last.AmountCents)} on day {last.DayIndex}.");
                }
            }
        }

        private static void AppendFacilities(StringBuilder builder, FinancialObligationAuthority authority, string playerId)
        {
            builder.AppendLine(); builder.AppendLine("Credit facilities / open accounts:");
            bool found = false;
            foreach (CreditFacilityRecord facility in authority.Facilities)
            {
                if (facility == null || (!Same(facility.Borrower, playerId) && !Same(facility.Creditor, playerId))) continue;
                found = true;
                builder.AppendLine($"  {facility.FacilityId}: {Display(facility.Borrower)} / {Display(facility.Creditor)} | authorized {Money(facility.AuthorizedLimitCents)} | drawn {Money(facility.DrawnPrincipalCents)} | undrawn {Money(Math.Max(0, facility.AuthorizedLimitCents - facility.DrawnPrincipalCents))}");
            }
            if (!found) builder.AppendLine("  None recorded.");
        }

        private static void AppendGuaranties(StringBuilder builder, FinancialObligationAuthority authority, string playerId)
        {
            builder.AppendLine(); builder.AppendLine("Guaranties / sureties:");
            bool found = false;
            foreach (GuarantyRecord guaranty in authority.Guaranties)
            {
                if (guaranty == null || (!Same(guaranty.Guarantor, playerId) && !Same(guaranty.Creditor, playerId))) continue;
                found = true;
                string state = guaranty.Released ? "released" : guaranty.Called ? $"called {Money(guaranty.CalledAmountCents)}" : "contingent";
                builder.AppendLine($"  {guaranty.GuarantyId}: {Display(guaranty.Guarantor)} supports {Display(guaranty.PrimaryDebtor)} up to {Money(guaranty.MaximumExposureCents)} | {state}");
            }
            if (!found) builder.AppendLine("  None recorded.");
        }

        private static string DescribeSecurity(FinancialObligation obligation, FinancialObligationAuthority authority)
        {
            var pieces = new List<string>();
            foreach (string id in obligation.SecurityInterestIds)
                foreach (SecurityInterestRecord security in authority.Securities)
                    if (security != null && string.Equals(security.SecurityInterestId, id, StringComparison.Ordinal))
                        pieces.Add($"priority {security.Priority}: {(security.CollateralIds != null && security.CollateralIds.Count > 0 ? string.Join(", ", security.CollateralIds) : "unspecified collateral")}{(security.Released ? " (released)" : string.Empty)}");
            return pieces.Count > 0 ? string.Join("; ", pieces) : "record unavailable";
        }

        private static string DescribeGuaranties(FinancialObligation obligation, FinancialObligationAuthority authority)
        {
            var pieces = new List<string>();
            foreach (string id in obligation.GuarantyIds)
                foreach (GuarantyRecord guaranty in authority.Guaranties)
                    if (guaranty != null && string.Equals(guaranty.GuarantyId, id, StringComparison.Ordinal))
                        pieces.Add($"{Display(guaranty.Guarantor)} up to {Money(guaranty.MaximumExposureCents)}");
            return pieces.Count > 0 ? string.Join("; ", pieces) : "record unavailable";
        }

        private static string DescribeNextPayment(FinancialObligation obligation, int currentDayIndex)
        {
            if (obligation.Status == FinancialObligationStatus.Delinquent || obligation.Status == FinancialObligationStatus.Defaulted) return "Payment state: lender attention required.";
            if (obligation.Status == FinancialObligationStatus.Due) return "Payment state: due now.";
            if (obligation.PaymentTerms == null || obligation.PaymentTerms.PaymentIntervalDays <= 0) return string.Empty;
            int nextDay = obligation.IssuedDayIndex + obligation.PaymentTerms.PaymentIntervalDays;
            if (currentDayIndex >= obligation.IssuedDayIndex)
            {
                int intervals = Math.Max(0, (currentDayIndex - obligation.IssuedDayIndex) / obligation.PaymentTerms.PaymentIntervalDays);
                nextDay = obligation.IssuedDayIndex + (intervals + 1) * obligation.PaymentTerms.PaymentIntervalDays;
            }
            int amount = obligation.PaymentTerms.InstallmentPrincipalCents + obligation.AccruedInterestCents;
            return $"Next scheduled payment: {Money(Math.Max(0, amount))} on day {nextDay}.";
        }

        private static string DescribeTerms(FinancialObligation obligation)
        {
            FinancialPaymentTerms terms = obligation.PaymentTerms;
            if (terms == null) return "not specified";
            string rate = (terms.AnnualInterestRateBps / 100m).ToString("0.##", CultureInfo.InvariantCulture) + "% annual";
            return $"{terms.Structure}, {rate}";
        }

        private static int Total(List<FinancialObligation> values)
        {
            long total = 0; foreach (FinancialObligation value in values) total += value?.TotalOutstandingCents ?? 0;
            return total > int.MaxValue ? int.MaxValue : (int)total;
        }
        private static bool Same(string left, string right) => string.Equals(left, right, StringComparison.OrdinalIgnoreCase);
        private static string Display(string value) => string.IsNullOrWhiteSpace(value) ? "unknown party" : value;
        private static string Money(int cents) => "$" + (cents / 100m).ToString("N2", CultureInfo.InvariantCulture);
    }
}
