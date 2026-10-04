using System;
using System.Collections.Generic;
using UnityEngine;
using EntityId = LandLedgers.Primitives.EntityId;
using LandLedgers.Economy.Financing;

namespace LandLedgers.Economy.Bank
{
    /// <summary>D3B: lifecycle of an agency collection item.</summary>
    public enum CollectionItemStatus
    {
        Unspecified = 0,
        /// <summary>Paper received at the counter, held for the customer.</summary>
        Received = 1,
        /// <summary>Sent out of town through a correspondent; proceeds pending.</summary>
        SentForCollection = 2,
        /// <summary>Maker paid; the bank owes the customer net proceeds.</summary>
        Collected = 3,
        /// <summary>Maker did not pay; paper returned to the customer.</summary>
        Defaulted = 4,
        /// <summary>Net proceeds paid out to the customer — closed.</summary>
        PaidToCustomer = 5,
    }

    /// <summary>
    /// D3B: one item left with the bank FOR COLLECTION. The bank is the
    /// customer's AGENT, not the paper's owner: the customer keeps the
    /// economic interest, the bank presents the paper and takes only its
    /// stated fee. This is the other half of Canon §16.6's "collection
    /// business" — the discount window (NoteDesk) buys paper outright, the
    /// collections desk works it for a fee.
    /// </summary>
    [Serializable]
    public sealed class CollectionItem
    {
        public string ItemId = string.Empty;
        public string CustomerName = string.Empty;
        public string MakerName = string.Empty;
        public string MakerTown = string.Empty;
        public EntityId InstrumentId = EntityId.Invalid;
        public int FaceCents;
        public int FeeCents;
        public string FeeTerms = string.Empty;
        public int ReceivedDayIndex;
        public int DueDayIndex;
        public CollectionItemStatus Status = CollectionItemStatus.Received;
        public int SettlementDayIndex = -1;
        public string CorrespondentAccountId = string.Empty;

        /// <summary>What the customer is owed once collected: face less the stated fee.</summary>
        public int NetProceedsCents() => Math.Max(0, FaceCents - FeeCents);

        public CollectionItem() { }
    }

    /// <summary>
    /// D3B: agency collections on notes for customers (Canon §16.6:
    /// "handling drafts/remittances and collections"). Two honest paths:
    ///
    /// - LOCAL paper: the maker pays at the counter in specie; the bank
    ///   takes its stated fee and owes the customer the net.
    /// - OUT-OF-TOWN paper: the item goes through a correspondent account.
    ///   Proceeds land as outside balance after the collection float
    ///   (calibration — see CollectionFloatDays); the bank cannot collect
    ///   out-of-town paper with no correspondent to send it through.
    ///
    /// Fee income is terms as written on each item — the desk states its fee
    /// when it accepts the paper, never after. A defaulted item is returned
    /// to the customer with the paper; no fee is taken on paper that did not
    /// pay (protest costs, if any, are part of the stated terms).
    ///
    /// HARD CONSTRAINT (NX-3B): the desk creates NO money. Proceeds are real
    /// specie or real correspondent credits; the customer is paid only from
    /// what actually arrived, and every shortfall is refused loudly.
    /// </summary>
    public sealed class BankCollectionsDesk
    {
        /// <summary>
        /// Calibration (not canon): days between sending out-of-town paper
        /// and the proceeds landing. The mail and the presenting bank take
        /// real time; the game should not pretend otherwise.
        /// </summary>
        public const int DefaultCollectionFloatDays = 7;

        private readonly BankRuntime bank;
        private readonly CreditRegistry credit;

        private readonly List<CollectionItem> items = new List<CollectionItem>();
        private int nextItemNumber = 1;

        /// <summary>Collection float in days — calibration, settable by the operating layer.</summary>
        public int CollectionFloatDays = DefaultCollectionFloatDays;

        public BankRuntime Bank => bank;
        public CreditRegistry Credit => credit;
        public IReadOnlyList<CollectionItem> Items => items;

        public BankCollectionsDesk(BankRuntime bank, CreditRegistry credit)
        {
            this.bank = bank;
            this.credit = credit;
        }

        /// <summary>
        /// Net proceeds the bank owes customers on collected-but-unpaid
        /// items — a real liability on the balance sheet (Canon §16.4).
        /// </summary>
        public int CollectionObligationsCents()
        {
            int total = 0;
            foreach (CollectionItem item in items)
                if (item.Status == CollectionItemStatus.Collected)
                    total += item.NetProceedsCents();
            return total;
        }

        private CollectionItem FindItem(string itemId)
        {
            foreach (CollectionItem item in items)
                if (string.Equals(item.ItemId, itemId, StringComparison.Ordinal))
                    return item;
            return null;
        }

        private string Ready(List<string> diagnostics)
        {
            if (bank == null || credit == null)
                return "BankCollectionsDesk: the desk needs a bank and a credit registry.";
            return null;
        }

        /// <summary>
        /// A customer leaves paper with the bank for collection. The desk
        /// states its fee NOW, as terms — feeCents and feeTerms are the
        /// agreement, never revised later. The registry holder becomes the
        /// bank (held as agent for the customer, recorded on the item).
        /// </summary>
        public string AcceptForCollection(string customerName, string makerName,
            string makerTown, EntityId instrumentId, int feeCents, string feeTerms,
            int dueDayIndex, int dayIndex, List<string> diagnostics)
        {
            diagnostics = diagnostics ?? new List<string>();
            string ready = Ready(diagnostics);
            if (ready != null) return ready;
            if (string.IsNullOrWhiteSpace(customerName))
                return "BankCollectionsDesk.AcceptForCollection: the customer must be named — the bank collects for someone.";
            if (string.IsNullOrWhiteSpace(makerName))
                return "BankCollectionsDesk.AcceptForCollection: the maker must be named.";
            if (!credit.TryGetPromissoryNote(instrumentId, out PromissoryNote note) || note == null)
                return $"BankCollectionsDesk.AcceptForCollection: no promissory note '{instrumentId}' on the registry — the desk collects real paper, not rumors.";
            if (note.Status != CreditInstrumentStatus.Active)
                return $"BankCollectionsDesk.AcceptForCollection: note '{instrumentId}' is {note.Status} — only live paper is collected.";
            if (string.Equals(note.MakerName, customerName, StringComparison.Ordinal))
                return $"BankCollectionsDesk.AcceptForCollection: '{customerName}' is the MAKER of '{instrumentId}' — a maker does not leave their own note for collection; they pay it.";
            if (!string.IsNullOrWhiteSpace(makerName) &&
                !string.Equals(note.MakerName, makerName, StringComparison.Ordinal))
                return $"BankCollectionsDesk.AcceptForCollection: the registry says '{instrumentId}' is made by '{note.MakerName}', not '{makerName}' — the paper is not what was described.";
            foreach (CollectionItem existing in items)
                if (existing.InstrumentId.Equals(instrumentId) &&
                    (existing.Status == CollectionItemStatus.Received ||
                     existing.Status == CollectionItemStatus.SentForCollection ||
                     existing.Status == CollectionItemStatus.Collected))
                    return $"BankCollectionsDesk.AcceptForCollection: note '{instrumentId}' is already in collection as '{existing.ItemId}'.";
            if (feeCents < 0)
                return "BankCollectionsDesk.AcceptForCollection: the fee cannot be negative.";

            note.HolderName = bank.BusinessName;
            var item = new CollectionItem
            {
                ItemId = $"COLL-{bank.BusinessInstanceId}-{nextItemNumber++:D4}",
                CustomerName = customerName,
                MakerName = note.MakerName,
                MakerTown = makerTown ?? string.Empty,
                InstrumentId = instrumentId,
                FaceCents = note.PrincipalCents,
                FeeCents = feeCents,
                FeeTerms = feeTerms ?? string.Empty,
                ReceivedDayIndex = dayIndex,
                DueDayIndex = dueDayIndex,
                Status = CollectionItemStatus.Received,
            };
            items.Add(item);
            diagnostics.Add($"BankCollectionsDesk [{bank.BusinessName}]: '{item.ItemId}' accepted — {note.PrincipalCents}c on {note.MakerName} for '{customerName}'" +
                (string.IsNullOrWhiteSpace(item.MakerTown) ? "" : $" ({item.MakerTown})") +
                $", fee {feeCents}c{(string.IsNullOrWhiteSpace(item.FeeTerms) ? "" : " (" + item.FeeTerms + ")")} — held as agent, not owned.");
            return null;
        }

        /// <summary>
        /// Sends out-of-town paper through a correspondent. Requires a real
        /// correspondent account — without one there is nobody out of town
        /// to present the paper to. Proceeds are expected after the
        /// collection float; SettleCollection records what actually arrived.
        /// </summary>
        public string SendForCollection(string itemId, BankCorrespondentAccount correspondent,
            int dayIndex, List<string> diagnostics)
        {
            diagnostics = diagnostics ?? new List<string>();
            string ready = Ready(diagnostics);
            if (ready != null) return ready;
            CollectionItem item = FindItem(itemId);
            if (item == null)
                return $"BankCollectionsDesk.SendForCollection: no item '{itemId}'.";
            if (item.Status != CollectionItemStatus.Received)
                return $"BankCollectionsDesk.SendForCollection: '{itemId}' is {item.Status} — only received paper goes out.";
            if (correspondent == null)
                return $"BankCollectionsDesk.SendForCollection: '{itemId}' needs a correspondent — out-of-town paper cannot be collected by wishing (Canon §18.11).";

            item.Status = CollectionItemStatus.SentForCollection;
            item.CorrespondentAccountId = correspondent.AccountId;
            item.SettlementDayIndex = dayIndex + Math.Max(0, CollectionFloatDays);
            diagnostics.Add($"BankCollectionsDesk [{bank.BusinessName}]: '{itemId}' sent through '{correspondent.CorrespondentName}' — proceeds expected day {item.SettlementDayIndex} (float {CollectionFloatDays}d).");
            return null;
        }

        /// <summary>
        /// Records what the presentment actually brought back. Collected:
        /// proceeds land as correspondent balance and the bank owes the
        /// customer net proceeds (fee earned — the residual shows it).
        /// Defaulted: the paper goes back to the customer, no fee taken.
        /// </summary>
        public string SettleCollection(string itemId, bool collected,
            BankCorrespondentAccount correspondent, int dayIndex, List<string> diagnostics)
        {
            diagnostics = diagnostics ?? new List<string>();
            string ready = Ready(diagnostics);
            if (ready != null) return ready;
            CollectionItem item = FindItem(itemId);
            if (item == null)
                return $"BankCollectionsDesk.SettleCollection: no item '{itemId}'.";
            if (item.Status != CollectionItemStatus.SentForCollection)
                return $"BankCollectionsDesk.SettleCollection: '{itemId}' is {item.Status} — only sent paper settles.";

            if (collected)
            {
                if (correspondent == null)
                    return $"BankCollectionsDesk.SettleCollection: '{itemId}' was sent through a correspondent — its proceeds need one to land in.";
                string refused = correspondent.ReceiveCollectionSettlement(item.FaceCents,
                    $"collection {itemId} ({item.MakerName})", diagnostics);
                if (refused != null) return refused;
                item.Status = CollectionItemStatus.Collected;
                item.SettlementDayIndex = dayIndex;
                if (credit.TryGetPromissoryNote(item.InstrumentId, out PromissoryNote note) && note != null)
                    credit.SatisfyInstrument(item.InstrumentId.ToString(), diagnostics);
                diagnostics.Add($"BankCollectionsDesk [{bank.BusinessName}]: '{itemId}' COLLECTED — {item.FaceCents}c through '{correspondent.CorrespondentName}'; '{item.CustomerName}' is owed {item.NetProceedsCents()}c (fee {item.FeeCents}c earned).");
            }
            else
            {
                item.Status = CollectionItemStatus.Defaulted;
                item.SettlementDayIndex = dayIndex;
                if (credit.TryGetPromissoryNote(item.InstrumentId, out PromissoryNote note) && note != null)
                    note.HolderName = item.CustomerName;
                diagnostics.Add($"BankCollectionsDesk [{bank.BusinessName}]: '{itemId}' DEFAULTED — {item.MakerName} did not pay. Paper returned to '{item.CustomerName}'; no fee taken on paper that did not pay.");
            }
            return null;
        }

        /// <summary>
        /// LOCAL paper: the maker pays at the counter in specie. The bank
        /// takes its stated fee and owes the customer the net — a liability
        /// until PayCustomer. The operating layer pairs the vault lot.
        /// </summary>
        public string CollectLocalItem(string itemId, int dayIndex, List<string> diagnostics)
        {
            diagnostics = diagnostics ?? new List<string>();
            string ready = Ready(diagnostics);
            if (ready != null) return ready;
            CollectionItem item = FindItem(itemId);
            if (item == null)
                return $"BankCollectionsDesk.CollectLocalItem: no item '{itemId}'.";
            if (item.Status != CollectionItemStatus.Received)
                return $"BankCollectionsDesk.CollectLocalItem: '{itemId}' is {item.Status} — only received local paper is collected at the counter.";

            string refused = bank.Ledger.RecordOperatingInflow(item.FaceCents,
                $"local collection {itemId} ({item.MakerName})", diagnostics);
            if (refused != null) return refused;
            item.Status = CollectionItemStatus.Collected;
            item.SettlementDayIndex = dayIndex;
            credit.SatisfyInstrument(item.InstrumentId.ToString(), diagnostics);
            diagnostics.Add($"BankCollectionsDesk [{bank.BusinessName}]: '{itemId}' collected locally — {item.FaceCents}c in; '{item.CustomerName}' is owed {item.NetProceedsCents()}c (fee {item.FeeCents}c earned).");
            return null;
        }

        /// <summary>
        /// Pays the customer their net proceeds on a collected item. The cash
        /// moves through the ledger (out); the operating layer pairs the
        /// vault specie the customer walks away with. Refused when the
        /// correspondent leg cannot fund it — the customer is paid from what
        /// actually arrived, never advanced.
        /// </summary>
        public string PayCustomer(string itemId, BankCorrespondentAccount correspondent,
            int dayIndex, List<string> diagnostics)
        {
            diagnostics = diagnostics ?? new List<string>();
            string ready = Ready(diagnostics);
            if (ready != null) return ready;
            CollectionItem item = FindItem(itemId);
            if (item == null)
                return $"BankCollectionsDesk.PayCustomer: no item '{itemId}'.";
            if (item.Status != CollectionItemStatus.Collected)
                return $"BankCollectionsDesk.PayCustomer: '{itemId}' is {item.Status} — nothing is owed yet.";
            int net = item.NetProceedsCents();

            if (!string.IsNullOrWhiteSpace(item.CorrespondentAccountId))
            {
                if (correspondent == null)
                    return $"BankCollectionsDesk.PayCustomer: '{itemId}' settled through a correspondent — its proceeds need one to come home through.";
                string refused = correspondent.DrawOnCorrespondent(net, bank, dayIndex, diagnostics);
                if (refused != null) return refused;
            }
            string outflowRefused = bank.Ledger.RecordOperatingOutflow(net,
                $"collection proceeds to '{item.CustomerName}' ({itemId}), day {dayIndex}", diagnostics);
            if (outflowRefused != null) return outflowRefused;

            item.Status = CollectionItemStatus.PaidToCustomer;
            diagnostics.Add($"BankCollectionsDesk [{bank.BusinessName}]: '{itemId}' paid — '{item.CustomerName}' received {net}c. Closed.");
            return null;
        }

        // ---------- save DTO (inside the owning runtime class) ----------

        [Serializable]
        public sealed class BankCollectionsDeskSaveDto
        {
            public int NextItemNumber = 1;
            public int CollectionFloatDays = DefaultCollectionFloatDays;
            public List<CollectionItem> Items = new List<CollectionItem>();
        }

        public BankCollectionsDeskSaveDto ToSaveDto()
        {
            return new BankCollectionsDeskSaveDto
            {
                NextItemNumber = Math.Max(1, nextItemNumber),
                CollectionFloatDays = Math.Max(0, CollectionFloatDays),
                Items = new List<CollectionItem>(items),
            };
        }

        public void LoadFromSaveDto(BankCollectionsDeskSaveDto dto)
        {
            items.Clear();
            if (dto == null) return;
            nextItemNumber = Math.Max(1, dto.NextItemNumber);
            CollectionFloatDays = Math.Max(0, dto.CollectionFloatDays);
            if (dto.Items != null)
                foreach (CollectionItem item in dto.Items)
                    if (item != null && !string.IsNullOrWhiteSpace(item.ItemId))
                        items.Add(item);
        }
    }
}
