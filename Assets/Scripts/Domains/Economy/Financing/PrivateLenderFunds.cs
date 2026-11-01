using System;
using System.Collections.Generic;
using UnityEngine;

namespace LandLedgers.Economy.Financing
{
    /// <summary>
    /// NX-3B: one commitment of a private lender's own capital to a loan.
    /// </summary>
    [Serializable]
    public sealed class LenderCommitment
    {
        public string CommitmentId = string.Empty;
        public string InstrumentId = string.Empty; // the T2A instrument this funds
        public int AmountCents;
        public int CommittedDayIndex;
        public bool Released;

        public LenderCommitment() { }
    }

    /// <summary>
    /// NX-3B: a private lender's balance sheet. T2A named lenders as
    /// counterparties but gave them no funds — a lender could lend what they
    /// never had. Historical (1870s): private bankers and moneylenders lent
    /// their OWN capital (and sometimes their depositors'); there was no
    /// fractional-reserve invention for a private individual. This ledger
    /// makes that honest: commitments cannot exceed capital, and every
    /// commitment names the instrument it funds.
    ///
    /// This is deliberately NOT a bank: no deposit-taking, no money creation —
    /// just a named person's finite purse.
    /// </summary>
    public sealed class PrivateLenderFunds
    {
        public string LenderName = string.Empty;
        public int CapitalCents;

        private readonly List<LenderCommitment> commitments = new List<LenderCommitment>();
        private readonly List<string> diagnostics = new List<string>();

        public IReadOnlyList<string> Diagnostics => diagnostics;
        public IReadOnlyList<LenderCommitment> Commitments => commitments;

        public PrivateLenderFunds() { }
        public PrivateLenderFunds(string lenderName, int capitalCents)
        {
            LenderName = lenderName ?? string.Empty;
            CapitalCents = Math.Max(0, capitalCents);
        }

        /// <summary>Capital currently lent out and not yet released.</summary>
        public int CommittedCents()
        {
            int total = 0;
            foreach (LenderCommitment c in commitments)
                if (!c.Released) total += c.AmountCents;
            return total;
        }

        public int AvailableCents() => CapitalCents - CommittedCents();

        /// <summary>
        /// Commits funds to a loan. Refuses when the lender lacks the funds —
        /// a private lender cannot lend what they do not have.
        /// </summary>
        public string CommitFunds(string commitmentId, string instrumentId, int amountCents,
            int dayIndex, List<string> diag = null)
        {
            diag = diag ?? diagnostics;
            if (string.IsNullOrWhiteSpace(LenderName))
                return "PrivateLenderFunds.CommitFunds: the lender must be named — no anonymous lenders.";
            if (amountCents <= 0) return "PrivateLenderFunds.CommitFunds: the commitment must be positive.";
            if (string.IsNullOrWhiteSpace(instrumentId))
                return "PrivateLenderFunds.CommitFunds: the funded instrument must be named.";
            if (amountCents > AvailableCents())
            {
                diag.Add($"PrivateLenderFunds [{LenderName}]: REFUSED — asked to commit {amountCents}c " +
                    $"but only {AvailableCents()}c of {CapitalCents}c capital is free. Lenders lend their own capital.");
                return $"PrivateLenderFunds.CommitFunds: '{LenderName}' has {AvailableCents()}c available — cannot commit {amountCents}c.";
            }
            commitments.Add(new LenderCommitment
            {
                CommitmentId = string.IsNullOrWhiteSpace(commitmentId) ? $"commit-{dayIndex}-{commitments.Count}" : commitmentId,
                InstrumentId = instrumentId,
                AmountCents = amountCents,
                CommittedDayIndex = dayIndex,
            });
            diag.Add($"PrivateLenderFunds [{LenderName}]: committed {amountCents}c to '{instrumentId}' — {AvailableCents()}c still free.");
            return null;
        }

        /// <summary>Releases a commitment when the underlying loan is satisfied.</summary>
        public string ReleaseCommitment(string instrumentId, List<string> diag = null)
        {
            diag = diag ?? diagnostics;
            foreach (LenderCommitment c in commitments)
            {
                if (!c.Released && string.Equals(c.InstrumentId, instrumentId, StringComparison.Ordinal))
                {
                    c.Released = true;
                    diag.Add($"PrivateLenderFunds [{LenderName}]: released {c.AmountCents}c from '{instrumentId}' — {AvailableCents()}c free.");
                    return null;
                }
            }
            return $"PrivateLenderFunds.ReleaseCommitment: no open commitment funds '{instrumentId}'.";
        }

        /// <summary>
        /// The lender injects more of their own capital (inheritance, profits
        /// moved in, sale proceeds) — always from a named source, never
        /// conjured. The caller moves the actual money; this records it.
        /// </summary>
        public string AddCapital(int amountCents, string sourceNote, List<string> diag = null)
        {
            diag = diag ?? diagnostics;
            if (amountCents <= 0) return "PrivateLenderFunds.AddCapital: the amount must be positive.";
            if (string.IsNullOrWhiteSpace(sourceNote))
                return "PrivateLenderFunds.AddCapital: the capital source must be named — capital is never conjured.";
            CapitalCents += amountCents;
            diag.Add($"PrivateLenderFunds [{LenderName}]: +{amountCents}c capital ({sourceNote}) — capital now {CapitalCents}c.");
            return null;
        }

        #region Save / Load
        [Serializable]
        public sealed class PrivateLenderFundsSaveDto
        {
            public string LenderName = string.Empty;
            public int CapitalCents;
            public List<LenderCommitment> Commitments = new List<LenderCommitment>();
        }

        public PrivateLenderFundsSaveDto CaptureSaveDto()
        {
            return new PrivateLenderFundsSaveDto
            {
                LenderName = LenderName ?? string.Empty,
                CapitalCents = Math.Max(0, CapitalCents),
                Commitments = new List<LenderCommitment>(commitments),
            };
        }

        public void LoadFromSaveDto(PrivateLenderFundsSaveDto dto)
        {
            commitments.Clear();
            diagnostics.Clear();
            if (dto == null) return;
            LenderName = dto.LenderName ?? string.Empty;
            CapitalCents = Math.Max(0, dto.CapitalCents);
            if (dto.Commitments != null)
                foreach (LenderCommitment c in dto.Commitments)
                    if (c != null && c.AmountCents > 0)
                        commitments.Add(c);
        }
        #endregion
    }
}
