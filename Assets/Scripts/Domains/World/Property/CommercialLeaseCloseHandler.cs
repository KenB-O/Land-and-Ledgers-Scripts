using System;
using System.Collections.Generic;
using LandLedgers.Economy.Financing;
using LandLedgers.Population;

namespace LandLedgers.World.Property
{
    /// <summary>
    /// B3: the harness-compiled half of the closed-business premises
    /// release. B2 wired the release into SimulationDrivers, which the
    /// harness cannot compile, so the decision logic lived where no
    /// source test could reach it. This handler carries the exact B2
    /// behaviour — same order, same diagnostics, same decisions — with
    /// the runtime, market and ledgers supplied as injected ports, and
    /// SimulationDrivers is a thin adapter that resolves the ids and
    /// passes the production ports. Behaviour preserved verbatim from
    /// SimulationDrivers.ReleaseClosedBusinessPremises /
    /// RefundLeaseDeposit (B2); nothing here changes a decision, only
    /// where the decision is written.
    /// </summary>
    public sealed class CommercialLeaseCloseHandler
    {
        /// <summary>Ports the handler needs; the adapter binds them to production services.</summary>
        public sealed class ClosePorts
        {
            public CommercialLeaseMarket Market;
            public Func<int, string> ReleasePremises;
            public Func<int, HouseholdLedger> LedgerByPersonId;
            public Func<int, HouseholdLedger> LedgerByHouseholdId;
            public FinancialObligationAuthority Obligations;
        }

        /// <summary>
        /// Runs the B2 close sequence for one closed business: release
        /// the premises, then either end the active lease (with the
        /// deposit refund) or re-list the building with the founder as
        /// landlord. A refused release stops the sequence and the lease
        /// is NOT ended silently; every outcome is appended to diag.
        /// </summary>
        public void HandleClosedBusiness(string businessInstanceId, int buildingId, int plotId,
            int founderPersonId, string founderName, ClosePorts ports, int dayIndex, List<string> diag)
        {
            if (diag == null) diag = new List<string>();
            if (buildingId < 0)
            {
                diag.Add($"CommercialLease: business '{businessInstanceId}' closed with no assigned building — nothing to release.");
                return;
            }

            if (ports == null || ports.Market == null)
            {
                diag.Add($"CommercialLease: business '{businessInstanceId}' closed but the lease market is missing — building {buildingId} NOT released.");
                return;
            }

            string releaseRefusal = ports.ReleasePremises != null
                ? ports.ReleasePremises(buildingId)
                : $"ReleaseNpcBusinessPremises: no release port for building {buildingId}.";
            if (releaseRefusal != null)
            {
                diag.Add($"CommercialLease: {releaseRefusal}");
                return;
            }

            CommercialLeaseAgreement lease = ports.Market.FindActiveLeaseForBusiness(businessInstanceId);
            if (lease != null)
            {
                ports.Market.EndLease(lease, dayIndex, diag);
                RefundLeaseDeposit(lease, ports, dayIndex, diag);
            }
            else
            {
                ports.Market.ListBuilding(buildingId, plotId, founderPersonId, founderName, dayIndex, diag);
            }
        }

        /// <summary>
        /// B2: refunds a lease deposit on a clean lease end. The deposit
        /// is refunded only when no rent Payable on the lease is still
        /// unsettled; otherwise it is held and the reason is logged —
        /// the offset against arrears is a follow-up, not a silent loss.
        /// </summary>
        public void RefundLeaseDeposit(CommercialLeaseAgreement lease, ClosePorts ports, int dayIndex, List<string> diag)
        {
            if (lease == null || lease.DepositPaidCents <= 0) return;
            bool arrearsOutstanding = false;
            if (ports != null && ports.Obligations != null)
            {
                foreach (string payableId in lease.RentPayableIds)
                {
                    FinancialObligation payable = ports.Obligations.Find(payableId);
                    if (payable != null && !payable.Settled) { arrearsOutstanding = true; break; }
                }
            }

            if (arrearsOutstanding)
            {
                diag.Add($"CommercialLease: lease '{lease.AgreementId}' deposit {lease.DepositPaidCents}c HELD — rent arrears are still outstanding on the lease (offset against arrears is a follow-up).");
                return;
            }

            HouseholdLedger landlordLedger = ports != null && ports.LedgerByPersonId != null
                ? ports.LedgerByPersonId(lease.LandlordPersonId) : null;
            HouseholdLedger tenantLedger = ports != null && ports.LedgerByHouseholdId != null
                ? ports.LedgerByHouseholdId(lease.TenantHouseholdId) : null;
            if (landlordLedger == null || tenantLedger == null)
            {
                diag.Add($"CommercialLease: lease '{lease.AgreementId}' deposit {lease.DepositPaidCents}c NOT refunded — " +
                    $"landlord ledger {(landlordLedger == null ? "missing" : "found")} / tenant ledger {(tenantLedger == null ? "missing" : "found")}.");
                return;
            }

            string refundRefusal = landlordLedger.TryTransferTo(tenantLedger, dayIndex,
                lease.DepositPaidCents,
                $"lease deposit refund for {lease.AgreementId} (lease ended)",
                HouseholdIncomeSource.OtherDocumented, lease.AgreementId,
                $"lease deposit refund for {lease.AgreementId}",
                "person:" + lease.LandlordPersonId);
            diag.Add(refundRefusal == null
                ? $"CommercialLease: lease '{lease.AgreementId}' deposit {lease.DepositPaidCents}c refunded to H{lease.TenantHouseholdId}."
                : $"CommercialLease: lease '{lease.AgreementId}' deposit refund FAILED: {refundRefusal}.");
        }
    }
}
