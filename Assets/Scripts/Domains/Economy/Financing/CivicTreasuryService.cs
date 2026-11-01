using System;
using System.Collections.Generic;

namespace LandLedgers.Civic
{
    /// <summary>
    /// Pass IX WS6 (A1-11/A1-07): kind of a civic treasury ledger entry.
    /// APPEND-ONLY — never renumber; new kinds are added at the end only.
    /// </summary>
    public enum CivicLedgerEntryKind
    {
        Unspecified = 0,
        TaxReceived = 1,
        TeacherPayroll = 2,
        CivicPayroll = 3,
        OtherCredit = 4,
        OtherDebit = 5,
    }

    /// <summary>Pass IX WS6: one dated, append-only civic treasury ledger entry.</summary>
    [Serializable]
    public sealed class CivicTreasuryLedgerEntry
    {
        public CivicLedgerEntryKind Kind = CivicLedgerEntryKind.Unspecified;
        public int AmountCents;
        public int DayIndex;
        public int BalanceAfterCents;
        public string Note = string.Empty;

        public CivicTreasuryLedgerEntry() { }
    }

    /// <summary>
    /// Pass IX WS6 (A1-11): the ONE civic cash store — the public recipient
    /// Canon requires for property taxes (A1-07) and the fund that pays the
    /// schoolhouse teacher (A1-10). Instance store owned by
    /// SimulationSystemsHub (never a static), following the existing
    /// single-writer cash patterns: the balance changes only through
    /// TryCredit/TryDebit here, a debit refuses when funds are insufficient,
    /// and the balance can never go negative. Every movement is recorded as
    /// a dated ledger entry so tax cents conserve end to end and can be
    /// audited: sum(credits) - sum(debits) == BalanceCents.
    ///
    /// Placement note: the type lives with the finance authorities
    /// (Domains/Economy/Financing) so the Roslyn harness — which does not
    /// compile Domains/Civic — can exercise it and every leg that touches
    /// it; its namespace keeps civic ownership (LandLedgers.Civic).
    /// </summary>
    public sealed class CivicTreasuryService
    {
        private int balanceCents;
        private readonly List<CivicTreasuryLedgerEntry> ledger = new List<CivicTreasuryLedgerEntry>();
        private readonly List<string> diagnostics = new List<string>();

        public int BalanceCents => Math.Max(0, balanceCents);
        public IReadOnlyList<CivicTreasuryLedgerEntry> Ledger => ledger;
        public IReadOnlyList<string> Diagnostics => diagnostics;

        /// <summary>Credits the treasury. Refuses non-positive amounts.</summary>
        public bool TryCredit(int amountCents, CivicLedgerEntryKind kind, int dayIndex, string note, List<string> diag = null)
        {
            diag = diag ?? diagnostics;
            if (amountCents <= 0)
            {
                diag.Add($"CivicTreasury.TryCredit: amount must be positive (got {amountCents}c) — refused.");
                return false;
            }
            balanceCents = checked(balanceCents + amountCents);
            ledger.Add(new CivicTreasuryLedgerEntry
            {
                Kind = kind,
                AmountCents = amountCents,
                DayIndex = dayIndex,
                BalanceAfterCents = balanceCents,
                Note = note ?? string.Empty,
            });
            return true;
        }

        /// <summary>
        /// Debits the treasury. Refuses when the balance cannot cover the
        /// full amount — the caller routes any shortfall to a named Payable
        /// through FinancialObligationAuthority, never fictional cash.
        /// </summary>
        public bool TryDebit(int amountCents, CivicLedgerEntryKind kind, int dayIndex, string note, List<string> diag = null)
        {
            diag = diag ?? diagnostics;
            if (amountCents <= 0)
            {
                diag.Add($"CivicTreasury.TryDebit: amount must be positive (got {amountCents}c) — refused.");
                return false;
            }
            if (amountCents > balanceCents)
            {
                diag.Add($"CivicTreasury.TryDebit: treasury holds {balanceCents}c, cannot debit {amountCents}c — refused, balance unchanged.");
                return false;
            }
            balanceCents -= amountCents;
            ledger.Add(new CivicTreasuryLedgerEntry
            {
                Kind = kind,
                AmountCents = -amountCents,
                DayIndex = dayIndex,
                BalanceAfterCents = balanceCents,
                Note = note ?? string.Empty,
            });
            return true;
        }

        /// <summary>Total credited for one entry kind (diagnostics/audit).</summary>
        public int TotalForKind(CivicLedgerEntryKind kind)
        {
            int total = 0;
            foreach (CivicTreasuryLedgerEntry entry in ledger)
                if (entry != null && entry.Kind == kind) total += entry.AmountCents;
            return total;
        }

        #region Save / Load

        [Serializable]
        public sealed class CivicTreasurySaveDto
        {
            public int BalanceCents;
            public List<CivicTreasuryLedgerEntry> Ledger = new List<CivicTreasuryLedgerEntry>();
        }

        public CivicTreasurySaveDto CaptureSaveDto()
        {
            return new CivicTreasurySaveDto
            {
                BalanceCents = BalanceCents,
                Ledger = new List<CivicTreasuryLedgerEntry>(ledger),
            };
        }

        /// <summary>Clear-then-load, matching the hub's other authorities.</summary>
        public void LoadFromSaveDto(CivicTreasurySaveDto dto)
        {
            ledger.Clear();
            balanceCents = 0;
            if (dto == null) return;
            balanceCents = Math.Max(0, dto.BalanceCents);
            if (dto.Ledger != null)
            {
                foreach (CivicTreasuryLedgerEntry entry in dto.Ledger)
                    if (entry != null) ledger.Add(entry);
            }
        }

        #endregion
    }
}
