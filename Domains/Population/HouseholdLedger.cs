using System;
using System.Collections.Generic;
using LandLedgers.Primitives;

namespace LandLedgers.Population
{
    /// <summary>
    /// HF-4: every household cash inflow must carry provenance (Canon XIII 13.2 CANON LOCK:
    /// ordinary household cash may not replenish merely because a time threshold passed).
    /// </summary>
    public enum HouseholdIncomeSource
    {
        Unspecified = 0, // never valid on a recorded inflow
        WageEmployment = 1, // via EmploymentRelationship (sourceReference = employment id)
        OwnerDraw = 2, // proprietor draw from owned business profit
        OwnerContribution = 3, // owner injecting personal capital
        SaleProceeds = 4, // sale of goods/assets with a real counterparty
        Remittance = 5, // money sent from outside (kin, etc.)
        LoanProceeds = 6, // borrowed funds with an obligation reference
        OutsideEmployerPayment = 7, // off-map employer, coarse counterparty allowed
        InvestmentReturn = 8,
        BoardingIncome = 9, // boarder/lodging payments received
        ServiceIncome = 10, // contract/service work settlement via agreement
        OtherDocumented = 11, // must still carry a reason
    }

    /// <summary>One household cash movement. Inflows always carry source + reason.</summary>
    [Serializable]
    public sealed class HouseholdLedgerEntry
    {
        public int EntrySequence;
        public int DayIndex;
        public int AmountCents; // signed: positive = inflow, negative = outflow
        public bool IsInflow => AmountCents > 0;
        public HouseholdIncomeSource Source; // required for inflows
        public string SourceReference = string.Empty; // employment id, business id, agreement id...
        public string Reason = string.Empty; // required for inflows (Canon 13.2)
        public string Purpose = string.Empty; // required for outflows
        public string Counterparty = string.Empty;
        public int BalanceAfterCents;
    }

    /// <summary>
    /// HF-4: a household's cash ledger. Balance is DERIVED from entries; inflows without
    /// provenance are rejected, never silently booked (Canon 13.2).
    /// </summary>
    [Serializable]
    public sealed class HouseholdLedgerState
    {
        public int HouseholdId;
        public int NextEntrySequence;
        public List<HouseholdLedgerEntry> Entries = new List<HouseholdLedgerEntry>();
    }

    /// <summary>
    /// HF-4: per-household cash ledger authority. Provenance enforcement lives here:
    /// <see cref="RecordInflow"/> rejects sourceless/unreasoned inflows with a diagnostic.
    /// </summary>
    public sealed class HouseholdLedger
    {
        private readonly HouseholdLedgerState state;
        private readonly List<string> diagnostics = new List<string>();

        public HouseholdLedger(HouseholdLedgerState state)
        {
            this.state = state ?? throw new ArgumentNullException(nameof(state));
        }

        public HouseholdLedger(int householdId)
            : this(new HouseholdLedgerState { HouseholdId = householdId })
        {
        }

        public IReadOnlyList<string> Diagnostics => diagnostics;
        public int HouseholdId => state.HouseholdId;

        /// <summary>
        /// Records a cash inflow. Rejects (returns diagnostic, books nothing) when the
        /// source class or reason is missing — synthetic top-ups are never valid (Canon 13.2).
        /// </summary>
        public string RecordInflow(
            int dayIndex,
            int amountCents,
            HouseholdIncomeSource source,
            string sourceReference,
            string reason,
            string counterparty)
        {
            if (amountCents <= 0)
            {
                return Reject($"Rejected inflow of {amountCents}c: amount must be positive.");
            }

            if (source == HouseholdIncomeSource.Unspecified)
            {
                return Reject("Rejected inflow: source class is Unspecified. Every inflow must name its source class (Canon 13.2).");
            }

            if (string.IsNullOrWhiteSpace(reason))
            {
                return Reject("Rejected inflow: reason is required. Every inflow must carry its economic reason (Canon 13.2).");
            }

            AddEntry(dayIndex, amountCents, source, sourceReference ?? string.Empty, reason, string.Empty, counterparty ?? string.Empty);
            return null;
        }

        /// <summary>
        /// Records a wage payment through an EmploymentRelationship (PKG-6 authority).
        /// The employment id is the provenance reference; agreed terms live on the
        /// relationship, not here.
        /// </summary>
        public string RecordWagePayment(
            int dayIndex,
            string employmentId,
            int personId,
            int amountCents,
            string periodLabel)
        {
            if (string.IsNullOrWhiteSpace(employmentId))
            {
                return Reject("Rejected wage payment: EmploymentRelationship id is required as the provenance reference.");
            }

            return RecordInflow(
                dayIndex,
                amountCents,
                HouseholdIncomeSource.WageEmployment,
                employmentId,
                $"wage {periodLabel} for P{personId} via {employmentId}",
                "employer");
        }

        /// <summary>Records a cash outflow. Purpose is required.</summary>
        public string RecordOutflow(int dayIndex, int amountCents, string purpose, string counterparty)
        {
            if (amountCents <= 0)
            {
                return Reject($"Rejected outflow of {amountCents}c: amount must be positive.");
            }

            if (string.IsNullOrWhiteSpace(purpose))
            {
                return Reject("Rejected outflow: purpose is required.");
            }

            AddEntry(dayIndex, -amountCents, HouseholdIncomeSource.Unspecified, string.Empty, string.Empty, purpose, counterparty ?? string.Empty);
            return null;
        }

        /// <summary>Balance is derived from entries, never stored as competing truth.</summary>
        public int GetBalanceCents()
        {
            int balance = 0;
            for (int i = 0; i < state.Entries.Count; i++)
            {
                balance += state.Entries[i].AmountCents;
            }

            return balance;
        }

        public IReadOnlyList<HouseholdLedgerEntry> Entries => state.Entries;
        public HouseholdLedgerState State => state;

        private void AddEntry(
            int dayIndex,
            int amountCents,
            HouseholdIncomeSource source,
            string sourceReference,
            string reason,
            string purpose,
            string counterparty)
        {
            int balanceAfter = GetBalanceCents() + amountCents;
            state.Entries.Add(new HouseholdLedgerEntry
            {
                EntrySequence = state.NextEntrySequence++,
                DayIndex = dayIndex,
                AmountCents = amountCents,
                Source = source,
                SourceReference = sourceReference,
                Reason = reason,
                Purpose = purpose,
                Counterparty = counterparty,
                BalanceAfterCents = balanceAfter,
            });
        }

        private string Reject(string diagnostic)
        {
            diagnostics.Add(diagnostic);
            return diagnostic;
        }
    }

    /// <summary>Registry of per-household ledgers with save export/import.</summary>
    public sealed class HouseholdLedgerRegistry
    {
        private readonly Dictionary<int, HouseholdLedger> ledgers = new Dictionary<int, HouseholdLedger>();
        private readonly List<string> diagnostics = new List<string>();

        public IReadOnlyList<string> Diagnostics => diagnostics;

        public HouseholdLedger GetOrCreate(int householdId)
        {
            if (!ledgers.TryGetValue(householdId, out HouseholdLedger ledger))
            {
                ledger = new HouseholdLedger(householdId);
                ledgers[householdId] = ledger;
            }

            return ledger;
        }

        public HouseholdLedger Get(int householdId)
        {
            return ledgers.TryGetValue(householdId, out HouseholdLedger ledger) ? ledger : null;
        }

        public void ExportState(List<HouseholdLedgerState> outStates)
        {
            if (outStates == null)
            {
                return;
            }

            foreach (KeyValuePair<int, HouseholdLedger> entry in ledgers)
            {
                outStates.Add(entry.Value.State);
            }
        }

        public void ImportState(IEnumerable<HouseholdLedgerState> inStates)
        {
            if (inStates == null)
            {
                return;
            }

            foreach (HouseholdLedgerState state in inStates)
            {
                if (state == null || state.HouseholdId < 0)
                {
                    continue;
                }

                if (ledgers.ContainsKey(state.HouseholdId))
                {
                    diagnostics.Add($"ImportState: duplicate ledger for H{state.HouseholdId} skipped.");
                    continue;
                }

                state.Entries ??= new List<HouseholdLedgerEntry>();
                ledgers[state.HouseholdId] = new HouseholdLedger(state);
            }
        }
    }

    /// <summary>
    /// HF-4: one family-labor contribution. There are deliberately NO wage fields:
    /// family enterprise labor updates task time, capability and output through the
    /// household/business relationship — never payroll (TECH §4.5, PL-04, PKG-2).
    /// </summary>
    [Serializable]
    public sealed class FamilyLaborContribution
    {
        public int PersonId;
        public int HouseholdId;
        public string BusinessId = string.Empty;
        public string TaskCategory = string.Empty;
        public int DayIndex;
        public int MinutesWorked;
        public int OutputUnits;
        public string OutputUnitLabel = string.Empty;
        public string Notes = string.Empty;
    }

    /// <summary>
    /// HF-4: family-labor accounting. Contributions accumulate task time and output per
    /// person/business; any attempt to attach wage terms is rejected via the PKG-2
    /// taxonomy guard (WorkRelationshipGuard.ValidateNoWageForNonWageKind).
    /// </summary>
    public sealed class FamilyLaborLedger
    {
        private readonly List<FamilyLaborContribution> contributions = new List<FamilyLaborContribution>();
        private readonly List<string> diagnostics = new List<string>();

        public IReadOnlyList<string> Diagnostics => diagnostics;
        public int ContributionCount => contributions.Count;

        /// <summary>
        /// Records family enterprise labor. agreedWageCents must be zero: passing wage
        /// terms for family labor is a taxonomy violation (PL-04).
        /// </summary>
        public string RecordContribution(FamilyLaborContribution contribution, int agreedWageCents)
        {
            if (contribution == null || contribution.PersonId < 0)
            {
                return Reject("Rejected family-labor contribution: null or invalid person.");
            }

            string violation = WorkRelationshipGuard.ValidateNoWageForNonWageKind(
                WorkRelationshipKind.FamilyEnterpriseLabor, agreedWageCents);
            if (violation != null)
            {
                return Reject(violation);
            }

            if (contribution.MinutesWorked < 0 || contribution.OutputUnits < 0)
            {
                return Reject("Rejected family-labor contribution: negative time or output.");
            }

            contributions.Add(contribution);
            return null;
        }

        /// <summary>Total minutes and output units for a person at a business.</summary>
        public void GetTotals(int personId, string businessId, out int totalMinutes, out int totalOutputUnits)
        {
            totalMinutes = 0;
            totalOutputUnits = 0;
            for (int i = 0; i < contributions.Count; i++)
            {
                FamilyLaborContribution contribution = contributions[i];
                if (contribution != null
                    && contribution.PersonId == personId
                    && string.Equals(contribution.BusinessId, businessId, StringComparison.Ordinal))
                {
                    totalMinutes += contribution.MinutesWorked;
                    totalOutputUnits += contribution.OutputUnits;
                }
            }
        }

        public IReadOnlyList<FamilyLaborContribution> Contributions => contributions;

        private string Reject(string diagnostic)
        {
            diagnostics.Add(diagnostic);
            return diagnostic;
        }
    }
}
