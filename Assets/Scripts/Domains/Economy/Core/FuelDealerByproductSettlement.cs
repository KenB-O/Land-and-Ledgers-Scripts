// Land & Ledgers — sawmill byproduct settlement (owner Decision 1, D-F3).
//
// The sawmill-to-dealer byproduct transfer is a PURCHASE, not a gift of
// custody: the dealer pays the mill 1c per intake unit (Decision 1,
// provisional calibration, before transport and processing). The custody
// transfer and the payment are one transaction — if the dealer cannot pay,
// the transfer is refused and nothing moves; the fuel stays at the mill.
//
// This helper owns the payment half of that transaction. The caller
// (SimulationDrivers.TransferSawmillByproductsToFuelDealer) owns the
// custody half: it checks affordability BEFORE moving any lots, moves the
// custody, then settles on the actual before/after intake delta through
// this helper. The payment settles on the measured delta, never on an
// estimate, so a lot that failed to transfer is never paid for.
//
// Cash movement is atomic per port: IBusinessCashPort.TryDebit refuses
// outright when the dealer is short, moving nothing; Credit cannot fail
// for a non-negative amount. If the mill credit is refused after the
// dealer debit succeeded (a port defect, not a balance problem), the
// refusal says so loudly and the dealer is NOT re-debited — the custody
// already moved, so the books must be reconciled, never silently re-paid.

using System.Collections.Generic;
using LandLedgers.Economy.Businesses.GeneralStore;

namespace LandLedgers.Economy
{
    public static class FuelDealerByproductSettlement
    {
        public sealed class SettlementResult
        {
            public int IntakeUnits;
            public int PriceCents;
            public bool Settled;
            public string Refusal = string.Empty;
        }

        // Settles the D-F3 payment for intakeUnits of transferred byproduct:
        // debits the dealer's cash and credits the mill the same amount.
        // Returns a result naming the refusal when nothing was settled.
        // Zero units settle vacuously (nothing moved, nothing owed).
        public static SettlementResult Settle(
            int intakeUnits,
            IBusinessCashPort dealerCash,
            IBusinessCashPort millCash,
            List<string> diag)
        {
            diag = diag ?? new List<string>();
            var result = new SettlementResult { IntakeUnits = intakeUnits > 0 ? intakeUnits : 0 };

            if (result.IntakeUnits <= 0)
            {
                result.Settled = true;
                return result;
            }

            if (dealerCash == null)
            {
                result.Refusal = "no dealer cash port: byproduct payment cannot settle; refuse the transfer, do not move fuel unpaid.";
                diag.Add("[ByproductSettlement] " + result.Refusal);
                return result;
            }

            if (millCash == null)
            {
                result.Refusal = "no mill cash port: byproduct payment cannot settle; refuse the transfer, do not move fuel unpaid.";
                diag.Add("[ByproductSettlement] " + result.Refusal);
                return result;
            }

            result.PriceCents = FuelConversionRates.ByproductPriceCents(result.IntakeUnits);
            string debitProblem = dealerCash.TryDebit(
                result.PriceCents,
                "sawmill byproduct purchase " + result.IntakeUnits + "u @ 1c/u (D-F3)");
            if (debitProblem != null)
            {
                result.Refusal = "dealer cannot pay " + result.PriceCents + "c for "
                    + result.IntakeUnits + "u of byproduct (" + debitProblem
                    + ") — no money moved, fuel stays at the mill.";
                diag.Add("[ByproductSettlement] " + result.Refusal);
                return result;
            }

            string creditProblem = millCash.Credit(
                result.PriceCents,
                "sawmill byproduct sale " + result.IntakeUnits + "u @ 1c/u (D-F3)");
            if (creditProblem != null)
            {
                result.Refusal = "mill credit refused after the dealer debit succeeded ("
                    + creditProblem + ") — dealer was debited " + result.PriceCents
                    + "c; reconcile the books, do not re-debit.";
                diag.Add("[ByproductSettlement] " + result.Refusal);
                return result;
            }

            result.Settled = true;
            diag.Add("[ByproductSettlement] settled " + result.PriceCents + "c for "
                + result.IntakeUnits + "u of sawmill byproduct @ 1c/u (D-F3).");
            return result;
        }
    }
}
