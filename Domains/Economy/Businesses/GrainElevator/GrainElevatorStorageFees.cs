using EntityId = LandLedgers.Primitives.EntityId;

using System;
using System.Collections.Generic;
using LandLedgers.Primitives;
using UnityEngine;

namespace LandLedgers.Economy.Businesses.GrainElevator
{
    /// <summary>
    /// D2F: which kind of storage-fee charge a ledger line is. All rates are
    /// POLICY DATA (Canon R6 §6 calibration hold — exact storage charges are
    /// not locked historically), so the kinds exist as data, not canon.
    /// </summary>
    public enum GrainElevatorFeeChargeKind
    {
        PeriodAccrual = 0, // a closed 30-day storage period on custody grain
        IntakeHandling = 1, // receiving charge on custody intake (policy data)
        OutturnHandling = 2, // shipping charge on release (policy data)
        Adjustment = 3, // manual correction — recorded loudly, never silent
    }

    /// <summary>
    /// D2F: one storage-fee charge against a NAMED customer — the fee side
    /// of the custody obligation. Money moves only through ledger
    /// authorities; this runtime computes and records what is owed, never
    /// posts it. Charges reference the stored lot and the policy rate so
    /// every cent traces to a real bin and a named storer.
    /// </summary>
    [Serializable]
    public sealed class GrainElevatorFeeCharge
    {
        public EntityId ChargeId = EntityId.Invalid; // EntityKind.Lot (HF-1)
        public GrainElevatorFeeChargeKind Kind = GrainElevatorFeeChargeKind.PeriodAccrual;
        public string CustomerId = string.Empty;
        public string CustomerName = string.Empty;
        public string ElevatorLotId = string.Empty; // the stored custody lot this charge names
        public int Units;
        public int Periods; // period accruals only
        public int RateCentsPerUnit;
        public int AmountCents;
        public int DayIndex;
        public string Note = string.Empty;

        public GrainElevatorFeeCharge() { }
    }

    /// <summary>
    /// D2F: one storage-fee payment from a NAMED customer. The ledger
    /// authority posts the money; the runtime only records the payment
    /// against the customer's fee account.
    /// </summary>
    [Serializable]
    public sealed class GrainElevatorFeePayment
    {
        public EntityId PaymentId = EntityId.Invalid; // EntityKind.Lot (HF-1)
        public string CustomerId = string.Empty;
        public string CustomerName = string.Empty;
        public int AmountCents;
        public int DayIndex;
        public string Note = string.Empty;

        public GrainElevatorFeePayment() { }
    }

    /// <summary>
    /// D2F: the elevator's storage-fee ledger — per-customer fee accounts.
    /// Custody fees accrue to the NAMED storer (canon bailment: the fee is
    /// owed by the owner of the stored grain, never by the elevator to
    /// itself). The ledger records charges and payments honestly; the money
    /// itself is posted by ledger authorities elsewhere.
    ///
    /// OPEN FORK (not guessed): whether an elevator may withhold release of
    /// bailment grain for unpaid storage. Period warehousemen's liens are
    /// historically real, but canon says "exact court procedure, lien
    /// priority, redemption and filing law remain dated research," so the
    /// enforcement remedy is NOT settled doctrine. The runtime tracks the
    /// receivable honestly and exposes it; the
    /// GrainElevatorStoragePolicy.WithholdReleaseForUnpaidFees flag is an
    /// explicit fork parameter (default false — no self-help seizure by
    /// default), and the release decision stays with the caller.
    /// </summary>
    public sealed class GrainElevatorFeeLedger
    {
        private readonly List<string> diagnostics = new List<string>();
        private readonly List<GrainElevatorFeeCharge> charges = new List<GrainElevatorFeeCharge>();
        private readonly List<GrainElevatorFeePayment> payments = new List<GrainElevatorFeePayment>();

        public IReadOnlyList<string> Diagnostics => diagnostics;
        public IReadOnlyList<GrainElevatorFeeCharge> Charges => charges;
        public IReadOnlyList<GrainElevatorFeePayment> Payments => payments;

        /// <summary>Posts a fee charge to a named customer's account. Returns the charge, or null on refusal.</summary>
        public GrainElevatorFeeCharge PostCharge(
            EntityIdRegistry idRegistry, string customerId, string customerName,
            string elevatorLotId, GrainElevatorFeeChargeKind kind,
            int units, int periods, int rateCentsPerUnit, int amountCents,
            int dayIndex, string note, List<string> diag)
        {
            diag = diag ?? diagnostics;
            if (idRegistry == null)
            {
                diag.Add("GrainElevatorFeeLedger.PostCharge: no id registry — charge refused.");
                return null;
            }
            if (string.IsNullOrWhiteSpace(customerId))
            {
                diag.Add("GrainElevatorFeeLedger.PostCharge: a fee charge needs a NAMED customer — anonymous charges are refused.");
                return null;
            }
            if (amountCents < 0)
            {
                diag.Add("GrainElevatorFeeLedger.PostCharge: negative charges are refused — use a payment or an Adjustment with a note.");
                return null;
            }
            var charge = new GrainElevatorFeeCharge
            {
                ChargeId = idRegistry.Allocate(EntityKind.Lot),
                Kind = kind,
                CustomerId = customerId.Trim(),
                CustomerName = customerName ?? string.Empty,
                ElevatorLotId = elevatorLotId ?? string.Empty,
                Units = Math.Max(0, units),
                Periods = Math.Max(0, periods),
                RateCentsPerUnit = Math.Max(0, rateCentsPerUnit),
                AmountCents = amountCents,
                DayIndex = dayIndex,
                Note = note ?? string.Empty,
            };
            charges.Add(charge);
            diag.Add($"GrainElevatorFeeLedger: {kind} charge {charge.ChargeId} — "
                + $"{amountCents}c to {charge.CustomerName} ({charge.CustomerId}) for lot {charge.ElevatorLotId} on day {dayIndex}."
                + (string.IsNullOrWhiteSpace(charge.Note) ? string.Empty : $" {charge.Note}"));
            return charge;
        }

        /// <summary>Records a fee payment from a named customer (the ledger authority posts the money).</summary>
        public GrainElevatorFeePayment RecordPayment(
            EntityIdRegistry idRegistry, string customerId, string customerName,
            int amountCents, int dayIndex, string note, List<string> diag)
        {
            diag = diag ?? diagnostics;
            if (idRegistry == null)
            {
                diag.Add("GrainElevatorFeeLedger.RecordPayment: no id registry — payment refused.");
                return null;
            }
            if (string.IsNullOrWhiteSpace(customerId))
            {
                diag.Add("GrainElevatorFeeLedger.RecordPayment: a fee payment needs a NAMED customer.");
                return null;
            }
            if (amountCents <= 0)
            {
                diag.Add("GrainElevatorFeeLedger.RecordPayment: payment must be positive.");
                return null;
            }
            var payment = new GrainElevatorFeePayment
            {
                PaymentId = idRegistry.Allocate(EntityKind.Lot),
                CustomerId = customerId.Trim(),
                CustomerName = customerName ?? string.Empty,
                AmountCents = amountCents,
                DayIndex = dayIndex,
                Note = note ?? string.Empty,
            };
            payments.Add(payment);
            int balance = UnpaidBalanceCents(customerId);
            diag.Add($"GrainElevatorFeeLedger: payment {payment.PaymentId} — "
                + $"{amountCents}c from {payment.CustomerName} ({payment.CustomerId}) on day {dayIndex}; "
                + $"unpaid balance now {balance}c."
                + (balance < 0 ? " Overpayment — the customer holds a credit." : string.Empty));
            return payment;
        }

        /// <summary>Unpaid storage-fee balance for one customer: charges minus payments. Negative = credit.</summary>
        public int UnpaidBalanceCents(string customerId)
        {
            if (string.IsNullOrWhiteSpace(customerId)) return 0;
            string id = customerId.Trim();
            int balance = 0;
            foreach (var charge in charges)
            {
                if (string.Equals(charge.CustomerId, id, StringComparison.Ordinal))
                    balance += charge.AmountCents;
            }
            foreach (var payment in payments)
            {
                if (string.Equals(payment.CustomerId, id, StringComparison.Ordinal))
                    balance -= payment.AmountCents;
            }
            return balance;
        }

        public bool HasUnpaidFees(string customerId)
        {
            return UnpaidBalanceCents(customerId) > 0;
        }

        public int TotalUnpaidCents()
        {
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var charge in charges) seen.Add(charge.CustomerId);
            foreach (var payment in payments) seen.Add(payment.CustomerId);
            int total = 0;
            foreach (var id in seen) total += UnpaidBalanceCents(id);
            return total;
        }

        public List<GrainElevatorFeeCharge> ChargesForCustomer(string customerId)
        {
            var result = new List<GrainElevatorFeeCharge>();
            if (string.IsNullOrWhiteSpace(customerId)) return result;
            string id = customerId.Trim();
            foreach (var charge in charges)
            {
                if (string.Equals(charge.CustomerId, id, StringComparison.Ordinal)) result.Add(charge);
            }
            return result;
        }

        /// <summary>D2F save contract: lives inside the owning ledger class.</summary>
        [Serializable]
        public sealed class GrainElevatorFeeLedgerSaveDto
        {
            public List<GrainElevatorFeeCharge> Charges = new List<GrainElevatorFeeCharge>();
            public List<GrainElevatorFeePayment> Payments = new List<GrainElevatorFeePayment>();
        }

        public GrainElevatorFeeLedgerSaveDto CaptureSaveDto()
        {
            var dto = new GrainElevatorFeeLedgerSaveDto();
            foreach (var charge in charges)
            {
                dto.Charges.Add(new GrainElevatorFeeCharge
                {
                    ChargeId = charge.ChargeId,
                    Kind = charge.Kind,
                    CustomerId = charge.CustomerId,
                    CustomerName = charge.CustomerName,
                    ElevatorLotId = charge.ElevatorLotId,
                    Units = charge.Units,
                    Periods = charge.Periods,
                    RateCentsPerUnit = charge.RateCentsPerUnit,
                    AmountCents = charge.AmountCents,
                    DayIndex = charge.DayIndex,
                    Note = charge.Note,
                });
            }
            foreach (var payment in payments)
            {
                dto.Payments.Add(new GrainElevatorFeePayment
                {
                    PaymentId = payment.PaymentId,
                    CustomerId = payment.CustomerId,
                    CustomerName = payment.CustomerName,
                    AmountCents = payment.AmountCents,
                    DayIndex = payment.DayIndex,
                    Note = payment.Note,
                });
            }
            return dto;
        }

        public void LoadFromSaveDto(GrainElevatorFeeLedgerSaveDto dto)
        {
            charges.Clear();
            payments.Clear();
            if (dto == null) return;
            if (dto.Charges != null)
            {
                foreach (var charge in dto.Charges)
                {
                    if (charge == null) continue;
                    charges.Add(charge);
                }
            }
            if (dto.Payments != null)
            {
                foreach (var payment in dto.Payments)
                {
                    if (payment == null) continue;
                    payments.Add(payment);
                }
            }
        }
    }
}
