using System;
using System.Collections.Generic;
using LandLedgers.Primitives;
using UnityEngine;

namespace LandLedgers.Economy.Businesses.Lawyer
{
    /// <summary>
    /// W9A: the kinds of client matters a law practice handles. Each matter is
    /// a tracked case with real parties — billing always resolves to a named
    /// person, never to an anonymous ledger entry.
    /// </summary>
    public enum LegalMatterKind
    {
        Unspecified = 0,
        DeedDrafting = 1,        // W9B: produces a conveyance instrument (draft, not recorded)
        ContractDrafting = 2,    // W9B: produces a real contract between real parties
        WillDrafting = 3,        // W9B: produces a Will for the NX-3C probate flow
        DisputeRepresentation = 4, // W9B: representation in a T3D property dispute
        TitleAbstract = 5,       // abstract / chain-of-title examination work
        LegalAdvice = 6,         // counsel on licenses, gates, compliance (Canon Part XII)
    }

    /// <summary>W9A: lifecycle of one client matter.</summary>
    public enum MatterStatus
    {
        Unspecified = 0,
        Open = 1,      // work accumulating; unbilled lines may accrue
        Billed = 2,    // all accrued fees converted to invoices (receivables)
        Closed = 3,    // all receivables collected, file done
        Abandoned = 4, // client walked away; unpaid balances still owed, not erased
    }

    /// <summary>
    /// W9A: one client matter. The client is always a real person id; the
    /// counterparty (if any) is a real person id as well. A matter cannot
    /// be opened for nobody — that is how fees stay grounded in the world.
    /// </summary>
    [Serializable]
    public sealed class LegalMatter
    {
        public string MatterId = string.Empty;
        public int ClientPersonId = -1;
        public string ClientName = string.Empty;
        public int CounterpartyPersonId = -1;
        public string CounterpartyName = string.Empty;
        public LegalMatterKind Kind = LegalMatterKind.Unspecified;
        public MatterStatus Status = MatterStatus.Open;
        public string Summary = string.Empty;
        public int OpenedDayIndex;
        public int ClosedDayIndex = -1;

        public LegalMatter() { }
    }

    /// <summary>W9A: one itemized billing line.</summary>
    [Serializable]
    public sealed class FeeLineItem
    {
        public string Description = string.Empty;
        public int Cents;

        public FeeLineItem() { }

        public FeeLineItem(string description, int cents)
        {
            Description = description ?? string.Empty;
            Cents = cents;
        }
    }

    /// <summary>
    /// W9A: one issued fee invoice. An unpaid invoice is a REAL receivable
    /// owed by the named client — Canon Part VI §6.4: a credit sale can create
    /// revenue/receivable without cash. Collecting the receivable is cash but
    /// not a second sale; payments only shrink the balance. The practice never
    /// mints cash by billing: RecordPayment requires real money from the caller
    /// (or their own systems) to arrive alongside the call.
    /// </summary>
    [Serializable]
    public sealed class LegalFeeInvoice
    {
        public string InvoiceId = string.Empty;
        public string MatterId = string.Empty;
        public int ClientPersonId = -1;
        public string ClientName = string.Empty;
        public int IssuedDayIndex;
        public List<FeeLineItem> Lines = new List<FeeLineItem>();
        public int TotalCents;
        public int RetainerAppliedCents;
        public int PaidCents;

        /// <summary>Outstanding receivable in cents; never negative.</summary>
        public int BalanceCents => Math.Max(0, TotalCents - RetainerAppliedCents - PaidCents);

        public LegalFeeInvoice() { }
    }

    /// <summary>
    /// W9A: default retainer requirements and hourly rates. Labeled as scenario
    /// calibration per Canon Part XII §12.2 (exact playtest prices stay
    /// calibration, not canon constants): a practice may replace these freely.
    /// 1870s-western grounding: lawyers worked largely on per-matter flat fees
    /// and modest retainers, not modern billable-hour regimes; defaults reflect
    /// that shape.
    /// </summary>
    [Serializable]
    public sealed class LawyerFeeSchedule
    {
        public int DefaultRetainerCents = 2500;   // $25 default retainer (calibration)
        public int HourlyRateCents = 200;        // $2/hr (calibration)
        public int WillDraftingFlatCents = 500;  // $5 will (calibration)
        public int DeedDraftingFlatCents = 300;  // $3 deed (calibration)
        public int ContractDraftingFlatCents = 400; // $4 contract (calibration)

        public int DefaultRetainerFor(LegalMatterKind kind)
        {
            switch (kind)
            {
                case LegalMatterKind.WillDrafting: return WillDraftingFlatCents;
                case LegalMatterKind.DeedDrafting: return DeedDraftingFlatCents;
                case LegalMatterKind.ContractDrafting: return ContractDraftingFlatCents;
                default: return DefaultRetainerCents;
            }
        }
    }

    /// <summary>
    /// W9A: the physical-office requirements Canon assigns a lawyer/notary:
    /// desk; law/reference books; pen/ink/paper; document files/forms;
    /// seal where required; secure records. A practice without its office
    /// cannot open matters — presence is a precondition, not a bonus.
    /// </summary>
    [Serializable]
    public sealed class LawyerOfficeRequirements
    {
        public bool HasDesk;
        public bool HasLawBooks;
        public bool HasWritingSupplies; // pen/ink/paper
        public bool HasDocumentForms;
        public bool HasSeal;
        public bool HasSecureRecords;

        /// <summary>Human-readable problems; empty means the office is fit.</summary>
        public List<string> Validate()
        {
            var problems = new List<string>();
            if (!HasDesk) problems.Add("No desk.");
            if (!HasLawBooks) problems.Add("No law/reference books.");
            if (!HasWritingSupplies) problems.Add("No pen/ink/paper.");
            if (!HasDocumentForms) problems.Add("No document files/forms.");
            if (!HasSeal) problems.Add("No seal (required where instruments must be sealed).");
            if (!HasSecureRecords) problems.Add("No secure records.");
            return problems;
        }

        public bool IsFit => Validate().Count == 0;
    }

    /// <summary>
    /// W9A: per-instance law practice runtime. Owns client matters, the
    /// retainer trust ledger (retainer money is the client's money held in
    /// trust, NOT earned revenue until applied to an invoice), itemized
    /// fee accrual, invoicing, and receivable collection.
    ///
    /// No-fiat rules enforced here:
    /// - matters open only for real, named client persons;
    /// - fees accrue only to open matters;
    /// - retainer is applied to invoices only, never conjured;
    /// - payments shrink receivables; overpayment is refused, not pocketed;
    /// - abandonment never erases a balance — the client still owes it;
    /// - closing a matter with an unreturned retainer balance reports the
    ///   refund the caller must actually hand back (this service never
    ///   moves cash itself).
    /// </summary>
    public sealed class LawyerPracticeRuntime
    {
        private readonly string businessInstanceId;
        private readonly Dictionary<string, LegalMatter> matters =
            new Dictionary<string, LegalMatter>(StringComparer.Ordinal);
        private readonly Dictionary<string, LegalFeeInvoice> invoices =
            new Dictionary<string, LegalFeeInvoice>(StringComparer.Ordinal);
        private readonly Dictionary<string, List<FeeLineItem>> unbilled =
            new Dictionary<string, List<FeeLineItem>>(StringComparer.Ordinal);
        private readonly Dictionary<string, int> retainerBalances =
            new Dictionary<string, int>(StringComparer.Ordinal); // matterId -> cents held in trust

        private LawyerFeeSchedule feeSchedule = new LawyerFeeSchedule();
        private LawyerOfficeRequirements office = new LawyerOfficeRequirements();
        private readonly List<string> diagnostics = new List<string>();

        public IReadOnlyList<string> Diagnostics => diagnostics;
        public LawyerFeeSchedule FeeSchedule => feeSchedule;
        public LawyerOfficeRequirements Office => office;

        public LawyerPracticeRuntime(string businessInstanceId)
        {
            this.businessInstanceId = businessInstanceId ?? string.Empty;
        }

        public void SetFeeSchedule(LawyerFeeSchedule schedule)
        {
            feeSchedule = schedule ?? new LawyerFeeSchedule();
        }

        public void SetOffice(LawyerOfficeRequirements requirements)
        {
            office = requirements ?? new LawyerOfficeRequirements();
        }

        /// <summary>Opens a matter for a real, named client. Returns the matter, or null on rejection.</summary>
        public LegalMatter OpenMatter(
            EntityIdRegistry ids, int clientPersonId, string clientName,
            LegalMatterKind kind, string summary, int dayIndex,
            int counterpartyPersonId = -1, string counterpartyName = null,
            List<string> diag = null)
        {
            diag = diag ?? diagnostics;
            if (!office.IsFit)
            {
                diag.Add($"Law practice {businessInstanceId}: cannot open a matter — office unfit: {string.Join("; ", office.Validate())}.");
                return null;
            }
            if (clientPersonId < 0)
            {
                diag.Add($"Law practice {businessInstanceId}: matters open only for real clients — no person id given.");
                return null;
            }
            if (string.IsNullOrWhiteSpace(clientName))
            {
                diag.Add($"Law practice {businessInstanceId}: the client must be named.");
                return null;
            }
            if (kind == LegalMatterKind.Unspecified)
            {
                diag.Add($"Law practice {businessInstanceId}: the matter kind must be stated.");
                return null;
            }
            if (counterpartyPersonId >= 0 && string.IsNullOrWhiteSpace(counterpartyName))
            {
                diag.Add($"Law practice {businessInstanceId}: a named counterparty id needs a name.");
                return null;
            }

            var matter = new LegalMatter
            {
                MatterId = ids != null ? ids.Allocate(EntityKind.Contract).ToString() : Guid.NewGuid().ToString("N"),
                ClientPersonId = clientPersonId,
                ClientName = clientName.Trim(),
                CounterpartyPersonId = counterpartyPersonId,
                CounterpartyName = counterpartyName ?? string.Empty,
                Kind = kind,
                Status = MatterStatus.Open,
                Summary = summary ?? string.Empty,
                OpenedDayIndex = dayIndex,
            };
            matters[matter.MatterId] = matter;
            unbilled[matter.MatterId] = new List<FeeLineItem>();
            retainerBalances[matter.MatterId] = 0;
            diag.Add($"Law practice {businessInstanceId}: matter {matter.MatterId} opened — {kind} for '{matter.ClientName}' (day {dayIndex}).");
            return matter;
        }

        /// <summary>
        /// Records retainer money received from the client for a matter. The
        /// caller is responsible for the real cash movement into the practice;
        /// this ledger records only that the trust balance exists.
        /// </summary>
        public string ReceiveRetainer(string matterId, int cents, List<string> diag)
        {
            diag = diag ?? diagnostics;
            if (!matters.TryGetValue(matterId, out LegalMatter matter))
                return $"ReceiveRetainer: unknown matter '{matterId}'.";
            if (matter.Status != MatterStatus.Open)
                return $"ReceiveRetainer: matter '{matterId}' is {matter.Status} — retainer only on open matters.";
            if (cents <= 0)
                return $"ReceiveRetainer: retainer must be positive (got {cents}c).";

            retainerBalances[matterId] = retainerBalances[matterId] + cents;
            diag.Add($"Law practice {businessInstanceId}: {cents}c retainer received from '{matter.ClientName}' on matter {matterId} — trust balance now {retainerBalances[matterId]}c.");
            return null;
        }

        /// <summary>Accrues one itemized fee line to an open matter (unbilled until invoiced).</summary>
        public string AccrueFee(string matterId, string description, int cents, List<string> diag)
        {
            diag = diag ?? diagnostics;
            if (!matters.TryGetValue(matterId, out LegalMatter matter))
                return $"AccrueFee: unknown matter '{matterId}'.";
            if (matter.Status != MatterStatus.Open)
                return $"AccrueFee: matter '{matterId}' is {matter.Status} — fees accrue only to open matters.";
            if (string.IsNullOrWhiteSpace(description))
                return "AccrueFee: the fee line must be described.";
            if (cents <= 0)
                return $"AccrueFee: fee lines must be positive (got {cents}c).";

            unbilled[matterId].Add(new FeeLineItem(description, cents));
            diag.Add($"Law practice {businessInstanceId}: {cents}c accrued — '{description}' on matter {matterId} (unbilled).");
            return null;
        }

        /// <summary>
        /// Converts all unbilled lines on a matter into one invoice. The matter's
        /// trust balance is applied first; the remainder is a real receivable owed
        /// by the client. A matter with nothing unbilled issues nothing.
        /// </summary>
        public LegalFeeInvoice IssueInvoice(EntityIdRegistry ids, string matterId, int dayIndex, List<string> diag)
        {
            diag = diag ?? diagnostics;
            if (!matters.TryGetValue(matterId, out LegalMatter matter))
            {
                diag.Add($"IssueInvoice: unknown matter '{matterId}'.");
                return null;
            }
            if (matter.Status != MatterStatus.Open)
            {
                diag.Add($"IssueInvoice: matter '{matterId}' is {matter.Status} — invoice only from open matters.");
                return null;
            }
            if (unbilled[matterId].Count == 0)
            {
                diag.Add($"Law practice {businessInstanceId}: matter {matterId} has nothing unbilled — no invoice issued.");
                return null;
            }

            int total = 0;
            foreach (var line in unbilled[matterId]) total += line.Cents;

            int retainerAvailable = retainerBalances[matterId];
            int retainerApplied = Math.Min(retainerAvailable, total);
            retainerBalances[matterId] = retainerAvailable - retainerApplied;

            var invoice = new LegalFeeInvoice
            {
                InvoiceId = ids != null ? ids.Allocate(EntityKind.Contract).ToString() : Guid.NewGuid().ToString("N"),
                MatterId = matterId,
                ClientPersonId = matter.ClientPersonId,
                ClientName = matter.ClientName,
                IssuedDayIndex = dayIndex,
                Lines = new List<FeeLineItem>(unbilled[matterId]),
                TotalCents = total,
                RetainerAppliedCents = retainerApplied,
                PaidCents = 0,
            };
            unbilled[matterId].Clear();
            invoices[invoice.InvoiceId] = invoice;
            matter.Status = MatterStatus.Billed;
            diag.Add($"Law practice {businessInstanceId}: invoice {invoice.InvoiceId} issued on matter {matterId} — {total}c billed, {retainerApplied}c from retainer, {invoice.BalanceCents}c receivable from '{matter.ClientName}'.");
            return invoice;
        }

        /// <summary>
        /// Records a real payment against an invoice. The caller performs the
        /// actual cash movement; this call only shrinks the receivable.
        /// Overpayment is refused — the practice does not pocket excess.
        /// </summary>
        public string RecordPayment(string invoiceId, int cents, List<string> diag)
        {
            diag = diag ?? diagnostics;
            if (!invoices.TryGetValue(invoiceId, out LegalFeeInvoice invoice))
                return $"RecordPayment: unknown invoice '{invoiceId}'.";
            if (cents <= 0)
                return $"RecordPayment: payment must be positive (got {cents}c).";
            if (cents > invoice.BalanceCents)
                return $"RecordPayment: {cents}c exceeds the {invoice.BalanceCents}c balance on invoice {invoiceId} — overpayment refused.";

            invoice.PaidCents += cents;
            diag.Add($"Law practice {businessInstanceId}: {cents}c received on invoice {invoiceId} from '{invoice.ClientName}' — balance now {invoice.BalanceCents}c.");
            return null;
        }

        /// <summary>
        /// Closes a matter. Requires every invoice fully paid; any remaining
        /// trust balance is reported as the refund the caller must actually
        /// return to the client — the practice never keeps unearned retainer.
        /// </summary>
        public string CloseMatter(string matterId, int dayIndex, out int retainerRefundCents, List<string> diag)
        {
            diag = diag ?? diagnostics;
            retainerRefundCents = 0;
            if (!matters.TryGetValue(matterId, out LegalMatter matter))
                return $"CloseMatter: unknown matter '{matterId}'.";
            if (matter.Status == MatterStatus.Closed)
                return $"CloseMatter: matter '{matterId}' is already closed.";

            foreach (var invoice in invoices.Values)
            {
                if (invoice.MatterId == matterId && invoice.BalanceCents > 0)
                    return $"CloseMatter: invoice {invoice.InvoiceId} still shows {invoice.BalanceCents}c receivable — collect before closing.";
            }

            retainerRefundCents = retainerBalances[matterId];
            retainerBalances[matterId] = 0;
            matter.Status = MatterStatus.Closed;
            matter.ClosedDayIndex = dayIndex;
            diag.Add($"Law practice {businessInstanceId}: matter {matterId} closed (day {dayIndex}). Retainer refund due to '{matter.ClientName}': {retainerRefundCents}c.");
            return null;
        }

        /// <summary>
        /// Abandons a matter. Unpaid balances survive abandonment — walking
        /// away does not erase what the client owes.
        /// </summary>
        public string AbandonMatter(string matterId, int dayIndex, List<string> diag)
        {
            diag = diag ?? diagnostics;
            if (!matters.TryGetValue(matterId, out LegalMatter matter))
                return $"AbandonMatter: unknown matter '{matterId}'.";
            if (matter.Status == MatterStatus.Closed)
                return $"AbandonMatter: matter '{matterId}' is already closed.";

            matter.Status = MatterStatus.Abandoned;
            matter.ClosedDayIndex = dayIndex;
            int outstanding = 0;
            foreach (var invoice in invoices.Values)
            {
                if (invoice.MatterId == matterId) outstanding += invoice.BalanceCents;
            }
            diag.Add($"Law practice {businessInstanceId}: matter {matterId} abandoned (day {dayIndex}). {outstanding}c remains receivable from '{matter.ClientName}' — abandonment erases nothing.");
            return null;
        }

        /// <summary>Total outstanding receivables across all invoices, in cents.</summary>
        public int OutstandingReceivablesCents()
        {
            int sum = 0;
            foreach (var invoice in invoices.Values) sum += invoice.BalanceCents;
            return sum;
        }

        /// <summary>Total trust money currently held for clients, in cents.</summary>
        public int TotalRetainerTrustCents()
        {
            int sum = 0;
            foreach (int balance in retainerBalances.Values) sum += balance;
            return sum;
        }

        public LegalMatter GetMatter(string matterId)
        {
            return matters.TryGetValue(matterId, out LegalMatter m) ? m : null;
        }

        public LegalFeeInvoice GetInvoice(string invoiceId)
        {
            return invoices.TryGetValue(invoiceId, out LegalFeeInvoice i) ? i : null;
        }

        public IReadOnlyList<LegalMatter> Matters
        {
            get
            {
                var list = new List<LegalMatter>(matters.Values);
                return list;
            }
        }

        public IReadOnlyList<LegalFeeInvoice> Invoices
        {
            get
            {
                var list = new List<LegalFeeInvoice>(invoices.Values);
                return list;
            }
        }

        #region Save / Load

        [Serializable]
        public sealed class LawyerPracticeSaveDto
        {
            public List<LegalMatter> Matters = new List<LegalMatter>();
            public List<LegalFeeInvoice> Invoices = new List<LegalFeeInvoice>();
            public List<MatterUnbilled> Unbilled = new List<MatterUnbilled>();
            public List<MatterRetainer> Retainers = new List<MatterRetainer>();
            public LawyerFeeSchedule FeeSchedule = new LawyerFeeSchedule();
            public LawyerOfficeRequirements Office = new LawyerOfficeRequirements();
        }

        [Serializable]
        public sealed class MatterUnbilled
        {
            public string MatterId = string.Empty;
            public List<FeeLineItem> Lines = new List<FeeLineItem>();
        }

        [Serializable]
        public sealed class MatterRetainer
        {
            public string MatterId = string.Empty;
            public int Cents;
        }

        public LawyerPracticeSaveDto CaptureSaveDto()
        {
            var dto = new LawyerPracticeSaveDto();
            foreach (var matter in matters.Values) dto.Matters.Add(matter);
            foreach (var invoice in invoices.Values) dto.Invoices.Add(invoice);
            foreach (var kvp in unbilled)
            {
                dto.Unbilled.Add(new MatterUnbilled { MatterId = kvp.Key, Lines = new List<FeeLineItem>(kvp.Value) });
            }
            foreach (var kvp in retainerBalances)
            {
                dto.Retainers.Add(new MatterRetainer { MatterId = kvp.Key, Cents = kvp.Value });
            }
            dto.FeeSchedule = feeSchedule;
            dto.Office = office;
            return dto;
        }

        public void LoadFromSaveDto(LawyerPracticeSaveDto dto)
        {
            matters.Clear();
            invoices.Clear();
            unbilled.Clear();
            retainerBalances.Clear();
            if (dto == null) return;
            foreach (var matter in dto.Matters)
            {
                if (matter == null) continue;
                matters[matter.MatterId] = matter;
                unbilled[matter.MatterId] = new List<FeeLineItem>();
                retainerBalances[matter.MatterId] = 0;
            }
            foreach (var item in dto.Unbilled)
            {
                if (item == null || !unbilled.ContainsKey(item.MatterId)) continue;
                unbilled[item.MatterId] = new List<FeeLineItem>(item.Lines ?? new List<FeeLineItem>());
            }
            foreach (var item in dto.Retainers)
            {
                if (item == null || !retainerBalances.ContainsKey(item.MatterId)) continue;
                retainerBalances[item.MatterId] = item.Cents;
            }
            foreach (var invoice in dto.Invoices)
            {
                if (invoice == null) continue;
                invoices[invoice.InvoiceId] = invoice;
            }
            feeSchedule = dto.FeeSchedule ?? new LawyerFeeSchedule();
            office = dto.Office ?? new LawyerOfficeRequirements();
        }

        #endregion
    }
}
