using System.Collections.Generic;
using LandLedgers.Economy.Bank;
using LandLedgers.Economy.Financing;
using LandLedgers.Primitives;
using NUnit.Framework;

namespace LandLedgers.EditorTests.Economy
{
    /// <summary>
    /// W7C: the note issuance desk per Canon banking. Bank notes are issued
    /// against real specie/reserves as registered T2A promissory-note
    /// instruments (never conjured); redemption on demand pays specie and
    /// retires the paper; the discount window buys third-party paper below
    /// face within finite capital. HARD CONSTRAINT (NX-3B): the bank creates
    /// no money — issuance is gated on vault specie.
    /// </summary>
    [TestFixture]
    public sealed class BankNoteDeskTests
    {
        private List<string> diag;
        private BankRuntime bank;
        private CreditRegistry credit;
        private EntityIdRegistry ids;
        private NoteDesk desk;

        [SetUp]
        public void SetUp()
        {
            diag = new List<string>();
            bank = new BankRuntime("bank-1", "First Bank of Town", "Kennedy Baldwin-ooms");
            Assert.IsNull(bank.EstablishVault("iron safe", false, true, diag));
            Assert.IsNull(bank.RecordOpeningCapital(50000, "owner savings",
                new List<SpecieLot>
                {
                    new SpecieLot { Kind = VaultSpecieKind.GoldCoin, KindName = "gold coin", AmountCents = 50000, SourceName = "owner savings" },
                }, 0, diag));
            credit = new CreditRegistry();
            ids = new EntityIdRegistry();
            desk = new NoteDesk(bank, credit, ids);
        }

        private static SpecieLot Cash(int cents, string source)
        {
            return new SpecieLot { Kind = VaultSpecieKind.GoldCoin, KindName = "gold coin", AmountCents = cents, SourceName = source };
        }

        private List<PromissoryNote> RegistryNotes()
        {
            return credit.CaptureSaveDto().Notes;
        }

        [Test]
        public void IssueNotesForLoanRegistersBothHalves()
        {
            Assert.IsNull(desk.IssueNotesForLoan("Borrower Bob", 20000, "1 year, 8%", 10, diag));
            Assert.AreEqual(20000, desk.NotesOutstandingCents());

            // No specie moved — the notes ARE the disbursement.
            Assert.AreEqual(50000, bank.VaultSpecieTotalCents());
            Assert.AreEqual(50000, bank.Ledger.CashOnHandCents);

            List<PromissoryNote> notes = RegistryNotes();
            PromissoryNote loanNote = notes.Find(n => n.MakerName == "Borrower Bob");
            Assert.IsNotNull(loanNote, "The borrower's loan note is the asset half.");
            Assert.AreEqual("First Bank of Town", loanNote.PayeeName);
            Assert.AreEqual(20000, loanNote.PrincipalCents);

            PromissoryNote bankNote = notes.Find(n => n.MakerName == "First Bank of Town");
            Assert.IsNotNull(bankNote, "The bank's bearer notes are the liability half.");
            Assert.AreEqual(NoteDesk.BearerPayeeName, bankNote.PayeeName);
            Assert.AreEqual(20000, bankNote.PrincipalCents);
            Assert.AreEqual("Borrower Bob", bankNote.HolderName);
            Assert.IsTrue(bankNote.Terms.Contains("bearer"));
        }

        [Test]
        public void IssuanceRefusedBeyondSpecieBacking()
        {
            Assert.IsNull(desk.IssueNotesForLoan("Borrower Bob", 40000, "1 year, 8%", 10, diag));
            Assert.IsNotNull(desk.IssueNotesForLoan("Borrower Ann", 20000, "1 year, 8%", 10, diag),
                "40000c outstanding + 20000c new exceeds 50000c vault specie — refused.");
            Assert.AreEqual(40000, desk.NotesOutstandingCents(), "Refused issuance changes nothing.");
            Assert.AreEqual(50000, bank.VaultSpecieTotalCents());
        }

        [Test]
        public void IssueNotesForSpecieAndRedeem()
        {
            Assert.IsNull(desk.IssueNotesForSpecie("Depositor Dan", Cash(10000, "Depositor Dan"), 11, diag));
            Assert.AreEqual(60000, bank.VaultSpecieTotalCents());
            Assert.AreEqual(60000, bank.Ledger.CashOnHandCents, "Note-backed specie is cash the bank holds.");
            Assert.AreEqual(10000, desk.NotesOutstandingCents());
            Assert.AreEqual(50000, bank.OwnerEquityAfterNotesCents(desk.NotesOutstandingCents()),
                "Equity nets the note liability.");

            List<SpecieLot> paid = desk.RedeemNotes("Depositor Dan", 10000, 12, diag);
            Assert.IsNotNull(paid);
            int paidTotal = 0;
            foreach (SpecieLot lot in paid) paidTotal += lot.AmountCents;
            Assert.AreEqual(10000, paidTotal, "Redemption pays real specie.");
            Assert.AreEqual(50000, bank.VaultSpecieTotalCents());
            Assert.AreEqual(50000, bank.Ledger.CashOnHandCents);
            Assert.AreEqual(0, desk.NotesOutstandingCents());

            List<PromissoryNote> notes = RegistryNotes();
            PromissoryNote bankNote = notes.Find(n => n.MakerName == "First Bank of Town");
            Assert.AreEqual(CreditInstrumentStatus.Satisfied, bankNote.Status, "Redeemed notes retire.");
        }

        [Test]
        public void RedemptionRefusedWhenVaultShort()
        {
            Assert.IsNull(desk.IssueNotesForLoan("Borrower Bob", 40000, "1 year, 8%", 10, diag));
            // Unpaired vault loss — the alarm scenario.
            Assert.IsNotNull(bank.DrawVaultLots(20000, diag));
            Assert.IsNull(desk.RedeemNotes("Borrower Bob", 40000, 11, diag),
                "The vault holds 30000c against 40000c presented — refused, never faked (Canon §18.10).");
            Assert.AreEqual(40000, desk.NotesOutstandingCents(), "Refused redemptions retire nothing.");
            Assert.AreEqual(50000, bank.Ledger.CashOnHandCents, "Refused redemptions move nothing.");
        }

        [Test]
        public void RedemptionRequiresWholeNotes()
        {
            Assert.IsNull(desk.IssueNotesForLoan("Borrower Bob", 20000, "1 year, 8%", 10, diag));
            Assert.IsNull(desk.RedeemNotes("Borrower Bob", 15000, 11, diag),
                "Bearer instruments are not divisible — present whole notes.");
            Assert.AreEqual(20000, desk.NotesOutstandingCents());
        }

        [Test]
        public void DiscountWindowWithinFiniteCapital()
        {
            PromissoryNote paper = credit.IssuePromissoryNote(ids, "Miller", "Storekeeper",
                10000, "90 days", 5, null, diag);
            Assert.IsNotNull(paper);

            Assert.IsNull(desk.DiscountNote(paper.InstrumentId, 9000, 6, diag));
            Assert.AreEqual(41000, bank.Ledger.CashOnHandCents, "The bank pays cash it holds.");
            Assert.AreEqual(41000, bank.VaultSpecieTotalCents(), "Ledger and vault move together.");
            Assert.AreEqual("First Bank of Town", paper.HolderName, "Endorsement recorded on the instrument.");
            Assert.AreEqual(1, new List<DiscountedPaper>(desk.DiscountedPaper).Count);

            // Beyond finite capital: refused.
            PromissoryNote bigPaper = credit.IssuePromissoryNote(ids, "Railroad", "Supplier",
                50000, "60 days", 5, null, diag);
            Assert.IsNotNull(desk.DiscountNote(bigPaper.InstrumentId, 45000, 6, diag),
                "The bank holds 41000c — a 45000c purchase is refused.");
            Assert.AreEqual(41000, bank.Ledger.CashOnHandCents, "Refused discounts move nothing.");
            Assert.AreEqual(41000, bank.VaultSpecieTotalCents(), "Refused discounts move nothing.");

            // Collection at face realizes the discount as cash.
            Assert.IsNull(desk.CollectDiscountedNote(paper.InstrumentId, 90, diag));
            Assert.AreEqual(51000, bank.Ledger.CashOnHandCents);
            Assert.AreEqual(51000, bank.VaultSpecieTotalCents());
            Assert.AreEqual(CreditInstrumentStatus.Satisfied, paper.Status);
        }

        [Test]
        public void DiscountRefusesOwnPaperAndPremiums()
        {
            Assert.IsNull(desk.IssueNotesForLoan("Borrower Bob", 20000, "1 year, 8%", 10, diag));
            PromissoryNote bankNote = RegistryNotes().Find(n => n.MakerName == "First Bank of Town");
            Assert.IsNotNull(desk.DiscountNote(bankNote.InstrumentId, 19000, 11, diag),
                "The desk discounts third-party paper — its own notes are issuance, not inventory.");

            PromissoryNote paper = credit.IssuePromissoryNote(ids, "Miller", "Storekeeper",
                10000, "90 days", 5, null, diag);
            Assert.IsNotNull(desk.DiscountNote(paper.InstrumentId, 11000, 11, diag),
                "Paying 11000c for 10000c face is a premium, not a discount.");
            Assert.IsNotNull(desk.DiscountNote(paper.InstrumentId, 0, 11, diag),
                "The price must be positive.");
        }

        [Test]
        public void NoteDeskSaveRoundTrip()
        {
            Assert.IsNull(desk.IssueNotesForLoan("Borrower Bob", 20000, "1 year, 8%", 10, diag));
            PromissoryNote paper = credit.IssuePromissoryNote(ids, "Miller", "Storekeeper",
                10000, "90 days", 5, null, diag);
            Assert.IsNull(desk.DiscountNote(paper.InstrumentId, 9000, 6, diag));

            NoteDesk.NoteDeskSaveDto dto = desk.ToSaveDto();
            var restored = new NoteDesk(bank, credit, ids);
            restored.LoadFromSaveDto(dto);

            Assert.AreEqual(20000, restored.NotesOutstandingCents());
            Assert.AreEqual(1, new List<DiscountedPaper>(restored.DiscountedPaper).Count);
            Assert.AreEqual(10000, new List<DiscountedPaper>(restored.DiscountedPaper)[0].FaceCents);
            Assert.AreEqual(9000, new List<DiscountedPaper>(restored.DiscountedPaper)[0].PaidCents);
        }
    }
}
