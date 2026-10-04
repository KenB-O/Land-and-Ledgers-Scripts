using System.Collections.Generic;
using LandLedgers.Economy.Bank;
using LandLedgers.Economy.Blacksmith;
using LandLedgers.Economy.Financing;
using LandLedgers.Primitives;
using NUnit.Framework;

namespace LandLedgers.EditorTests.Economy
{
    /// <summary>
    /// W7B: teller operations. Deposits are taken on as liabilities through
    /// the existing BankDeposits instrument; withdrawals are refused when
    /// uncovered — never faked (Canon §18.10); teller windows are workstations
    /// with throughput; end-of-day balancing counts the vault and books
    /// shortages as real losses (Canon §18.12).
    /// </summary>
    [TestFixture]
    public sealed class BankTellerVaultTests
    {
        private List<string> diag;

        [SetUp]
        public void SetUp()
        {
            diag = new List<string>();
        }

        private static EntityId Person(int n) => EntityId.For(EntityKind.Person, n);

        private static List<EquipmentAsset> WindowComponents()
        {
            return new List<EquipmentAsset>
            {
                new EquipmentAsset { AssetId = "EQ-counter-1", Kind = "teller-counter", Condition01 = 1f },
                new EquipmentAsset { AssetId = "EQ-drawer-1", Kind = "cash-drawer", Condition01 = 1f },
                new EquipmentAsset { AssetId = "EQ-scale-1", Kind = "scale-set", Condition01 = 1f },
            };
        }

        private static SpecieLot Cash(VaultSpecieKind kind, string kindName, int cents, string source)
        {
            return new SpecieLot { Kind = kind, KindName = kindName, AmountCents = cents, SourceName = source };
        }

        private (BankRuntime bank, TellerOperations tellers) OpenBankWithWindow(int capitalCents = 50000)
        {
            var bank = new BankRuntime("bank-1", "First Bank of Town", "Kennedy Baldwin-ooms");
            Assert.IsNull(bank.EstablishVault("iron safe", false, true, diag));
            Assert.IsNull(bank.RecordOpeningCapital(capitalCents, "owner savings",
                new List<SpecieLot> { Cash(VaultSpecieKind.GoldCoin, "gold coin", capitalCents, "owner savings") }, 0, diag));
            var tellers = new TellerOperations(bank);
            TellerWindow window = tellers.GetOrCreateWindow("w1");
            Assert.IsNull(window.EstablishFromComponents(WindowComponents(), "banking-house", diag));
            Assert.IsNull(tellers.OpenWindow("w1", "Teller Tom", Person(7), 0, diag));
            return (bank, tellers);
        }

        /// <summary>
        /// The honest pairing the operating layer owns: a loan moves ledger
        /// cash AND vault specie together.
        /// </summary>
        private static void DisburseLoanWithCash(BankRuntime bank, int amountCents, string loanRef, List<string> diag)
        {
            Assert.IsNull(bank.Ledger.DisburseLoan(amountCents, loanRef, diag));
            Assert.IsNotNull(bank.DrawVaultLots(amountCents, diag), "Paired disbursement draws the specie too.");
        }

        [Test]
        public void WindowNeedsAReadyStationAndANamedTeller()
        {
            var bank = new BankRuntime("bank-1", "First Bank of Town", "Kennedy Baldwin-ooms");
            var tellers = new TellerOperations(bank);
            tellers.GetOrCreateWindow("w1");
            Assert.IsNotNull(tellers.OpenWindow("w1", "Teller Tom", Person(7), 0, diag),
                "A room alone grants no window (Tech X §3.5).");
            TellerWindow window = tellers.GetOrCreateWindow("w1");
            Assert.IsNull(window.EstablishFromComponents(WindowComponents(), "banking-house", diag));
            Assert.IsNotNull(tellers.OpenWindow("w1", "", Person(7), 0, diag),
                "No anonymous hands in the drawer.");
            Assert.IsNull(tellers.OpenWindow("w1", "Teller Tom", Person(7), 0, diag));
        }

        [Test]
        public void DepositBecomesLiability()
        {
            var (bank, tellers) = OpenBankWithWindow();
            Assert.IsNull(tellers.TakeDeposit("w1", "a1", "Samuel", DepositKind.Demand,
                Cash(VaultSpecieKind.SilverCoin, "silver coin", 10000, "Samuel"), 1, diagnostics: diag));
            Assert.AreEqual(60000, bank.Ledger.CashOnHandCents);
            Assert.AreEqual(10000, bank.Ledger.DepositsOwedCents(), "Deposits are owed money (Canon §18.8).");
            Assert.AreEqual(60000, bank.VaultSpecieTotalCents());
            Assert.AreEqual(0, bank.CheckVaultReconciliation(new List<string>()));
            Assert.AreEqual(1, tellers.Windows["w1"].ServedToday);
            Assert.AreEqual(10000, tellers.Windows["w1"].ReceivedTodayCents);
        }

        [Test]
        public void RefusedDepositHandsTheCashBack()
        {
            var (bank, tellers) = OpenBankWithWindow();
            string refusal = tellers.TakeDeposit("w1", "a1", "", DepositKind.Demand,
                Cash(VaultSpecieKind.SilverCoin, "silver coin", 10000, "John Smith"), 1, diagnostics: diag);
            Assert.IsNotNull(refusal, "The depositor must be named — no anonymous deposits.");
            Assert.AreEqual(50000, bank.VaultSpecieTotalCents(), "The voided lot leaves the vault — cash handed back.");
            Assert.AreEqual(50000, bank.Ledger.CashOnHandCents);
            Assert.AreEqual(0, bank.Ledger.DepositsOwedCents());
        }

        [Test]
        public void WithdrawalPaysRealSpecie()
        {
            var (bank, tellers) = OpenBankWithWindow();
            Assert.IsNull(tellers.TakeDeposit("w1", "a1", "Samuel", DepositKind.Demand,
                Cash(VaultSpecieKind.SilverCoin, "silver coin", 40000, "Samuel"), 1, diagnostics: diag));
            List<SpecieLot> paid = tellers.PayWithdrawal("w1", "a1", 15000, 2, false, diag);
            Assert.IsNotNull(paid);
            int paidTotal = 0;
            foreach (SpecieLot lot in paid) paidTotal += lot.AmountCents;
            Assert.AreEqual(15000, paidTotal, "The customer walks away with real specie lots.");
            Assert.AreEqual(75000, bank.VaultSpecieTotalCents());
            Assert.AreEqual(75000, bank.Ledger.CashOnHandCents);
            Assert.AreEqual(25000, bank.Ledger.DepositsOwedCents());
            Assert.AreEqual(15000, tellers.Windows["w1"].PaidTodayCents);
        }

        [Test]
        public void WithdrawalRefusedWhenUncoveredNeverFaked()
        {
            var (bank, tellers) = OpenBankWithWindow();
            Assert.IsNull(tellers.TakeDeposit("w1", "a1", "Samuel", DepositKind.Demand,
                Cash(VaultSpecieKind.SilverCoin, "silver coin", 40000, "Samuel"), 1, diagnostics: diag));
            DisburseLoanWithCash(bank, 80000, "loan-1", diag);

            List<SpecieLot> paid = tellers.PayWithdrawal("w1", "a1", 20000, 2, false, diag);
            Assert.IsNull(paid, "The bank holds 10000c — a 20000c withdrawal is refused, never faked (Canon §18.10).");
            Assert.AreEqual(10000, bank.Ledger.CashOnHandCents, "Refused withdrawals move nothing.");
            Assert.AreEqual(10000, bank.VaultSpecieTotalCents(), "Refused withdrawals move nothing.");
        }

        [Test]
        public void TermDepositLocksAtTheWindow()
        {
            var (bank, tellers) = OpenBankWithWindow();
            Assert.IsNull(tellers.TakeDeposit("w1", "t1", "Widow", DepositKind.Term,
                Cash(VaultSpecieKind.GoldCoin, "gold coin", 20000, "Widow"), 1, 90, 400, diag));
            Assert.IsNull(tellers.PayWithdrawal("w1", "t1", 5000, 10, false, diag),
                "Term deposits lock to maturity — the window enforces it.");
            Assert.IsNotNull(tellers.PayWithdrawal("w1", "t1", 5000, 95, false, diag),
                "After maturity, the money is theirs.");
        }

        [Test]
        public void WindowThroughputGates()
        {
            var (bank, tellers) = OpenBankWithWindow();
            tellers.Windows["w1"].DailyCapacity = 1;
            Assert.IsNull(tellers.TakeDeposit("w1", "a1", "Samuel", DepositKind.Demand,
                Cash(VaultSpecieKind.SilverCoin, "silver coin", 1000, "Samuel"), 1, diagnostics: diag));
            Assert.IsNotNull(tellers.TakeDeposit("w1", "a2", "Ruth", DepositKind.Demand,
                Cash(VaultSpecieKind.SilverCoin, "silver coin", 1000, "Ruth"), 1, diagnostics: diag),
                "A window past capacity turns customers away until tomorrow.");
        }

        [Test]
        public void EndOfDayBooksShortageAsRealLoss()
        {
            var (bank, tellers) = OpenBankWithWindow();
            Assert.IsNull(tellers.TakeDeposit("w1", "a1", "Samuel", DepositKind.Demand,
                Cash(VaultSpecieKind.SilverCoin, "silver coin", 20000, "Samuel"), 1, diagnostics: diag));
            // Unrecorded loss: specie leaves with no ledger entry.
            Assert.IsNotNull(bank.DrawVaultLots(5000, diag));
            Assert.AreEqual(-5000, bank.CheckVaultReconciliation(new List<string>()));

            int equityBefore = bank.OwnerEquityCents();
            Assert.IsNull(tellers.EndOfDayBalancing(1, diag));
            Assert.AreEqual(65000, bank.Ledger.CashOnHandCents, "The shortage is booked — cash on hand drops.");
            Assert.AreEqual(equityBefore - 5000, bank.OwnerEquityCents(), "Liabilities unchanged: equity absorbs the loss.");
            Assert.AreEqual(20000, bank.Ledger.DepositsOwedCents(), "Depositors are still owed every cent.");
            Assert.AreEqual(0, bank.CheckVaultReconciliation(new List<string>()), "Books balance after the loss is booked.");
            Assert.AreEqual(0, tellers.Windows["w1"].ServedToday, "Tallies reset for tomorrow.");
        }

        [Test]
        public void EndOfDayReportsOverageWithoutBookingIt()
        {
            var (bank, tellers) = OpenBankWithWindow();
            // Found cash with no ledger entry — source unknown.
            Assert.IsNull(bank.ReceiveVaultLot(
                Cash(VaultSpecieKind.SilverCoin, "silver coin", 500, "found in drawer"), 1, diag));
            Assert.IsNull(tellers.EndOfDayBalancing(1, diag));
            Assert.AreEqual(50000, bank.Ledger.CashOnHandCents,
                "Overages are NOT booked — unexplained cash is not income.");
            Assert.AreEqual(500, bank.CheckVaultReconciliation(new List<string>()),
                "The overage keeps flagging until its source is identified.");
        }

        [Test]
        public void TellerOperationsSaveRoundTrip()
        {
            var (bank, tellers) = OpenBankWithWindow();
            Assert.IsNull(tellers.TakeDeposit("w1", "a1", "Samuel", DepositKind.Demand,
                Cash(VaultSpecieKind.SilverCoin, "silver coin", 10000, "Samuel"), 1, diagnostics: diag));

            TellerOperations.TellerOperationsSaveDto dto = tellers.ToSaveDto();
            var restoredOps = new TellerOperations(bank);
            restoredOps.LoadFromSaveDto(dto);

            Assert.IsTrue(restoredOps.Windows.ContainsKey("w1"));
            TellerWindow window = restoredOps.Windows["w1"];
            Assert.IsTrue(window.IsOpen);
            Assert.IsTrue(window.StationReady);
            Assert.AreEqual("Teller Tom", window.TellerName);
            Assert.AreEqual(1, window.ServedToday);
            Assert.AreEqual(10000, window.ReceivedTodayCents);
        }
    }
}
