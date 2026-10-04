using System.Collections.Generic;
using System.Linq;
using LandLedgers.Economy.Bank;
using LandLedgers.Economy.Financing;
using NUnit.Framework;

namespace LandLedgers.EditorTests.Economy
{
    /// <summary>
    /// D4A: interbank balances between banks (Canon §16.6, §18.8, §18.11).
    /// Deposits one bank holds at another are ASSETS of the depositor and
    /// LIABILITIES of the holder (the NX-3B doctrine applies to banks as
    /// depositors too). Paper presented across banks becomes an explicit
    /// claim; periodic bilateral netting folds claims into positions that
    /// settle in full in specie lots with provenance, or by offset.
    /// HARD CONSTRAINT (D4A): no money creation — every refusal below is
    /// the constraint working.
    /// </summary>
    [TestFixture]
    public sealed class InterbankBalancesTests
    {
        private List<string> diag;
        private BankRuntime bankA;
        private BankRuntime bankB;
        private InterbankSettlement service;

        [SetUp]
        public void SetUp()
        {
            diag = new List<string>();
            bankA = MakeBank("bank-a", "First Bank of Arnprior", 100000);
            bankB = MakeBank("bank-b", "Second Bank of Arnprior", 100000);
            service = new InterbankSettlement();
            Assert.IsNull(service.RegisterBank(bankA, diag));
            Assert.IsNull(service.RegisterBank(bankB, diag));
        }

        private BankRuntime MakeBank(string id, string name, int capitalCents)
        {
            var bank = new BankRuntime(id, name, "Kennedy Baldwin-ooms");
            Assert.IsNull(bank.EstablishVault("iron safe", false, true, diag));
            Assert.IsNull(bank.RecordOpeningCapital(capitalCents, "owner savings",
                new List<SpecieLot>
                {
                    new SpecieLot { Kind = VaultSpecieKind.GoldCoin, KindName = "gold coin", AmountCents = capitalCents, SourceName = "owner savings" },
                }, 0, diag));
            return bank;
        }

        private void OpenCustomerAccount(BankRuntime bank, string accountId, string name, int amountCents)
        {
            Assert.NotNull(bank.Ledger.OpenAccount(accountId, name, DepositKind.Demand, amountCents, 0, diag: diag));
            Assert.IsNull(bank.ReceiveVaultLot(new SpecieLot
            {
                Kind = VaultSpecieKind.GoldCoin, KindName = "gold coin",
                AmountCents = amountCents, SourceName = "customer " + name,
            }, 0, diag));
        }

        private int CustomerBalance(BankRuntime bank, string accountId)
        {
            foreach (DepositAccount account in bank.Ledger.Accounts)
                if (account.AccountId == accountId)
                    return account.BalanceCents;
            Assert.Fail($"No account '{accountId}'.");
            return 0;
        }

        // ---------- interbank deposit accounts ----------

        [Test]
        public void OpenInterbankDeposit_MovesRealCashBothSides()
        {
            Assert.IsNull(service.OpenInterbankDeposit("bank-a", "bank-b", "IB-A-AT-B",
                DepositKind.Demand, 30000, 5, diag: diag));

            // Depositor: cash and vault both down — real money left.
            Assert.AreEqual(70000, bankA.Ledger.CashOnHandCents);
            Assert.AreEqual(70000, bankA.VaultSpecieTotalCents());
            // Holder: cash and vault both up, and it OWES the money (NX-3B doctrine).
            Assert.AreEqual(130000, bankB.Ledger.CashOnHandCents);
            Assert.AreEqual(130000, bankB.VaultSpecieTotalCents());
            Assert.AreEqual(30000, bankB.Ledger.DepositsOwedCents(),
                "The holding bank owes the depositing bank — a bank depositor is still a depositor.");

            InterbankDepositAsset asset = service.DepositAssets.Single(a => a.AssetId == "IB-A-AT-B");
            Assert.AreEqual("bank-a", asset.DepositingBankId);
            Assert.AreEqual("bank-b", asset.HoldingBankId);
            Assert.AreEqual(30000, asset.BalanceCents);
            Assert.AreEqual(30000, service.InterbankAssetsCents("bank-a"),
                "The depositing bank carries an asset.");
            Assert.AreEqual(70000 + 130000, bankA.Ledger.CashOnHandCents + bankB.Ledger.CashOnHandCents,
                "No money created or destroyed in the move.");
        }

        [Test]
        public void OpenInterbankDeposit_RefusedWhenDepositorShort()
        {
            string refused = service.OpenInterbankDeposit("bank-a", "bank-b", "IB-A-AT-B",
                DepositKind.Demand, 300000, 5, diag: diag);
            Assert.IsNotNull(refused, "A bank cannot deposit money it does not hold.");
            Assert.AreEqual(100000, bankA.Ledger.CashOnHandCents, "Refused deposits move nothing.");
            Assert.AreEqual(100000, bankA.VaultSpecieTotalCents());
            Assert.AreEqual(0, service.DepositAssets.Count);
            Assert.AreEqual(100000, bankB.Ledger.CashOnHandCents);
        }

        [Test]
        public void OpenInterbankDeposit_DuplicateAccountRefused()
        {
            Assert.IsNull(service.OpenInterbankDeposit("bank-a", "bank-b", "IB-A-AT-B",
                DepositKind.Demand, 30000, 5, diag: diag));
            string refused = service.OpenInterbankDeposit("bank-a", "bank-b", "IB-A-AT-B",
                DepositKind.Demand, 10000, 6, diag: diag);
            Assert.IsNotNull(refused, "One account id, one account.");
            Assert.AreEqual(70000, bankA.Ledger.CashOnHandCents, "The duplicate moved nothing.");
        }

        [Test]
        public void OpenInterbankDeposit_SelfDepositRefused()
        {
            Assert.IsNotNull(service.OpenInterbankDeposit("bank-a", "bank-a", "IB-X",
                DepositKind.Demand, 10000, 5, diag: diag),
                "A bank does not hold interbank deposits with itself.");
        }

        [Test]
        public void AddAndDraw_MoveBothViewsTogether()
        {
            Assert.IsNull(service.OpenInterbankDeposit("bank-a", "bank-b", "IB-A-AT-B",
                DepositKind.Demand, 30000, 5, diag: diag));
            Assert.IsNull(service.AddToInterbankDeposit("IB-A-AT-B", 10000, 6, diag));

            InterbankDepositAsset asset = service.DepositAssets.Single(a => a.AssetId == "IB-A-AT-B");
            Assert.AreEqual(40000, asset.BalanceCents);
            Assert.AreEqual(60000, bankA.Ledger.CashOnHandCents);
            Assert.AreEqual(140000, bankB.Ledger.CashOnHandCents);
            Assert.AreEqual(40000, bankB.Ledger.DepositsOwedCents());

            Assert.IsNull(service.DrawFromInterbankDeposit("IB-A-AT-B", 12000, 7, false, diag));
            Assert.AreEqual(28000, asset.BalanceCents);
            Assert.AreEqual(72000, bankA.Ledger.CashOnHandCents, "Drawn money comes home as real cash.");
            Assert.AreEqual(72000, bankA.VaultSpecieTotalCents());
            Assert.AreEqual(128000, bankB.Ledger.CashOnHandCents);
            Assert.AreEqual(128000, bankB.VaultSpecieTotalCents());
            Assert.AreEqual(28000, bankB.Ledger.DepositsOwedCents());
        }

        [Test]
        public void DrawFromInterbankDeposit_RefusedBeyondBalance()
        {
            Assert.IsNull(service.OpenInterbankDeposit("bank-a", "bank-b", "IB-A-AT-B",
                DepositKind.Demand, 30000, 5, diag: diag));
            Assert.IsNotNull(service.DrawFromInterbankDeposit("IB-A-AT-B", 30001, 7, false, diag),
                "A bank cannot draw more than it holds.");
            Assert.AreEqual(30000, service.DepositAssets.Single(a => a.AssetId == "IB-A-AT-B").BalanceCents);
        }

        [Test]
        public void DrawFromInterbankDeposit_TermLockRespected()
        {
            Assert.IsNull(service.OpenInterbankDeposit("bank-a", "bank-b", "IB-TERM",
                DepositKind.Term, 30000, 5, 90, 0, diag));

            string refused = service.DrawFromInterbankDeposit("IB-TERM", 30000, 10, false, diag);
            Assert.IsNotNull(refused, "Term deposits lock to maturity — interbank money included.");

            Assert.IsNull(service.DrawFromInterbankDeposit("IB-TERM", 30000, 10, true, diag),
                "With explicit early-withdrawal acceptance, the money comes home.");
            Assert.AreEqual(0, service.DepositAssets.Single(a => a.AssetId == "IB-TERM").BalanceCents);
            Assert.AreEqual(100000, bankA.Ledger.CashOnHandCents);
            Assert.AreEqual(100000, bankB.Ledger.CashOnHandCents);
        }

        // ---------- presentment: paper becomes a claim ----------

        [Test]
        public void PresentCheck_Credited_DebitsDrawerAndCreatesClaim()
        {
            OpenCustomerAccount(bankA, "a-drawer", "Miller", 20000);
            OpenCustomerAccount(bankB, "b-payee", "Smith", 15000);

            string refused = service.PresentInterbankPaper("bank-b", "bank-a",
                InterbankPaperKind.Check, "a-drawer", "b-payee", false,
                "Smith", 5000, "CHK-101", 10, out List<SpecieLot> lots, diag);
            Assert.IsNull(refused);
            Assert.IsNull(lots, "Credited presentment pays no cash.");

            // Drawee: the drawer's liability fell; the bank's own cash did not move.
            Assert.AreEqual(15000, CustomerBalance(bankA, "a-drawer"));
            Assert.AreEqual(120000, bankA.Ledger.CashOnHandCents,
                "The drawee paid no cash — the obligation migrated to the claim.");
            Assert.AreEqual(120000, bankA.VaultSpecieTotalCents());
            // Presenter: the payee gained a real deposit liability; the bank's cash did not move.
            Assert.AreEqual(20000, CustomerBalance(bankB, "b-payee"));
            Assert.AreEqual(115000, bankB.Ledger.CashOnHandCents,
                "No cash crossed the counter — the offsetting asset is the claim.");
            Assert.AreEqual(115000, bankB.VaultSpecieTotalCents());

            InterbankClaim claim = service.Claims.Single();
            Assert.AreEqual("bank-a", claim.DebtorBankId);
            Assert.AreEqual("bank-b", claim.CreditorBankId);
            Assert.AreEqual(5000, claim.AmountCents);
            Assert.AreEqual(InterbankPaperKind.Check, claim.PaperKind);
            Assert.AreEqual("a-drawer", claim.DrawerAccountId);
            Assert.AreEqual(InterbankClaimStatus.Open, claim.Status);
            Assert.AreEqual(5000, service.InterbankClaimsOwedToBankCents("bank-b"));
            Assert.AreEqual(5000, service.InterbankClaimsOwedByBankCents("bank-a"));
        }

        [Test]
        public void PresentCheck_BouncesWhenDrawerShort()
        {
            OpenCustomerAccount(bankA, "a-drawer", "Miller", 20000);
            OpenCustomerAccount(bankB, "b-payee", "Smith", 15000);

            string refused = service.PresentInterbankPaper("bank-b", "bank-a",
                InterbankPaperKind.Check, "a-drawer", "b-payee", false,
                "Smith", 25000, "CHK-102", 10, out _, diag);
            Assert.IsNotNull(refused, "A check the drawer cannot honor creates no claim.");
            Assert.AreEqual(20000, CustomerBalance(bankA, "a-drawer"), "The bounced check debited nothing.");
            Assert.AreEqual(120000, bankA.Ledger.CashOnHandCents);
            Assert.AreEqual(15000, CustomerBalance(bankB, "b-payee"), "The payee was not credited.");
            Assert.AreEqual(0, service.Claims.Count);
        }

        [Test]
        public void PresentCheck_PaidInCash_ReturnsLotsWithProvenance()
        {
            OpenCustomerAccount(bankA, "a-drawer", "Miller", 20000);

            string refused = service.PresentInterbankPaper("bank-b", "bank-a",
                InterbankPaperKind.Check, "a-drawer", null, true,
                "Smith", 5000, "CHK-103", 10, out List<SpecieLot> lots, diag);
            Assert.IsNull(refused);
            Assert.IsNotNull(lots);
            Assert.AreEqual(5000, lots.Sum(l => l.AmountCents));
            Assert.IsTrue(lots.All(l => !string.IsNullOrWhiteSpace(l.LotId) && !string.IsNullOrWhiteSpace(l.SourceName)),
                "Every lot the payee walks away with carries its provenance.");

            Assert.AreEqual(95000, bankB.Ledger.CashOnHandCents, "The presenting bank paid real cash.");
            Assert.AreEqual(95000, bankB.VaultSpecieTotalCents());
            Assert.AreEqual(15000, CustomerBalance(bankA, "a-drawer"));
            Assert.AreEqual(1, service.Claims.Count);
        }

        [Test]
        public void PresentDraft_ObligatesIssuingBankDirectly()
        {
            // No customer accounts anywhere: a draft is drawn on the bank itself.
            string refused = service.PresentInterbankPaper("bank-b", "bank-a",
                InterbankPaperKind.BankDraft, "", null, true,
                "Payee", 8000, "DRAFT-7", 10, out List<SpecieLot> lots, diag);
            Assert.IsNull(refused);
            Assert.AreEqual(8000, lots.Sum(l => l.AmountCents));

            InterbankClaim claim = service.Claims.Single();
            Assert.AreEqual(InterbankPaperKind.BankDraft, claim.PaperKind);
            Assert.AreEqual(string.Empty, claim.DrawerAccountId, "No customer account is touched by a draft.");
            Assert.AreEqual(100000, bankA.Ledger.CashOnHandCents, "The drawee bank's cash is untouched until settlement.");
            Assert.AreEqual(92000, bankB.Ledger.CashOnHandCents);

            string withDrawer = service.PresentInterbankPaper("bank-b", "bank-a",
                InterbankPaperKind.BankDraft, "some-account", null, true,
                "Payee", 1000, "DRAFT-8", 10, out _, diag);
            Assert.IsNotNull(withDrawer, "A draft does not name a customer drawer.");
        }

        [Test]
        public void PresentPaper_Refusals()
        {
            OpenCustomerAccount(bankA, "a-drawer", "Miller", 20000);
            OpenCustomerAccount(bankB, "b-payee", "Smith", 15000);

            Assert.IsNotNull(service.PresentInterbankPaper("bank-a", "bank-a",
                InterbankPaperKind.Check, "a-drawer", "b-payee", false, "Smith", 1000, "CHK-X", 10, out _, diag),
                "A bank does not present paper to itself.");
            Assert.IsNotNull(service.PresentInterbankPaper("bank-b", "bank-zzz",
                InterbankPaperKind.Check, "a-drawer", "b-payee", false, "Smith", 1000, "CHK-X", 10, out _, diag),
                "Unknown banks settle nothing.");
            Assert.IsNotNull(service.PresentInterbankPaper("bank-b", "bank-a",
                InterbankPaperKind.Check, "a-drawer", "b-payee", false, "Smith", 0, "CHK-X", 10, out _, diag),
                "Paper for nothing is not paper.");
            Assert.IsNotNull(service.PresentInterbankPaper("bank-b", "bank-a",
                InterbankPaperKind.Check, "", "b-payee", false, "Smith", 1000, "CHK-X", 10, out _, diag),
                "A check without a named drawer is not presentable.");
            Assert.IsNotNull(service.PresentInterbankPaper("bank-b", "bank-a",
                InterbankPaperKind.Check, "a-drawer", null, false, "Smith", 1000, "CHK-X", 10, out _, diag),
                "Crediting needs the payee's account.");
            Assert.AreEqual(0, service.Claims.Count, "Every refusal left no claim behind.");
        }

        // ---------- periodic settlement netting ----------

        private InterbankSettlementPosition NetTwoWayClaims()
        {
            OpenCustomerAccount(bankA, "a-drawer", "Miller", 20000);
            OpenCustomerAccount(bankA, "a-payee", "Adams", 15000);
            OpenCustomerAccount(bankB, "b-drawer", "Baker", 20000);
            OpenCustomerAccount(bankB, "b-payee", "Smith", 15000);

            // B presents on A: A owes B 5000.
            Assert.IsNull(service.PresentInterbankPaper("bank-b", "bank-a",
                InterbankPaperKind.Check, "a-drawer", "b-payee", false, "Smith", 5000, "CHK-1", 10, out _, diag));
            // A presents on B: B owes A 2000.
            Assert.IsNull(service.PresentInterbankPaper("bank-a", "bank-b",
                InterbankPaperKind.Check, "b-drawer", "a-payee", false, "Adams", 2000, "CHK-2", 11, out _, diag));

            List<InterbankSettlementPosition> made = service.RunSettlementCycle(20, diag);
            Assert.AreEqual(1, made.Count, "Two-way claims net to one position.");
            return made[0];
        }

        [Test]
        public void RunSettlementCycle_NetsBilateralClaims()
        {
            InterbankSettlementPosition position = NetTwoWayClaims();
            Assert.AreEqual("bank-a", position.DebtorBankId);
            Assert.AreEqual("bank-b", position.CreditorBankId);
            Assert.AreEqual(3000, position.NetAmountCents, "5000 one way, 2000 the other: 3000 net.");
            Assert.AreEqual(2, position.ComprisedClaimIds.Count);
            Assert.AreEqual(InterbankPositionStatus.Open, position.Status);
            Assert.IsTrue(service.Claims.All(c => c.Status == InterbankClaimStatus.Netted),
                "Netted claims are no longer individually payable.");
            // Netting moves no cash — only the position records the obligation.
            Assert.AreEqual(135000, bankA.Ledger.CashOnHandCents);
            Assert.AreEqual(135000, bankB.Ledger.CashOnHandCents);
        }

        [Test]
        public void RunSettlementCycle_ZeroNet_RetiresClaimsWithoutPosition()
        {
            OpenCustomerAccount(bankA, "a-drawer", "Miller", 20000);
            OpenCustomerAccount(bankA, "a-payee", "Adams", 15000);
            OpenCustomerAccount(bankB, "b-drawer", "Baker", 20000);
            OpenCustomerAccount(bankB, "b-payee", "Smith", 15000);

            Assert.IsNull(service.PresentInterbankPaper("bank-b", "bank-a",
                InterbankPaperKind.Check, "a-drawer", "b-payee", false, "Smith", 5000, "CHK-1", 10, out _, diag));
            Assert.IsNull(service.PresentInterbankPaper("bank-a", "bank-b",
                InterbankPaperKind.Check, "b-drawer", "a-payee", false, "Adams", 5000, "CHK-2", 11, out _, diag));

            List<InterbankSettlementPosition> made = service.RunSettlementCycle(20, diag);
            Assert.AreEqual(0, made.Count, "A zero net needs no position.");
            Assert.IsTrue(service.Claims.All(c => c.Status == InterbankClaimStatus.Netted));
            Assert.AreEqual(0, service.Positions.Count);
        }

        [Test]
        public void RunSettlementCycle_NoOpenClaims_NothingToNet()
        {
            List<InterbankSettlementPosition> made = service.RunSettlementCycle(20, diag);
            Assert.AreEqual(0, made.Count);
        }

        // ---------- settlement in specie ----------

        [Test]
        public void SettlePositionInSpecie_MovesLotsWithProvenance()
        {
            InterbankSettlementPosition position = NetTwoWayClaims();
            int aCashBefore = bankA.Ledger.CashOnHandCents;
            int bCashBefore = bankB.Ledger.CashOnHandCents;

            Assert.IsNull(service.SettlePositionInSpecie(position.PositionId, 21, diag));

            Assert.AreEqual(aCashBefore - 3000, bankA.Ledger.CashOnHandCents);
            Assert.AreEqual(aCashBefore - 3000, bankA.VaultSpecieTotalCents());
            Assert.AreEqual(bCashBefore + 3000, bankB.Ledger.CashOnHandCents);
            Assert.AreEqual(bCashBefore + 3000, bankB.VaultSpecieTotalCents());
            Assert.IsTrue(bankB.VaultLots.Any(l => l.LotId.StartsWith("SPECIE-bank-a-")),
                "The creditor's vault holds the debtor's actual lots — provenance travels with the coins.");
            Assert.IsTrue(bankB.VaultLots.All(l => !string.IsNullOrWhiteSpace(l.SourceName)));

            Assert.AreEqual(InterbankPositionStatus.Settled, position.Status);
            Assert.AreEqual(21, position.SettledDayIndex);
            Assert.IsTrue(service.Claims.All(c => c.Status == InterbankClaimStatus.Settled),
                "Settling the position retires every comprised claim.");
            Assert.AreEqual(0, service.InterbankClaimsOwedByBankCents("bank-a"));
        }

        [Test]
        public void SettlePositionInSpecie_RefusedWhenDebtorShort()
        {
            InterbankSettlementPosition position = NetTwoWayClaims();
            // Drain the debtor's ledger cash below the position amount.
            Assert.IsNull(bankA.Ledger.DisburseLoan(bankA.Ledger.CashOnHandCents - 1000, "drain", diag));

            string refused = service.SettlePositionInSpecie(position.PositionId, 21, diag);
            Assert.IsNotNull(refused, "A shortfall is refused loudly — never invented, never advanced.");
            Assert.AreEqual(InterbankPositionStatus.Open, position.Status, "The debt stands; the creditor waits.");
            Assert.IsTrue(service.Claims.All(c => c.Status == InterbankClaimStatus.Netted));
            Assert.AreEqual(1000, bankA.Ledger.CashOnHandCents, "The refused settlement moved nothing.");
        }

        [Test]
        public void SettlePositionInSpecie_RefusedWhenVaultShort()
        {
            // A bank whose books say cash but whose vault is empty cannot settle in specie.
            var cashOnly = new BankRuntime("bank-c", "Cash Only Bank", "Kennedy Baldwin-ooms");
            Assert.IsNull(cashOnly.EstablishVault("iron safe", false, true, diag));
            Assert.IsNull(cashOnly.Ledger.RecordCapitalInflow(50000, "owner savings", diag));
            Assert.IsNull(service.RegisterBank(cashOnly, diag));
            // The drawer account exists on the books, but deliberately with NO
            // vault lot: the books say cash, the vault is empty.
            Assert.NotNull(cashOnly.Ledger.OpenAccount("c-drawer", "Miller", DepositKind.Demand, 20000, 0, diag: diag));
            OpenCustomerAccount(bankB, "b-payee", "Smith", 15000);

            Assert.IsNull(service.PresentInterbankPaper("bank-b", "bank-c",
                InterbankPaperKind.Check, "c-drawer", "b-payee", false, "Smith", 5000, "CHK-9", 10, out _, diag));
            InterbankSettlementPosition position =
                service.RunSettlementCycle(20, diag).Single(p => p.DebtorBankId == "bank-c");

            string refused = service.SettlePositionInSpecie(position.PositionId, 21, diag);
            Assert.IsNotNull(refused, "Specie settlement needs specie — the books alone do not pay.");
            Assert.AreEqual(InterbankPositionStatus.Open, position.Status);
        }

        [Test]
        public void SettlePositionInSpecie_UnknownPositionRefused()
        {
            Assert.IsNotNull(service.SettlePositionInSpecie("IPOS-0000-0000", 21, diag));
        }

        // ---------- offset against an interbank deposit ----------

        [Test]
        public void OffsetPositionAgainstDeposit_CancelsBothObligations()
        {
            // A holds 10000 at B; then A owes B 4000 on presented paper.
            Assert.IsNull(service.OpenInterbankDeposit("bank-a", "bank-b", "IB-A-AT-B",
                DepositKind.Demand, 10000, 5, diag: diag));
            OpenCustomerAccount(bankA, "a-drawer", "Miller", 20000);
            OpenCustomerAccount(bankB, "b-payee", "Smith", 15000);
            Assert.IsNull(service.PresentInterbankPaper("bank-b", "bank-a",
                InterbankPaperKind.Check, "a-drawer", "b-payee", false, "Smith", 4000, "CHK-5", 10, out _, diag));

            InterbankSettlementPosition position = service.RunSettlementCycle(20, diag).Single();
            Assert.AreEqual(4000, position.NetAmountCents);

            int bCashBefore = bankB.Ledger.CashOnHandCents;
            Assert.IsNull(service.OffsetPositionAgainstDeposit(position.PositionId, "IB-A-AT-B", 21, false, diag));

            InterbankDepositAsset asset = service.DepositAssets.Single(a => a.AssetId == "IB-A-AT-B");
            Assert.AreEqual(6000, asset.BalanceCents, "The deposit absorbed the claim.");
            Assert.AreEqual(6000, CustomerBalance(bankB, "IB-A-AT-B"), "Liability and asset fell together.");
            Assert.AreEqual(bCashBefore, bankB.Ledger.CashOnHandCents,
                "An offset moves no cash — obligations cancel, specie stays.");
            Assert.AreEqual(InterbankPositionStatus.Settled, position.Status);
            Assert.IsTrue(service.Claims.All(c => c.Status == InterbankClaimStatus.Settled));
        }

        [Test]
        public void OffsetPositionAgainstDeposit_RefusedWhenDepositShort()
        {
            Assert.IsNull(service.OpenInterbankDeposit("bank-a", "bank-b", "IB-A-AT-B",
                DepositKind.Demand, 2000, 5, diag: diag));
            OpenCustomerAccount(bankA, "a-drawer", "Miller", 20000);
            OpenCustomerAccount(bankB, "b-payee", "Smith", 15000);
            Assert.IsNull(service.PresentInterbankPaper("bank-b", "bank-a",
                InterbankPaperKind.Check, "a-drawer", "b-payee", false, "Smith", 4000, "CHK-5", 10, out _, diag));
            InterbankSettlementPosition position = service.RunSettlementCycle(20, diag).Single();

            Assert.IsNotNull(service.OffsetPositionAgainstDeposit(position.PositionId, "IB-A-AT-B", 21, false, diag),
                "Offset covers the whole position or nothing — no partials.");
            Assert.AreEqual(InterbankPositionStatus.Open, position.Status);
            Assert.AreEqual(2000, service.DepositAssets.Single(a => a.AssetId == "IB-A-AT-B").BalanceCents);
        }

        // ---------- the hard constraint: conservation ----------

        [Test]
        public void NoMoneyCreation_ConservationAcrossFullCycle()
        {
            OpenCustomerAccount(bankA, "a-drawer", "Miller", 20000);
            OpenCustomerAccount(bankA, "a-payee", "Adams", 15000);
            OpenCustomerAccount(bankB, "b-drawer", "Baker", 20000);
            OpenCustomerAccount(bankB, "b-payee", "Smith", 15000);
            int baseCash = bankA.Ledger.CashOnHandCents + bankB.Ledger.CashOnHandCents;
            int baseVault = bankA.VaultSpecieTotalCents() + bankB.VaultSpecieTotalCents();
            Assert.AreEqual(270000, baseCash);

            Assert.IsNull(service.OpenInterbankDeposit("bank-a", "bank-b", "IB-A-AT-B",
                DepositKind.Demand, 30000, 5, diag: diag));
            Assert.IsNull(service.AddToInterbankDeposit("IB-A-AT-B", 10000, 6, diag));
            Assert.IsNull(service.DrawFromInterbankDeposit("IB-A-AT-B", 12000, 7, false, diag));
            Assert.IsNull(service.PresentInterbankPaper("bank-b", "bank-a",
                InterbankPaperKind.Check, "a-drawer", "b-payee", false, "Smith", 5000, "CHK-1", 10, out _, diag));
            Assert.IsNull(service.PresentInterbankPaper("bank-a", "bank-b",
                InterbankPaperKind.Check, "b-drawer", "a-payee", false, "Adams", 2000, "CHK-2", 11, out _, diag));

            List<InterbankSettlementPosition> made = service.RunSettlementCycle(20, diag);
            foreach (InterbankSettlementPosition position in made)
                Assert.IsNull(service.SettlePositionInSpecie(position.PositionId, 21, diag));

            Assert.AreEqual(baseCash, bankA.Ledger.CashOnHandCents + bankB.Ledger.CashOnHandCents,
                "Ledger cash is conserved through deposits, claims, netting and specie settlement.");
            Assert.AreEqual(baseVault, bankA.VaultSpecieTotalCents() + bankB.VaultSpecieTotalCents(),
                "Vault specie is conserved — lots move, none are minted.");
        }

        [Test]
        public void CashPaidPresentment_MovesMoneyOutToThePayee()
        {
            OpenCustomerAccount(bankA, "a-drawer", "Miller", 20000);
            int baseCash = bankA.Ledger.CashOnHandCents + bankB.Ledger.CashOnHandCents;

            Assert.IsNull(service.PresentInterbankPaper("bank-b", "bank-a",
                InterbankPaperKind.Check, "a-drawer", null, true,
                "Smith", 5000, "CHK-9", 10, out List<SpecieLot> lots, diag));

            Assert.AreEqual(baseCash - 5000, bankA.Ledger.CashOnHandCents + bankB.Ledger.CashOnHandCents,
                "Cash paid to the payee leaves the banking system — a transfer out, not creation.");
            Assert.AreEqual(5000, lots.Sum(l => l.AmountCents), "…and the payee holds exactly what the banks lost.");
        }

        // ---------- interest mirror ----------

        [Test]
        public void SettlementCycle_SyncsAssetInterestViewToLiability()
        {
            Assert.IsNull(service.OpenInterbankDeposit("bank-a", "bank-b", "IB-TERM",
                DepositKind.Term, 30000, 5, 90, 500, diag));

            bankB.Ledger.AccrueInterestDay(diag);
            bankB.Ledger.AccrueInterestDay(diag);
            bankB.Ledger.AccrueInterestDay(diag);
            // 30000c at 500bps: round(30000 * 0.05 / 365) = 4c/day -> 12c.
            InterbankDepositAsset asset = service.DepositAssets.Single(a => a.AssetId == "IB-TERM");
            Assert.AreEqual(0, asset.InterestAccruedCents, "The asset view does not accrue on its own.");

            service.RunSettlementCycle(10, diag);
            Assert.AreEqual(12, asset.InterestAccruedCents, "The cycle mirrors the liability's accrual.");
            Assert.AreEqual(30012, service.InterbankAssetsCents("bank-a"));
        }

        // ---------- save / load ----------

        [Test]
        public void SaveLoad_RoundTripsInstruments()
        {
            Assert.IsNull(service.OpenInterbankDeposit("bank-a", "bank-b", "IB-A-AT-B",
                DepositKind.Demand, 30000, 5, diag: diag));
            OpenCustomerAccount(bankA, "a-drawer", "Miller", 20000);
            OpenCustomerAccount(bankB, "b-payee", "Smith", 15000);
            Assert.IsNull(service.PresentInterbankPaper("bank-b", "bank-a",
                InterbankPaperKind.Check, "a-drawer", "b-payee", false, "Smith", 5000, "CHK-1", 10, out _, diag));
            service.RunSettlementCycle(20, diag);

            InterbankSettlement.InterbankSettlementSaveDto dto = service.ToSaveDto();

            var restored = new InterbankSettlement();
            Assert.IsNull(restored.RegisterBank(bankA, diag));
            Assert.IsNull(restored.RegisterBank(bankB, diag));
            restored.LoadFromSaveDto(dto);

            Assert.AreEqual(1, restored.DepositAssets.Count);
            Assert.AreEqual(30000, restored.DepositAssets.Single().BalanceCents);
            Assert.AreEqual(1, restored.Claims.Count);
            Assert.AreEqual(InterbankClaimStatus.Netted, restored.Claims.Single().Status);
            InterbankSettlementPosition position = restored.Positions.Single();
            Assert.AreEqual("bank-a", position.DebtorBankId);
            Assert.AreEqual("bank-b", position.CreditorBankId);
            Assert.AreEqual(5000, position.NetAmountCents);
            Assert.AreEqual(InterbankPositionStatus.Open, position.Status);
        }

        [Test]
        public void RegisterBank_DuplicateRefused()
        {
            Assert.IsNotNull(service.RegisterBank(bankA, diag), "Double registration is refused.");
            Assert.IsNotNull(service.RegisterBank(null, diag));
        }
    }
}
