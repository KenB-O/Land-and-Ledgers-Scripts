// Pass XII Worker A (E-DEFECT-02 reproduction + regression).
// Worker E reported: restoring TWICE from the SAME in-memory Ws4Snapshot
// object is not idempotent once the first restored run has executed — the
// second run finished with a different bank balance and without the mine
// payroll suspension. Pass XI's SaveRestore_DoubleRestore_IsIdempotent only
// restores twice and hashes BEFORE either run executes, so it cannot see
// snapshot corruption caused by the first run mutating shared DTO objects.
// This test runs the first restore to completion before restoring again,
// which is the shape that exposes load-path aliasing.
using LandLedgers.Economy;
using LandLedgers.Economy.Financing;
using LandLedgers.Economy.Postal;
using LandLedgers.Editor.Integration;
using LandLedgers.Persistence;
using LandLedgers.Population;
using NUnit.Framework;

namespace LandLedgers.Editor.Bootstrap
{
    [TestFixture]
    public sealed class Pass12DoubleRestoreTests
    {
        private static Ws4SettlementScenario RunFull()
        {
            var sim = new Ws4SettlementScenario();
            sim.Build();
            sim.RunAll();
            return sim;
        }

        [Test]
        public void DoubleRestore_AfterFirstRunCompletes_ReproducesBaseline()
        {
            string baseline = RunFull().ComputeHash();
            var sim = new Ws4SettlementScenario();
            sim.Build();
            sim.RunUntil(8, 0);
            Ws4Snapshot snap = sim.CaptureSnapshot();

            Ws4SettlementScenario r1 = Ws4SettlementScenario.Restore(snap);
            r1.RunAll();
            Assert.AreEqual(baseline, r1.ComputeHash(), "first restored run must match the uninterrupted run.");

            // The defect: r1's run must not have mutated the snapshot object,
            // so restoring the SAME snapshot again must reproduce the same run.
            Ws4SettlementScenario r2 = Ws4SettlementScenario.Restore(snap);
            r2.RunAll();
            Assert.AreEqual(baseline, r2.ComputeHash(),
                "second restore of the same in-memory snapshot must reproduce the baseline (load must not alias caller-owned DTO state).");
            Assert.AreEqual(r1.MinePayrollSuspendedDay, r2.MinePayrollSuspendedDay,
                "second run must observe the same mine payroll suspension as the first run.");
        }

        [Test]
        public void LedgerImport_DoesNotAliasImportedState()
        {
            // Unit-level pin for the ledger leg of the same defect: importing
            // a state object, then recording entries on the live ledger, must
            // leave the imported state object untouched, so a second import of
            // the same object reproduces the same ledger.
            var states = new System.Collections.Generic.List<HouseholdLedgerState>
            {
                new HouseholdLedgerState
                {
                    HouseholdId = 41,
                    NextEntrySequence = 7,
                    Entries = new System.Collections.Generic.List<HouseholdLedgerEntry>
                    {
                        new HouseholdLedgerEntry { EntrySequence = 6, DayIndex = 3, AmountCents = 1000, Source = HouseholdIncomeSource.ServiceIncome, Reason = "seed" },
                    },
                },
            };
            var first = new HouseholdLedgerRegistry();
            first.ImportState(states);
            HouseholdLedger live = first.Get(41);
            Assert.IsNotNull(live, "imported ledger must exist.");
            Assert.AreEqual(1000, live.GetBalanceCents(), "imported balance must match the state.");
            string problem = live.RecordInflow(4, 500, HouseholdIncomeSource.ServiceIncome, "x", "post-import inflow", "cpty");
            Assert.IsNull(problem, "post-import inflow must be accepted.");
            Assert.AreEqual(1500, live.GetBalanceCents(), "live balance must include the post-import inflow.");
            Assert.AreEqual(1, states[0].Entries.Count,
                "import must copy: recording on the live ledger must not append to the caller's state object.");
            Assert.AreEqual(7, states[0].NextEntrySequence,
                "import must copy: recording on the live ledger must not advance the caller's sequence cursor.");

            var second = new HouseholdLedgerRegistry();
            second.ImportState(states);
            Assert.AreEqual(1000, second.Get(41).GetBalanceCents(),
                "second import of the same state object must reproduce the original balance, not the mutated live balance.");
        }

        [Test]
        public void LedgerImport_SecondImportReplaces()
        {
            // E-NOTE-01 pin: load is clear-then-load per household. Importing
            // a second state for the same household must replace the first,
            // never silently keep stale ledgers behind a skip.
            var registry = new HouseholdLedgerRegistry();
            registry.ImportState(new System.Collections.Generic.List<HouseholdLedgerState>
            {
                new HouseholdLedgerState
                {
                    HouseholdId = 42,
                    NextEntrySequence = 1,
                    Entries = new System.Collections.Generic.List<HouseholdLedgerEntry>
                    {
                        new HouseholdLedgerEntry { EntrySequence = 0, DayIndex = 1, AmountCents = 1000, Source = HouseholdIncomeSource.ServiceIncome, Reason = "first" },
                    },
                },
            });
            Assert.AreEqual(1000, registry.Get(42).GetBalanceCents());
            registry.ImportState(new System.Collections.Generic.List<HouseholdLedgerState>
            {
                new HouseholdLedgerState
                {
                    HouseholdId = 42,
                    NextEntrySequence = 1,
                    Entries = new System.Collections.Generic.List<HouseholdLedgerEntry>
                    {
                        new HouseholdLedgerEntry { EntrySequence = 0, DayIndex = 1, AmountCents = 2500, Source = HouseholdIncomeSource.ServiceIncome, Reason = "second" },
                    },
                },
            });
            Assert.AreEqual(2500, registry.Get(42).GetBalanceCents(),
                "second import for the same household must replace the first (clear-then-load), not keep stale state.");
        }

        [Test]
        public void EmploymentLoad_DoesNotAliasRelationships()
        {
            var dto = new EmploymentRegistrySaveDto();
            dto.relationships.Add(new EmploymentRelationship
            {
                Id = "emp-t",
                EmployeePersonId = 5,
                EmployerBusinessId = "mine-1",
                LifecycleState = EmploymentLifecycleState.Active,
            });
            var registry = new EmploymentRelationshipRegistry();
            registry.LoadFromSaveDto(dto);
            Assert.IsTrue(registry.TryGetById("emp-t", out EmploymentRelationship live), "loaded relationship must exist.");
            live.LifecycleState = EmploymentLifecycleState.Suspended;
            Assert.AreEqual(EmploymentLifecycleState.Active, dto.relationships[0].LifecycleState,
                "suspending the live relationship must not mutate the caller's DTO.");
            var second = new EmploymentRelationshipRegistry();
            second.LoadFromSaveDto(dto);
            Assert.IsTrue(second.TryGetById("emp-t", out EmploymentRelationship reloaded), "second load must exist.");
            Assert.AreEqual(EmploymentLifecycleState.Active, reloaded.LifecycleState,
                "second load of the same DTO must reproduce the original lifecycle, not the mutated one.");
        }

        [Test]
        public void ObligationLoad_DoesNotAliasObligations()
        {
            var dto = new FinancialObligationSaveDto();
            dto.Obligations.Add(new FinancialObligation
            {
                ObligationId = "K-t",
                Debtor = "mine-1",
                Creditor = "person:5",
                OriginalPrincipalCents = 1000,
                OutstandingPrincipalCents = 1000,
                Status = FinancialObligationStatus.Active,
            });
            var authority = new FinancialObligationAuthority();
            authority.LoadFromSaveDto(dto);
            FinancialObligation live = authority.Find("K-t");
            Assert.IsNotNull(live, "loaded obligation must exist.");
            live.OutstandingPrincipalCents = 0;
            live.Status = FinancialObligationStatus.Satisfied;
            live.Payments.Add(new FinancialPaymentRecord { PaymentId = "p-t", AmountCents = 1000 });
            Assert.AreEqual(1000, dto.Obligations[0].OutstandingPrincipalCents,
                "settling the live obligation must not mutate the caller's DTO.");
            Assert.AreEqual(0, dto.Obligations[0].Payments.Count,
                "payments on the live obligation must not append to the caller's DTO.");
            var second = new FinancialObligationAuthority();
            second.LoadFromSaveDto(dto);
            FinancialObligation reloaded = second.Find("K-t");
            Assert.AreEqual(1000, reloaded.OutstandingPrincipalCents, "second load must reproduce the original outstanding.");
            Assert.AreEqual(FinancialObligationStatus.Active, reloaded.Status, "second load must reproduce the original status.");
            Assert.AreEqual(0, reloaded.Payments.Count, "second load must reproduce the original payment list.");
        }

        [Test]
        public void MoneyOrderLoad_DoesNotAliasAdvices()
        {
            var dto = new MoneyOrderServiceSaveDto();
            dto.offices.Add(new PostalMoneyOrderOffice { OfficeId = "po-t", MoneyOrderCashCents = 5000 });
            dto.advices.Add(new PostalMoneyOrderAdvice
            {
                SerialNumber = 1,
                IssuingOfficeId = "po-t",
                PayableOfficeId = "po-t",
                AmountCents = 2500,
                SentDayIndex = 8,
                DueArrivalDayIndex = 10,
                ReceivedDayIndex = -1,
            });
            var service = new MoneyOrderService();
            service.LoadFromSaveDto(dto);
            PostalMoneyOrderAdvice live = service.FindAdvice("po-t", 1);
            Assert.IsNotNull(live, "loaded advice must exist.");
            live.ReceivedDayIndex = 9;
            Assert.AreEqual(-1, dto.advices[0].ReceivedDayIndex,
                "marking the live advice received must not mutate the caller's DTO.");
            var second = new MoneyOrderService();
            second.LoadFromSaveDto(dto);
            Assert.AreEqual(-1, second.FindAdvice("po-t", 1).ReceivedDayIndex,
                "second load of the same DTO must reproduce the unreceived advice.");
        }

        [Test]
        public void DepositLoad_DoesNotAliasAccounts()
        {
            var dto = new BankDepositLedger.BankDepositLedgerSaveDto
            {
                BankName = "Test Bank",
                CashOnHandCents = 5000,
            };
            dto.Accounts.Add(new DepositAccount
            {
                AccountId = "dep-t",
                DepositorName = "H3",
                Kind = DepositKind.Demand,
                BalanceCents = 1000,
            });
            var ledger = new BankDepositLedger();
            ledger.LoadFromSaveDto(dto);
            Assert.IsNull(ledger.Withdraw("dep-t", 1000, 5, false), "live withdrawal must succeed.");
            Assert.AreEqual(1000, dto.Accounts[0].BalanceCents,
                "withdrawing from the live account must not mutate the caller's DTO.");
            var second = new BankDepositLedger();
            second.LoadFromSaveDto(dto);
            Assert.IsNull(second.Withdraw("dep-t", 1000, 5, false),
                "second load of the same DTO must still hold the original balance, so its withdrawal succeeds.");
        }
    }
}
