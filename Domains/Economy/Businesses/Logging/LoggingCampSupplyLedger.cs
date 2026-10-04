using System;
using System.Collections.Generic;
using UnityEngine;

namespace LandLedgers.Economy.Businesses.Logging
{
    /// <summary>
    /// D2D: one explicit delivery of camp provisions. Upstream-provenance
    /// doctrine (cf. FVS): provisions never arrive anonymously — every
    /// delivery names its supplier, or names an explicit bootstrap
    /// endowment. Remote-camp supply is a canon merchant function
    /// (Canon: a store "can simultaneously lead workwear, trail grocery
    /// staples and barely participate in remote-camp supply").
    /// </summary>
    [Serializable]
    public sealed class LoggingCampSupplyDelivery
    {
        public int DayIndex;
        public string SupplierId = string.Empty; // named supplier, or "bootstrap-endowment"
        public float ProvisionUnits;
        public string Notes = string.Empty; // e.g. "weekly pack-train from the general store"

        public LoggingCampSupplyDelivery() { }
    }

    /// <summary>
    /// D2D: the camp's provision stock — the food that keeps the crew
    /// working. Append-only delivery record with named provenance; shortfalls
    /// are refused loudly and NEVER auto-ordered (the camp does not conjure
    /// supplies; the operation must arrange a real delivery).
    ///
    /// One provision-unit is defined as one crew member's one day of rations
    /// (see LoggingCampParameters.ProvisionsPerHeadPerDay) — an authored
    /// calibration unit, not a historical claim.
    /// </summary>
    public sealed class LoggingCampSupplyLedger
    {
        /// <summary>
        /// The explicit bootstrap marker: provisions the camp opens with that
        /// are recorded as an endowment rather than traced to a supplier.
        /// </summary>
        public const string BootstrapEndowmentSupplierId = "bootstrap-endowment";

        private readonly List<LoggingCampSupplyDelivery> deliveries =
            new List<LoggingCampSupplyDelivery>();
        private float balance;
        private readonly List<string> diagnostics = new List<string>();

        public IReadOnlyList<string> Diagnostics => diagnostics;
        public IReadOnlyList<LoggingCampSupplyDelivery> Deliveries => deliveries;
        public float Balance => balance;

        /// <summary>
        /// Records an explicit provision delivery. Anonymous suppliers and
        /// non-positive quantities are refused loudly — a refusal touches
        /// nothing.
        /// </summary>
        public string RecordDelivery(string supplierId, float provisionUnits,
            int dayIndex, string notes, List<string> diag)
        {
            diag = diag ?? diagnostics;
            if (string.IsNullOrWhiteSpace(supplierId))
            {
                diag.Add("LoggingCampSupplyLedger: DELIVERY REFUSED — no supplier named. " +
                    "Provisions never arrive anonymously; name the supplier or record an explicit " +
                    $"'{BootstrapEndowmentSupplierId}'.");
                return "anonymous-supplier";
            }
            if (provisionUnits <= 0f)
            {
                diag.Add($"LoggingCampSupplyLedger: DELIVERY REFUSED — {provisionUnits} provision units " +
                    "from '" + supplierId + "' is not a delivery.");
                return "non-positive-quantity";
            }

            deliveries.Add(new LoggingCampSupplyDelivery
            {
                DayIndex = dayIndex,
                SupplierId = supplierId,
                ProvisionUnits = provisionUnits,
                Notes = notes ?? string.Empty,
            });
            balance += provisionUnits;
            diag.Add($"LoggingCampSupplyLedger: +{provisionUnits} provisions from '{supplierId}' " +
                $"(day {dayIndex}) — balance now {balance}.");
            return null;
        }

        /// <summary>
        /// Consumes provisions for the crew's day. Insufficient stock is
        /// refused LOUDLY — the camp never auto-orders; the balance is never
        /// driven negative. Returns null on success, a refusal otherwise.
        /// </summary>
        public string Consume(float provisionUnits, int dayIndex, List<string> diag)
        {
            diag = diag ?? diagnostics;
            if (provisionUnits <= 0f) return null; // nothing to consume
            if (balance < provisionUnits)
            {
                diag.Add($"LoggingCampSupplyLedger: CONSUMPTION REFUSED — {provisionUnits} provisions " +
                    $"wanted for day {dayIndex}, only {balance} on hand. The crew goes unfed; " +
                    "no delivery is auto-ordered.");
                return "insufficient-provisions";
            }
            balance -= provisionUnits;
            diag.Add($"LoggingCampSupplyLedger: -{provisionUnits} provisions consumed (day {dayIndex}) — " +
                $"balance now {balance}.");
            return null;
        }

        /// <summary>D2D save contract: lives inside the owning ledger class.</summary>
        [Serializable]
        public sealed class LoggingCampSupplyLedgerSaveDto
        {
            public float Balance;
            public List<LoggingCampSupplyDelivery> Deliveries = new List<LoggingCampSupplyDelivery>();
        }

        public LoggingCampSupplyLedgerSaveDto CaptureSaveDto()
        {
            var dto = new LoggingCampSupplyLedgerSaveDto { Balance = balance };
            foreach (var delivery in deliveries)
            {
                if (delivery == null) continue;
                dto.Deliveries.Add(new LoggingCampSupplyDelivery
                {
                    DayIndex = delivery.DayIndex,
                    SupplierId = delivery.SupplierId,
                    ProvisionUnits = delivery.ProvisionUnits,
                    Notes = delivery.Notes,
                });
            }
            return dto;
        }

        public void LoadFromSaveDto(LoggingCampSupplyLedgerSaveDto dto)
        {
            deliveries.Clear();
            balance = 0f;
            if (dto == null) return;
            balance = Math.Max(0f, dto.Balance);
            if (dto.Deliveries != null)
            {
                foreach (var delivery in dto.Deliveries)
                {
                    if (delivery == null) continue;
                    deliveries.Add(delivery);
                }
            }
        }
    }
}
