using System.Collections.Generic;
using LandLedgers.Economy.Bank;
using LandLedgers.Economy.Financing;
using NUnit.Framework;

namespace LandLedgers.EditorTests.Economy
{
    /// <summary>
    /// D4B: depositor-confidence records and signals (Canon §18.8–§18.11).
    /// Every expectation below is hand-traced from the real NX-3B ledger and
    /// W7 vault behavior — a run is an acceleration of a REAL liability, so
    /// the pattern records come only from executed withdrawals.
    /// </summary>
    [TestFixture]
    public sealed class DepositorConfidenceTests
    {
        private List<string> diag;
        private BankRuntime bank;
        private DepositorConfidenceLedger ledger;

        private BankRuntime MakeBank(string id, string name, int capitalCents)
        {
            var b = new BankRuntime(id, name, "Kennedy Baldwin-ooms");
            Assert.IsNull(b.EstablishVault("iron safe", false, true, diag));
            Assert.IsNull(b.RecordOpeningCapital(capitalCents, "owner savings",
                new List<SpecieLot>
                {
                    new SpecieLot { Kind = VaultSpecieKind.GoldCoin, KindName = "gold coin", AmountCents = capitalCents, SourceName = "owner savings" },
                }, 0, diag));
            return b;
        }

        private void OpenDemandAccount(BankRuntime b, string accountId, string name, int amount, int day)
        {
            Assert.IsNotNull(b.Ledger.OpenAccount(accountId, name, DepositKind.Demand, amount, day, diag: diag));
            Assert.IsNull(b.ReceiveVaultLot(new SpecieLot
            {
                Kind = VaultSpecieKind.GoldCoin, KindName = "gold coin",
                AmountCents = amount, SourceName = "depositor " + name,
            }, day, diag));
        }

        private void DisburseLoanPaired(BankRuntime b, int amount, string reference)
        {
            Assert.IsNull(b.Ledger.DisburseLoan(amount, reference, diag));
            Assert.IsNotNull(b.DrawVaultLots(amount, diag));
        }

        /// <summary>
        /// Main fixture: capital 100000; demand accts hh-1 20000 / biz-1
        /// 50000; term acct term-1 30000 (90d, 500bps); loan out 120000.
        /// Cash 80000, vault 80000, owed 100000, all reconciled.
        /// </summary>
        private void BuildMainBank()
        {
            diag = new List<string>();
            bank = MakeBank("bank-d4b", "Confidence Test Bank", 100000);
            OpenDemandAccount(bank, "acct-household-1", "Alice Farmer", 20000, 0);
            OpenDemandAccount(bank, "acct-business-1", "Mill and Grain Co", 50000, 0);
            Assert.IsNotNull(bank.Ledger.OpenAccount("acct-term-1", "Widow Harper",
                DepositKind.Term, 30000, 0, 90, 500, diag: diag));
            Assert.IsNull(bank.ReceiveVaultLot(new SpecieLot
            {
                Kind = VaultSpecieKind.GoldCoin, KindName = "gold coin",
                AmountCents = 30000, SourceName = "depositor Widow Harper",
            }, 0, diag));
            DisburseLoanPaired(bank, 120000, "mill expansion");
            Assert.AreEqual(0, bank.CheckVaultReconciliation(diag));

            ledger = new DepositorConfidenceLedger("bank-d4b");
            Assert.IsNull(ledger.RegisterDepositorClass("acct-household-1", DepositorClass.Household, diag));
            Assert.IsNull(ledger.RegisterDepositorClass("acct-business-1", DepositorClass.Business, diag));
            Assert.IsNull(ledger.RegisterDepositorClass("acct-term-1", DepositorClass.Household, diag));
        }

        private string RecordHonored(string accountId, int amount, int day)
        {
            string refused = ledger.RecordWithdrawalAttempt(bank, accountId, amount, day,
                false, out List<SpecieLot> lots, diag);
            Assert.IsNull(refused, "expected honored withdrawal: " + refused);
            Assert.IsNotNull(lots);
            int lotTotal = 0;
            foreach (SpecieLot lot in lots) lotTotal += lot.AmountCents;
            Assert.AreEqual(amount, lotTotal);
            return refused;
        }

        // ---------- withdrawal-event recording ----------

        [Test]
        public void WithdrawalEventsRecordRealWithdrawalsWithClass()
        {
            BuildMainBank();
            RecordHonored("acct-household-1", 1000, 1);
            RecordHonored("acct-business-1", 2000, 2);

            Assert.AreEqual(2, ledger.WithdrawalEvents.Count);
            DepositorConfidenceWithdrawalEvent first = ledger.WithdrawalEvents[0];
            Assert.AreEqual("DCW-0001", first.EventId);
            Assert.AreEqual("bank-d4b", first.BankInstanceId);
            Assert.AreEqual(1, first.DayIndex);
            Assert.AreEqual("acct-household-1", first.AccountId);
            Assert.AreEqual("Alice Farmer", first.DepositorName);
            Assert.AreEqual(DepositorClass.Household, first.Class);
            Assert.AreEqual(1000, first.AmountCents);
            Assert.IsTrue(first.WasHonored);
            Assert.IsFalse(first.LiquidityRefusal);
            Assert.IsTrue(first.WasDemandAccount);
            Assert.AreEqual(DepositorClass.Business, ledger.WithdrawalEvents[1].Class);

            // Ledger and vault moved together: 80000 - 3000 = 77000.
            Assert.AreEqual(77000, bank.Ledger.CashOnHandCents);
            Assert.AreEqual(77000, bank.VaultSpecieTotalCents());
            Assert.AreEqual(0, bank.CheckVaultReconciliation(diag));
        }

        [Test]
        public void RefusalsAreRecordedWithReasons()
        {
            BuildMainBank();
            // Balance refusal: 60000 > 50000 account balance, cash is fine.
            string refused = ledger.RecordWithdrawalAttempt(bank, "acct-business-1", 60000, 15,
                false, out List<SpecieLot> lots, diag);
            Assert.IsNotNull(refused);
            Assert.IsNull(lots);
            // Term lock: early withdrawal refused by the ledger's own guards.
            refused = ledger.RecordWithdrawalAttempt(bank, "acct-term-1", 10000, 16,
                false, out lots, diag);
            Assert.IsNotNull(refused);
            Assert.IsNull(lots);
            // Liquidity refusal: drain cash to 5000, then ask for 10000 (balance covers it).
            DisburseLoanPaired(bank, 75000, "second loan");
            Assert.AreEqual(5000, bank.Ledger.CashOnHandCents);
            refused = ledger.RecordWithdrawalAttempt(bank, "acct-business-1", 10000, 17,
                false, out lots, diag);
            Assert.IsNotNull(refused);
            Assert.IsNull(lots);

            Assert.AreEqual(3, ledger.WithdrawalEvents.Count);
            Assert.IsFalse(ledger.WithdrawalEvents[0].WasHonored);
            Assert.IsFalse(ledger.WithdrawalEvents[0].LiquidityRefusal);
            Assert.IsFalse(ledger.WithdrawalEvents[1].LiquidityRefusal);
            Assert.IsTrue(ledger.WithdrawalEvents[2].LiquidityRefusal);
            // The liquidity refusal touched neither the account nor the cash.
            int businessBalance = -1;
            foreach (DepositAccount account in bank.Ledger.Accounts)
                if (account.AccountId == "acct-business-1") businessBalance = account.BalanceCents;
            Assert.AreEqual(50000, businessBalance);
            Assert.AreEqual(5000, bank.Ledger.CashOnHandCents);

            Assert.AreEqual(3, ledger.RefusedStreakCount());
            Assert.AreEqual(0, ledger.DaysSinceLastRefusal(17));
            Assert.AreEqual(0.0, ledger.HonoredWithdrawalRatio(17, 30), 1e-9);
        }

        // ---------- liquidity coverage from the real books ----------

        [Test]
        public void LiquidityCoverageUsesRealBooksAndMaturityTiming()
        {
            BuildMainBank();
            // Day 10: demandable = 20000 + 50000 (term matures day 90, excluded).
            Assert.AreEqual(70000, ledger.DemandableDepositsCents(bank, 10));
            // No interbank attached: coverage is cash on hand alone.
            Assert.AreEqual(80000, ledger.LiquidCoverageCents(bank));
            Assert.AreEqual(80000.0 / 70000.0, ledger.LiquidityCoverageRatio(bank, 10), 1e-9);

            // Day 95: the term deposit has matured — it is demandable now.
            Assert.AreEqual(100000, ledger.DemandableDepositsCents(bank, 95));
            Assert.AreEqual(0.8, ledger.LiquidityCoverageRatio(bank, 95), 1e-9);
        }

        // ---------- withdrawal acceleration ----------

        [Test]
        public void WithdrawalAccelerationMeasuresRealVolumeChange()
        {
            diag = new List<string>();
            var accelBank = MakeBank("bank-d4b-2", "Accel Bank", 200000);
            OpenDemandAccount(accelBank, "acct-a", "Steady Saver", 200000, 0);
            var accelLedger = new DepositorConfidenceLedger("bank-d4b-2");
            accelLedger.Thresholds.RecentWindowDays = 2;
            accelLedger.Thresholds.BaselineWindowDays = 10;

            for (int day = 1; day <= 10; day++)
            {
                string refused = accelLedger.RecordWithdrawalAttempt(accelBank, "acct-a", 1000, day,
                    false, out _, diag);
                Assert.IsNull(refused);
            }
            for (int day = 11; day <= 12; day++)
            {
                string refused = accelLedger.RecordWithdrawalAttempt(accelBank, "acct-a", 20000, day,
                    false, out _, diag);
                Assert.IsNull(refused);
            }

            // Recent (10,12]: 40000/2 = 20000/day. Baseline (0,10]: 10000/10 = 1000/day. Factor = 20.
            Assert.AreEqual(20.0, accelLedger.WithdrawalAccelerationFactor(12), 1e-9);
            // Day 1: baseline window (-11,-1] is empty — unknown, not zero.
            Assert.AreEqual(-1.0, accelLedger.WithdrawalAccelerationFactor(1), 1e-9);
        }

        // ---------- concentration and tiers ----------

        [Test]
        public void LargestDepositorShareAndStrainedTier()
        {
            BuildMainBank();
            RecordHonored("acct-household-1", 1000, 1);
            RecordHonored("acct-business-1", 2000, 2);
            RecordHonored("acct-household-1", 1500, 3);
            RecordHonored("acct-business-1", 40000, 11);
            RecordHonored("acct-household-1", 15000, 12);
            RecordHonored("acct-business-1", 8000, 13);
            RecordHonored("acct-household-1", 2500, 14);
            OpenDemandAccount(bank, "acct-household-2", "Big Depositor", 50000, 17);

            // Balances: hh-1 0, biz-1 0, term-1 30000, hh-2 50000. Owed 80000.
            Assert.AreEqual(80000, bank.Ledger.DepositsOwedCents());
            Assert.AreEqual(0.625, ledger.LargestDepositorShareRatio(bank), 1e-9);

            // Cash 80000-70000+50000 = 60000; demandable day 17 = 50000 (term unmatured).
            DepositorConfidenceSignal signal = ledger.ComputeSignal(bank, 17, diag);
            Assert.AreEqual(60000, signal.LiquidCoverageCents);
            Assert.AreEqual(50000, signal.DemandableDepositsCents);
            Assert.AreEqual(1.2, signal.LiquidityCoverageRatio, 1e-9);
            // Concentration 0.625 >= 0.50 critical share -> Strained (nothing worse present).
            Assert.AreEqual(DepositorConfidenceTier.Strained, signal.Tier);
        }

        [Test]
        public void RefusedStreakDrivesCriticalTier()
        {
            BuildMainBank();
            ledger.RecordWithdrawalAttempt(bank, "acct-business-1", 60000, 15, false, out _, diag);
            ledger.RecordWithdrawalAttempt(bank, "acct-term-1", 10000, 16, false, out _, diag);
            DisburseLoanPaired(bank, 75000, "second loan");
            ledger.RecordWithdrawalAttempt(bank, "acct-business-1", 10000, 17, false, out _, diag);

            DepositorConfidenceSignal signal = ledger.ComputeSignal(bank, 17, diag);
            Assert.AreEqual(3, signal.RefusedStreak);
            Assert.AreEqual(DepositorConfidenceTier.Critical, signal.Tier);
        }

        [Test]
        public void WatchAndStableTiers()
        {
            diag = new List<string>();
            var watchBank = MakeBank("bank-d4b-w", "Watch Bank", 100000);
            OpenDemandAccount(watchBank, "acct-a", "A", 40000, 0);
            OpenDemandAccount(watchBank, "acct-b", "B", 30000, 0);
            OpenDemandAccount(watchBank, "acct-c", "C", 30000, 0);
            var watchLedger = new DepositorConfidenceLedger("bank-d4b-w");
            DepositorConfidenceSignal watch = watchLedger.ComputeSignal(watchBank, 5, diag);
            // Largest share 0.4 >= warn 0.25, nothing worse -> Watch.
            Assert.AreEqual(DepositorConfidenceTier.Watch, watch.Tier);

            var stableBank = MakeBank("bank-d4b-s", "Stable Bank", 100000);
            for (int i = 1; i <= 5; i++)
                OpenDemandAccount(stableBank, "acct-" + i, "Saver " + i, 20000, 0);
            var stableLedger = new DepositorConfidenceLedger("bank-d4b-s");
            DepositorConfidenceSignal stable = stableLedger.ComputeSignal(stableBank, 5, diag);
            // Largest share 0.2 < warn 0.25, coverage 2.0, no events -> Stable.
            Assert.AreEqual(DepositorConfidenceTier.Stable, stable.Tier);
            Assert.AreEqual(2.0, stable.LiquidityCoverageRatio, 1e-9);
        }

        // ---------- panic-transmission channels ----------

        [Test]
        public void PanicChannelsAreRecordedPerChannelAndRangeChecked()
        {
            BuildMainBank();
            Assert.IsNull(ledger.RecordPanicChannel(16, PanicTransmissionChannel.WithdrawalDemand,
                0.6, "interior banks drawing", diag));
            Assert.IsNull(ledger.RecordPanicChannel(17, PanicTransmissionChannel.CorrespondentAvailability,
                0.8, string.Empty, diag));
            // Out-of-range pressure is refused, not clamped.
            Assert.IsNotNull(ledger.RecordPanicChannel(17, PanicTransmissionChannel.AssetSaleability,
                1.5, string.Empty, diag));
            Assert.IsNotNull(ledger.RecordPanicChannel(17, PanicTransmissionChannel.Unspecified,
                0.5, string.Empty, diag));
            Assert.AreEqual(2, ledger.PanicReadings.Count);

            Assert.AreEqual(0.6, ledger.PanicPressure(17, PanicTransmissionChannel.WithdrawalDemand), 1e-9);
            Assert.AreEqual(0.0, ledger.PanicPressure(16, PanicTransmissionChannel.CorrespondentAvailability), 1e-9);
            Assert.AreEqual(0.8, ledger.PanicPressure(17, PanicTransmissionChannel.CorrespondentAvailability), 1e-9);
            Assert.AreEqual(0.0, ledger.PanicPressure(99, PanicTransmissionChannel.NewLoanAppetite), 1e-9);
        }

        // ---------- outside support ----------

        [Test]
        public void OutsideSupportRecordsOnlyRealInflows()
        {
            BuildMainBank();
            Assert.IsNull(ledger.RecordOutsideSupport(5, 25000, "correspondent draw, Omaha National", diag));
            Assert.IsNotNull(ledger.RecordOutsideSupport(6, 0, "empty promise", diag));
            Assert.IsNotNull(ledger.RecordOutsideSupport(6, 1000, string.Empty, diag));
            Assert.AreEqual(1, ledger.SupportEvents.Count);

            Assert.AreEqual(25000, ledger.OutsideSupportCents(17, 30));
            Assert.AreEqual(0, ledger.OutsideSupportCents(17, 5)); // window (12,17] excludes day 5
        }

        // ---------- interbank integration (D4A) ----------

        [Test]
        public void InterbankDepositorsResolveToCounterpartyBank()
        {
            diag = new List<string>();
            var bankA = MakeBank("bank-d4b-a", "First Bank", 100000);
            var bankB = MakeBank("bank-d4b-b", "Second Bank", 100000);
            var settlement = new InterbankSettlement();
            Assert.IsNull(settlement.RegisterBank(bankA, diag));
            Assert.IsNull(settlement.RegisterBank(bankB, diag));
            Assert.IsNull(settlement.OpenInterbankDeposit("bank-d4b-a", "bank-d4b-b", "ib-1",
                DepositKind.Demand, 30000, 0, diag: diag));

            var ledgerB = new DepositorConfidenceLedger("bank-d4b-b");
            ledgerB.AttachInterbankSettlement(settlement);
            Assert.AreEqual(DepositorClass.CounterpartyBank, ledgerB.ResolveDepositorClass(bankB, "ib-1"));

            string refused = ledgerB.RecordWithdrawalAttempt(bankB, "ib-1", 5000, 1,
                false, out List<SpecieLot> lots, diag);
            Assert.IsNull(refused);
            Assert.IsNotNull(lots);
            Assert.AreEqual(DepositorClass.CounterpartyBank, ledgerB.WithdrawalEvents[0].Class);

            // Drawdown for bank A: cash 70000 + drawable demand interbank asset 25000 = 95000.
            var ledgerA = new DepositorConfidenceLedger("bank-d4b-a");
            ledgerA.AttachInterbankSettlement(settlement);
            Assert.AreEqual(95000, ledgerA.LiquidCoverageCents(bankA));
        }

        // ---------- save / load ----------

        [Test]
        public void SaveLoadRoundTripPreservesRecordsAndCounters()
        {
            BuildMainBank();
            RecordHonored("acct-household-1", 1000, 1);
            RecordHonored("acct-business-1", 2000, 2);
            Assert.IsNull(ledger.RecordOutsideSupport(5, 25000, "correspondent draw", diag));
            Assert.IsNull(ledger.RecordPanicChannel(6, PanicTransmissionChannel.WithdrawalDemand, 0.6, string.Empty, diag));

            DepositorConfidenceLedger.DepositorConfidenceLedgerSaveDto dto = ledger.ToSaveDto();
            var restored = new DepositorConfidenceLedger();
            restored.LoadFromSaveDto(dto);

            Assert.AreEqual("bank-d4b", restored.BankInstanceId);
            Assert.AreEqual(2, restored.WithdrawalEvents.Count);
            Assert.AreEqual("DCW-0001", restored.WithdrawalEvents[0].EventId);
            Assert.AreEqual(2000, restored.WithdrawalEvents[1].AmountCents);
            Assert.AreEqual(1, restored.SupportEvents.Count);
            Assert.AreEqual(25000, restored.SupportEvents[0].AmountCents);
            Assert.AreEqual(1, restored.PanicReadings.Count);
            Assert.AreEqual(0.6, restored.PanicReadings[0].Pressure01, 1e-9);
            Assert.AreEqual(DepositorClass.Household, restored.ResolveDepositorClass(bank, "acct-household-1"));
            Assert.AreEqual(DepositorClass.Business, restored.ResolveDepositorClass(bank, "acct-business-1"));

            // Counters continue, not restart.
            string refused = restored.RecordWithdrawalAttempt(bank, "acct-household-1", 500, 3,
                false, out _, diag);
            Assert.IsNull(refused);
            Assert.AreEqual("DCW-0003", restored.WithdrawalEvents[2].EventId);
        }

        // ---------- read-only W7 view ----------

        [Test]
        public void ConfidenceViewIsReadOnly()
        {
            BuildMainBank();
            RecordHonored("acct-household-1", 1000, 1);

            int cashBefore = bank.Ledger.CashOnHandCents;
            int owedBefore = bank.Ledger.DepositsOwedCents();
            int vaultBefore = bank.VaultSpecieTotalCents();

            DepositorConfidenceSignal signal = BankDepositorConfidenceView.Build(bank, ledger, 10, diag);

            Assert.AreEqual(cashBefore, bank.Ledger.CashOnHandCents);
            Assert.AreEqual(owedBefore, bank.Ledger.DepositsOwedCents());
            Assert.AreEqual(vaultBefore, bank.VaultSpecieTotalCents());
            Assert.AreEqual("bank-d4b", signal.BankInstanceId);
            Assert.AreEqual(10, signal.DayIndex);
            Assert.AreEqual(bank.OwnerEquityCents(), signal.EquityCents);
            Assert.AreEqual(vaultBefore, signal.VaultCashCents);
        }

        [Test]
        public void ConfidenceViewWithNoLedgerReturnsEmptySignal()
        {
            BuildMainBank();
            DepositorConfidenceSignal signal = BankDepositorConfidenceView.Build(bank, null, 10, diag);
            Assert.AreEqual("bank-d4b", signal.BankInstanceId);
            Assert.AreEqual(DepositorConfidenceTier.Unspecified, signal.Tier);
            Assert.AreEqual(bank.Ledger.CashOnHandCents, 80000); // untouched
        }
    }
}
