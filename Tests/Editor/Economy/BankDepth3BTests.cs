using System.Collections.Generic;
using LandLedgers.Economy.Bank;
using LandLedgers.Economy.Financing;
using LandLedgers.Primitives;
using NUnit.Framework;

namespace LandLedgers.EditorTests.Economy
{
    /// <summary>
    /// D3B: bank depth — loan-book balance-sheet consolidation (Canon §16.4,
    /// §18.8), customer discount window with endorser recourse, agency
    /// collections on notes (Canon §16.6), correspondent accounts
    /// (Canon §18.11), and drafts. HARD CONSTRAINT (NX-3B): the bank creates
    /// no money — every refusal below is the constraint working.
    /// </summary>
    [TestFixture]
    public sealed class BankDepth3BTests
    {
        private List<string> diag;
        private BankRuntime bank;
        private CreditRegistry credit;
        private EntityIdRegistry ids;
        private NoteDesk desk;
        private BankLoanBook book;
        private BankCorrespondentAccount corr;
        private BankCollectionsDesk collections;

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
            book = new BankLoanBook(bank, credit);
            book.AttachNoteDesk(desk);
            corr = new BankCorrespondentAccount("corr-1", "Omaha National Bank", "acct 417", 1);
            book.AttachCorrespondent(corr);
            collections = new BankCollectionsDesk(bank, credit);
            book.AttachCollectionsDesk(collections);
            Assert.IsNull(book.RecordPaidCapital(50000, "owner savings", diag));
        }

        private PromissoryNote MakePaper(string maker, string payee, int faceCents)
        {
            PromissoryNote paper = credit.IssuePromissoryNote(ids, maker, payee,
                faceCents, "90 days", 5, null, diag);
            Assert.IsNotNull(paper);
            return paper;
        }

        // ---------- loan book & balance sheet ----------

        [Test]
        public void PaidCapitalRecordedOnce()
        {
            Assert.IsNotNull(book.RecordPaidCapital(1000, "more savings", diag),
                "Paid-in capital is contributed once (Canon §16.1) — not re-declared.");
            Assert.AreEqual(50000, book.PaidCapitalCents);
        }

        [Test]
        public void RegisterBorrowerNoteRequiresRegistryPaper()
        {
            var bogus = new EntityIdRegistry().Allocate(EntityKind.Contract);
            Assert.IsNotNull(book.RegisterBorrowerNote(bogus, "Nobody", 1000, 2, false, diag),
                "The book adopts real paper — no registry entry, no asset.");

            PromissoryNote foreign = MakePaper("Miller", "Some Other Bank", 8000);
            Assert.IsNotNull(book.RegisterBorrowerNote(foreign.InstrumentId, "Miller", 8000, 2, false, diag),
                "Paper naming another bank is not this bank's asset.");
            Assert.AreEqual(0, book.LiveEarningAssetsCents());
        }

        [Test]
        public void IssueNotesForLoanRegistersBookAssetAndSheetBalances()
        {
            Assert.IsNull(desk.IssueNotesForLoan("Borrower Bob", 20000, "1 year, 8%", 10, diag, book));
            Assert.AreEqual(20000, book.LiveBookValueCents(BankLoanAssetKind.BorrowerNote));

            BankBalanceSheet sheet = book.BuildBalanceSheet();
            Assert.AreEqual(70000, sheet.TotalAssetsCents(), "50000c cash + 20000c loan asset.");
            Assert.AreEqual(20000, sheet.TotalLiabilitiesCents(), "20000c notes outstanding.");
            Assert.AreEqual(50000, sheet.TotalEquityCents());
            Assert.AreEqual(0, sheet.UndistributedProfitsCents(), "No profit yet — notes issued at face against a face asset.");
            Assert.IsTrue(sheet.Balances(), "Canon §18.15: the sheet must balance.");
        }

        [Test]
        public void FullCycleKeepsSheetBalancedAndShowsRealProfit()
        {
            DepositAccount opened = bank.Ledger.OpenAccount("a1", "Depositor Dan", DepositKind.Demand, 20000, 11, diag: diag);
            Assert.IsNotNull(opened, "The deposit account opens — 20000c in, a liability.");
            Assert.AreEqual(70000, bank.Ledger.CashOnHandCents);
            Assert.IsNull(desk.IssueNotesForLoan("Borrower Bob", 20000, "1 year, 8%", 10, diag, book));
            PromissoryNote paper = MakePaper("Miller", "Storekeeper", 10000);
            Assert.IsNull(desk.DiscountNote(paper.InstrumentId, 9000, 12, diag, book));
            Assert.AreEqual(9000, book.LiveBookValueCents(BankLoanAssetKind.DiscountedPaper),
                "Discounted paper books at PAID COST, not face.");
            Assert.IsNull(desk.CollectDiscountedNote(paper.InstrumentId, 90, diag, book));

            BankBalanceSheet sheet = book.BuildBalanceSheet();
            Assert.IsTrue(sheet.Balances());
            Assert.AreEqual(1000, sheet.UndistributedProfitsCents(),
                "The 1000c discount spread is the only real profit — derived, never asserted.");

            Assert.IsNull(book.TransferToSurplus(400, diag));
            Assert.AreEqual(400, book.SurplusCents);
            Assert.AreEqual(600, book.UndistributedProfitsCents());
            Assert.IsNotNull(book.TransferToSurplus(700, diag),
                "Cannot capitalize earnings the bank has not made (Canon §16.4).");

            Assert.IsNull(book.DeclareOwnerDistribution(600, 91, diag));
            Assert.AreEqual(0, book.UndistributedProfitsCents());
            sheet = book.BuildBalanceSheet();
            Assert.IsTrue(sheet.Balances(), "Distributions move cash — the sheet still balances.");
            Assert.IsNotNull(book.DeclareOwnerDistribution(1, 92, diag),
                "The owner cannot distribute capital or surplus as earnings.");
        }

        [Test]
        public void ChargeOffAbsorbsLossThroughResidual()
        {
            Assert.IsNull(desk.IssueNotesForLoan("Borrower Bob", 20000, "1 year, 8%", 10, diag, book));
            PromissoryNote loanNote = credit.CaptureSaveDto().Notes.Find(n => n.MakerName == "Borrower Bob");
            Assert.IsNull(book.ChargeOffAsset(loanNote.InstrumentId, "borrower failed, no recovery", diag));

            BankBalanceSheet sheet = book.BuildBalanceSheet();
            Assert.IsTrue(sheet.Balances());
            Assert.AreEqual(-20000, sheet.UndistributedProfitsCents(),
                "The 20000c charge-off is a real loss — equity is poorer, nothing was hidden.");
            Assert.AreEqual(30000, sheet.TotalEquityCents());
        }

        [Test]
        public void ConnectedBorrowerIdentifiedNotJudged()
        {
            Assert.IsNull(desk.IssueNotesForLoan("Kennedy Baldwin-ooms", 10000, "demand", 10, diag, book));
            PromissoryNote loanNote = credit.CaptureSaveDto().Notes.Find(n => n.MakerName == "Kennedy Baldwin-ooms");
            Assert.IsNull(book.MarkConnectedBorrower(loanNote.InstrumentId, true, diag));
            Assert.IsTrue(new List<BankLoanBookEntry>(book.Entries).Find(e => e.InstrumentId.Equals(loanNote.InstrumentId)).ConnectedBorrower);
            Assert.AreEqual(10000, book.LiveBookValueCents(BankLoanAssetKind.BorrowerNote),
                "Canon §16.5: a connected loan is still a real loan — identified, still booked.");
        }

        [Test]
        public void MortgageDeedRegistersAtPrincipal()
        {
            MortgageDeed mortgage = credit.IssueMortgage(ids, "Homesteader Hal", "First Bank of Town",
                "prop-9", "quarter section 9", 15000, "5 years, 8%", 20, diag);
            Assert.IsNotNull(mortgage);
            Assert.IsNull(book.RegisterMortgageDeed(mortgage.InstrumentId, 20, false, diag));
            Assert.AreEqual(15000, book.LiveBookValueCents(BankLoanAssetKind.MortgageDeed));
            Assert.IsTrue(book.BuildBalanceSheet().Balances());
        }

        // ---------- customer discount window with recourse ----------

        [Test]
        public void DiscountCustomerPaperRegistersEndorserRecourse()
        {
            PromissoryNote paper = MakePaper("Miller", "Storekeeper", 10000);
            Assert.IsNull(desk.DiscountCustomerPaper("Storekeeper", paper.InstrumentId, 9000,
                "90 days at 10%", 6, book, diag));

            Assert.AreEqual(41000, bank.Ledger.CashOnHandCents, "The bank pays cash it holds.");
            Assert.AreEqual(41000, bank.VaultSpecieTotalCents(), "Ledger and vault move together.");
            Assert.AreEqual("First Bank of Town", paper.HolderName);

            List<GuarantyAgreement> guaranties = credit.CaptureSaveDto().Guaranties;
            GuarantyAgreement guaranty = guaranties.Find(g => g.GuarantorName == "Storekeeper");
            Assert.IsNotNull(guaranty, "The endorser's recourse is a registered guaranty, not a handshake.");
            Assert.AreEqual("First Bank of Town", guaranty.CreditorName);
            Assert.AreEqual("Miller", guaranty.DebtorName);
            Assert.AreEqual(10000, guaranty.MaxExposureCents);
            Assert.AreEqual(CreditInstrumentStatus.Active, guaranty.Status);

            Assert.AreEqual(9000, book.LiveBookValueCents(BankLoanAssetKind.DiscountedPaper));
            Assert.IsTrue(book.BuildBalanceSheet().Balances());
        }

        [Test]
        public void DiscountCustomerPaperRefusesMakerAndBadPrices()
        {
            PromissoryNote paper = MakePaper("Miller", "Storekeeper", 10000);
            Assert.IsNotNull(desk.DiscountCustomerPaper("Miller", paper.InstrumentId, 9000, "terms", 6, book, diag),
                "A maker discounting their own note is borrowing — route to a loan.");
            Assert.IsNotNull(desk.DiscountCustomerPaper("Storekeeper", paper.InstrumentId, 11000, "terms", 6, book, diag),
                "A premium is not a discount.");
            Assert.IsNotNull(desk.DiscountCustomerPaper("Storekeeper", paper.InstrumentId, 0, "terms", 6, book, diag),
                "The price must be positive.");
            Assert.AreEqual(50000, bank.Ledger.CashOnHandCents, "Refused discounts move nothing.");
        }

        [Test]
        public void DiscountCustomerPaperRefusedBeyondFiniteCapital()
        {
            PromissoryNote paper = MakePaper("Railroad", "Supplier", 60000);
            Assert.IsNotNull(desk.DiscountCustomerPaper("Supplier", paper.InstrumentId, 55000, "terms", 6, book, diag),
                "The bank holds 50000c — a 55000c discount is refused. Lenders lend what they have.");
            Assert.AreEqual(50000, bank.Ledger.CashOnHandCents);
        }

        [Test]
        public void MakerDefaultCallsEndorserGuaranty()
        {
            PromissoryNote paper = MakePaper("Miller", "Storekeeper", 10000);
            Assert.IsNull(desk.DiscountCustomerPaper("Storekeeper", paper.InstrumentId, 9000,
                "90 days at 10%", 6, book, diag));

            Assert.IsNull(desk.RecordDiscountDefault(paper.InstrumentId, 10000, 90, book, diag));

            GuarantyAgreement guaranty = credit.CaptureSaveDto().Guaranties.Find(g => g.GuarantorName == "Storekeeper");
            Assert.AreEqual(CreditInstrumentStatus.Called, guaranty.Status, "Active → Called: the obligation is real now.");
            Assert.AreEqual(10000, guaranty.CalledAmountCents);
            Assert.AreEqual(CreditInstrumentStatus.Defaulted, paper.Status);

            Assert.AreEqual(0, book.LiveBookValueCents(BankLoanAssetKind.DiscountedPaper));
            Assert.AreEqual(10000, book.LiveBookValueCents(BankLoanAssetKind.RecourseReceivable),
                "The paper converted to a receivable against the ENDORSER at the called amount.");
            BankBalanceSheet sheet = book.BuildBalanceSheet();
            Assert.IsTrue(sheet.Balances());
            Assert.AreEqual(1000, sheet.UndistributedProfitsCents(),
                "Bought at 9000c, endorser answers 10000c — the economics of recourse, derived.");
        }

        [Test]
        public void NoRecoursePaperCannotTakeRecoursePath()
        {
            PromissoryNote paper = MakePaper("Miller", "Storekeeper", 10000);
            Assert.IsNull(desk.DiscountNote(paper.InstrumentId, 9000, 6, diag, book));
            Assert.IsNotNull(desk.RecordDiscountDefault(paper.InstrumentId, 10000, 90, book, diag),
                "Desk-selected paper has no endorser — the bank owns the loss; charge it off.");
        }

        // ---------- correspondent accounts ----------

        [Test]
        public void RemitAndDrawMoveOutsideMoneyHonestly()
        {
            Assert.IsNull(corr.RemitToCorrespondent(10000, bank, 5, diag));
            Assert.AreEqual(10000, corr.BalanceCents);
            Assert.AreEqual(40000, bank.Ledger.CashOnHandCents, "An asset transfer, not income.");

            Assert.IsNull(corr.DrawOnCorrespondent(4000, bank, 6, diag));
            Assert.AreEqual(6000, corr.BalanceCents);
            Assert.AreEqual(44000, bank.Ledger.CashOnHandCents);

            Assert.IsNotNull(corr.DrawOnCorrespondent(7000, bank, 7, diag),
                "No overdrafts — the correspondent is not a money printer.");
            Assert.AreEqual(6000, corr.BalanceCents, "Refused draws move nothing.");
        }

        [Test]
        public void DraftSaleAndClearance()
        {
            Assert.IsNull(corr.RemitToCorrespondent(20000, bank, 5, diag));
            Assert.IsNull(corr.SellDraft("Traveler Tom", "Yankton Merchant", 5000, 50, "draft fee 1%", bank, 6, diag));

            Assert.AreEqual(35050, bank.Ledger.CashOnHandCents, "Buyer pays face + fee in cash.");
            Assert.AreEqual(5000, corr.DraftsOutstandingCents(), "The sold draft is a real liability.");
            Assert.AreEqual(15000, corr.AvailableToDrawCents(), "Drafts are spoken for — availability excludes them.");

            BankDraft draft = new List<BankDraft>(corr.Drafts)[0];
            Assert.IsNull(corr.ClearDraft(draft.DraftId, 20, diag));
            Assert.AreEqual(15000, corr.BalanceCents, "The correspondent paid the draft out of the balance.");
            Assert.AreEqual(0, corr.DraftsOutstandingCents());
            Assert.AreEqual(BankDraftStatus.Cleared, draft.Status);

            BankBalanceSheet sheet = book.BuildBalanceSheet();
            Assert.IsTrue(sheet.Balances());
            Assert.AreEqual(50, sheet.UndistributedProfitsCents(), "The draft fee is the only new equity.");
        }

        [Test]
        public void ClearDraftRefusedWhenCorrespondentShort()
        {
            Assert.IsNull(corr.RemitToCorrespondent(1000, bank, 5, diag));
            Assert.IsNull(corr.SellDraft("Traveler Tom", "Yankton Merchant", 5000, 0, "", bank, 6, diag));
            BankDraft draft = new List<BankDraft>(corr.Drafts)[0];
            Assert.IsNotNull(corr.ClearDraft(draft.DraftId, 20, diag),
                "The outside balance cannot cover the draft — it stands, loudly.");
            Assert.AreEqual(BankDraftStatus.Outstanding, draft.Status);
            Assert.AreEqual(5000, corr.DraftsOutstandingCents(), "Uncovered drafts stay liabilities.");
        }

        // ---------- agency collections ----------

        [Test]
        public void LocalCollectionPaysCustomerNetOfStatedFee()
        {
            PromissoryNote paper = MakePaper("Local Farmer", "Local Merchant", 3000);
            Assert.IsNull(collections.AcceptForCollection("Local Merchant", "Local Farmer", "",
                paper.InstrumentId, 60, "2% collection fee", 60, 5, diag));
            Assert.AreEqual("First Bank of Town", paper.HolderName, "Held as agent for the customer.");

            CollectionItem item = new List<CollectionItem>(collections.Items)[0];
            Assert.IsNull(collections.CollectLocalItem(item.ItemId, 20, diag));
            Assert.AreEqual(2940, collections.CollectionObligationsCents(), "Face less the stated fee — a liability.");

            Assert.IsNull(collections.PayCustomer(item.ItemId, null, 21, diag));
            Assert.AreEqual(0, collections.CollectionObligationsCents());
            Assert.AreEqual(CollectionItemStatus.PaidToCustomer, item.Status);
            Assert.AreEqual(CreditInstrumentStatus.Satisfied, paper.Status);

            BankBalanceSheet sheet = book.BuildBalanceSheet();
            Assert.IsTrue(sheet.Balances());
            Assert.AreEqual(60, sheet.UndistributedProfitsCents(), "The collection fee is real income — derived.");
        }

        [Test]
        public void OutOfTownPaperNeedsACorrespondent()
        {
            PromissoryNote paper = MakePaper("Yankton Miller", "Local Merchant", 5000);
            Assert.IsNull(collections.AcceptForCollection("Local Merchant", "Yankton Miller", "Yankton",
                paper.InstrumentId, 100, "2% collection fee", 60, 5, diag));
            CollectionItem item = new List<CollectionItem>(collections.Items)[0];
            Assert.IsNotNull(collections.SendForCollection(item.ItemId, null, 6, diag),
                "Out-of-town paper cannot be collected by wishing (Canon §18.11).");
            Assert.AreEqual(CollectionItemStatus.Received, item.Status, "Refused sends change nothing.");
        }

        [Test]
        public void FullOutOfTownCollectionCycle()
        {
            Assert.IsNull(corr.RemitToCorrespondent(10000, bank, 4, diag));
            PromissoryNote paper = MakePaper("Yankton Miller", "Local Merchant", 5000);
            Assert.IsNull(collections.AcceptForCollection("Local Merchant", "Yankton Miller", "Yankton",
                paper.InstrumentId, 100, "2% collection fee", 60, 5, diag));
            CollectionItem item = new List<CollectionItem>(collections.Items)[0];

            Assert.IsNull(collections.SendForCollection(item.ItemId, corr, 6, diag));
            Assert.AreEqual(CollectionItemStatus.SentForCollection, item.Status);
            Assert.AreEqual(13, item.SettlementDayIndex, "Proceeds expected after the collection float.");

            Assert.IsNull(collections.SettleCollection(item.ItemId, true, corr, 13, diag));
            Assert.AreEqual(15000, corr.BalanceCents, "Proceeds land as outside balance — never conjured vault cash.");
            Assert.AreEqual(4900, collections.CollectionObligationsCents());

            Assert.IsNull(collections.PayCustomer(item.ItemId, corr, 14, diag));
            Assert.AreEqual(10100, corr.BalanceCents);
            Assert.AreEqual(0, collections.CollectionObligationsCents());
            Assert.AreEqual(CollectionItemStatus.PaidToCustomer, item.Status);

            BankBalanceSheet sheet = book.BuildBalanceSheet();
            Assert.IsTrue(sheet.Balances());
            Assert.AreEqual(100, sheet.UndistributedProfitsCents(), "The collection fee, derived.");
            Assert.AreEqual(50100, sheet.TotalAssetsCents(), "40000c ledger cash + 10100c outside balance.");
        }

        [Test]
        public void DefaultedCollectionReturnsPaperWithNoFee()
        {
            Assert.IsNull(corr.RemitToCorrespondent(10000, bank, 4, diag));
            PromissoryNote paper = MakePaper("Yankton Miller", "Local Merchant", 5000);
            Assert.IsNull(collections.AcceptForCollection("Local Merchant", "Yankton Miller", "Yankton",
                paper.InstrumentId, 100, "2% collection fee", 60, 5, diag));
            CollectionItem item = new List<CollectionItem>(collections.Items)[0];
            Assert.IsNull(collections.SendForCollection(item.ItemId, corr, 6, diag));

            Assert.IsNull(collections.SettleCollection(item.ItemId, false, corr, 13, diag));
            Assert.AreEqual(CollectionItemStatus.Defaulted, item.Status);
            Assert.AreEqual("Local Merchant", paper.HolderName, "The paper goes back to its owner.");
            Assert.AreEqual(0, collections.CollectionObligationsCents(), "No fee on paper that did not pay.");
            Assert.AreEqual(10000, corr.BalanceCents, "Nothing arrived — nothing was credited.");
            Assert.IsTrue(book.BuildBalanceSheet().Balances());
        }

        [Test]
        public void PayCustomerRefusedBeforeCollection()
        {
            PromissoryNote paper = MakePaper("Local Farmer", "Local Merchant", 3000);
            Assert.IsNull(collections.AcceptForCollection("Local Merchant", "Local Farmer", "",
                paper.InstrumentId, 60, "2% collection fee", 60, 5, diag));
            CollectionItem item = new List<CollectionItem>(collections.Items)[0];
            Assert.IsNotNull(collections.PayCustomer(item.ItemId, null, 6, diag),
                "Nothing arrived — the customer is paid from what actually arrived, never advanced.");
        }

        [Test]
        public void CollectionFeeTermsAreStatedUpFront()
        {
            PromissoryNote paper = MakePaper("Local Farmer", "Local Merchant", 3000);
            Assert.IsNotNull(collections.AcceptForCollection("Local Merchant", "Local Farmer", "",
                paper.InstrumentId, -10, "negative fee", 60, 5, diag),
                "The fee cannot be negative.");
            Assert.IsNotNull(collections.AcceptForCollection("Local Farmer", "Local Farmer", "",
                paper.InstrumentId, 60, "terms", 60, 5, diag),
                "A maker does not leave their own note for collection — they pay it.");
        }

        // ---------- save round-trips ----------

        [Test]
        public void DepthSaveRoundTrips()
        {
            PromissoryNote paper = MakePaper("Miller", "Storekeeper", 10000);
            Assert.IsNull(desk.DiscountCustomerPaper("Storekeeper", paper.InstrumentId, 9000, "terms", 6, book, diag));
            Assert.IsNull(corr.RemitToCorrespondent(5000, bank, 5, diag));
            Assert.IsNull(corr.SellDraft("Traveler Tom", "Yankton Merchant", 1000, 25, "fee", bank, 6, diag));
            PromissoryNote paper2 = MakePaper("Local Farmer", "Local Merchant", 3000);
            Assert.IsNull(collections.AcceptForCollection("Local Merchant", "Local Farmer", "",
                paper2.InstrumentId, 60, "2%", 60, 5, diag));

            var bookDto = book.ToSaveDto();
            var corrDto = corr.ToSaveDto();
            var collDto = collections.ToSaveDto();

            var book2 = new BankLoanBook(bank, credit);
            book2.LoadFromSaveDto(bookDto);
            var corr2 = new BankCorrespondentAccount();
            corr2.LoadFromSaveDto(corrDto);
            var coll2 = new BankCollectionsDesk(bank, credit);
            coll2.LoadFromSaveDto(collDto);

            Assert.AreEqual(50000, book2.PaidCapitalCents);
            Assert.AreEqual(9000, book2.LiveBookValueCents(BankLoanAssetKind.DiscountedPaper));

            var desk2 = new NoteDesk(bank, credit, ids);
            desk2.LoadFromSaveDto(desk.ToSaveDto());
            DiscountedPaper restored = new List<DiscountedPaper>(desk2.DiscountedPaper)[0];
            Assert.AreEqual("Storekeeper", restored.CustomerEndorserName, "Recourse fields serialize with the paper.");
            Assert.IsTrue(restored.HasRecourse);
            Assert.IsFalse(string.IsNullOrWhiteSpace(restored.GuarantyInstrumentId));
            Assert.AreEqual(5000, corr2.BalanceCents);
            Assert.AreEqual(1000, corr2.DraftsOutstandingCents());
            Assert.AreEqual(1, new List<CollectionItem>(coll2.Items).Count);
            Assert.AreEqual(7, coll2.CollectionFloatDays);
        }
    }
}
