// DEF-071 (Decision 14): cancellation — construction cancellation settles
// hired-worker wages (paid from real cash, or recorded as a debt when the
// household cannot cover them) and ends the employments; a cancelled builder
// contract pays its released obligations, releases its retainage, and voids
// what was never earned. Nothing is erased merely because an activity was
// cancelled: wages owed become a claim, unearned obligations become voided
// records, and the cancellation itself is recorded with its cause.
// Copyright (c) Land & Ledgers contributors. Licensed under the MIT License.

using System.Collections.Generic;
using LandLedgers.Economy;
using LandLedgers.Economy.Financing;
using LandLedgers.Population;
using LandLedgers.Primitives;
using NUnit.Framework;

namespace LandLedgers.Editor.Economy
{
    [TestFixture]
    public sealed class ConstructionCancellationDef071Tests
    {
        // ---------- D3 fixture: hand-built record / project / funding ----------

        private CreditCashBridge cash;
        private List<string> diag;

        [SetUp]
        public void SetUp()
        {
            cash = new CreditCashBridge();
            diag = new List<string>();
            port = new FakeConstructionBillingCashPort();
            port.Fund(OwnerKey, 100000);
            port.Fund(BuilderKey, 0);
            billing = new ConstructionProgressBillingService(port);
        }

        private HouseholdLedger RegisterHousehold(int householdId, int cashCents)
        {
            var ledger = new HouseholdLedger(householdId);
            Assert.IsNull(ledger.RecordInflow(0, cashCents, HouseholdIncomeSource.OwnerContribution,
                "test-seed", "test seed capital", "test"));
            Assert.IsNull(cash.Register("household:" + householdId,
                new HouseholdCashStore(householdId, ledger)));
            return ledger;
        }

        private static EmploymentRelationship Hire(int personId, int weeklyWageCents)
        {
            return new EmploymentRelationship
            {
                Id = "emp-1",
                EmployeePersonId = personId,
                EmployerBusinessId = "household:7 construction",
                Compensation = CompensationTerms.FromWeeklyWage(weeklyWageCents, "test hire"),
                StartDayIndex = 11,
            };
        }

        private static (NpcConstructionRecord Record, ConstructionProject Project, NpcBuildPackageFunding Funding)
            HiredPackage(NpcBuildLaborKind laborKind, int minutesCommitted)
        {
            var funding = new NpcBuildPackageFunding
            {
                PackageId = "PKG-1",
                PhaseName = "Foundation",
                LaborKind = laborKind,
                WorkerMinutesCommitted = minutesCommitted,
            };
            if (laborKind == NpcBuildLaborKind.HiredWorker)
                funding.EmploymentIds.Add("emp-1");
            var record = new NpcConstructionRecord
            {
                RecordId = "REC-1",
                ProjectId = "PROJ-1",
                HouseholdId = 7,
            };
            record.PackageFundings.Add(funding);
            var project = new ConstructionProject
            {
                ProjectId = "PROJ-1",
                HouseholdId = 7,
            };
            project.WorkPackages.Add(new WorkPackage
            {
                PackageId = "PKG-1",
                ProjectId = "PROJ-1",
                PhaseName = "Foundation",
                Sequence = 0,
                Status = WorkPackageStatus.InProgress,
            });
            return (record, project, funding);
        }

        // ---------------- D3: hired wages on cancellation ----------------

        [Test]
        public void CancelPaysHiredWagesProRata_AndEndsEmployment()
        {
            HouseholdLedger payer = RegisterHousehold(7, 100000);
            HouseholdLedger worker = RegisterHousehold(9, 1000);
            var employments = new EmploymentRelationshipRegistry();
            Assert.IsNull(employments.Register(Hire(21, 4800)));
            var obligations = new FinancialObligationAuthority();
            var ids = new EntityIdRegistry();
            var commissioner = new NpcConstructionCommissioner();
            var (record, project, funding) = HiredPackage(NpcBuildLaborKind.HiredWorker, 240);

            Assert.IsNull(commissioner.SettleCancellationWages(employments, obligations, ids,
                record, project, cash, "household:7",
                new Dictionary<int, int> { { 21, 9 } }, 12, diag), string.Join("; ", diag));

            // 240 min of a 2400-min week at 4800c/week = 480c.
            Assert.AreEqual(480, funding.WagesPaidCents);
            Assert.AreEqual(1000 + 480, worker.GetBalanceCents());
            Assert.AreEqual(100000 - 480, payer.GetBalanceCents());
            Assert.AreEqual(480, record.TotalActualCostCents);
            Assert.IsTrue(employments.TryGetById("emp-1", out EmploymentRelationship rel));
            Assert.IsTrue(!rel.IsActive, "the engagement ends on the cancel day");
            Assert.AreEqual(EmploymentEndReason.EndedByAgreement, rel.EndReason);
            Assert.AreEqual(12, rel.EndDayIndex);
            Assert.AreEqual(0, obligations.Obligations.Count, "paid wages are not debts");
        }

        [Test]
        public void CancelOwesNoWages_ForOwnLabour()
        {
            HouseholdLedger payer = RegisterHousehold(7, 100000);
            RegisterHousehold(9, 1000);
            var employments = new EmploymentRelationshipRegistry();
            var obligations = new FinancialObligationAuthority();
            var ids = new EntityIdRegistry();
            var commissioner = new NpcConstructionCommissioner();
            var (record, project, funding) = HiredPackage(NpcBuildLaborKind.OwnLabor, 240);

            Assert.IsNull(commissioner.SettleCancellationWages(employments, obligations, ids,
                record, project, cash, "household:7",
                new Dictionary<int, int> { { 5, 7 } }, 12, diag), string.Join("; ", diag));

            Assert.AreEqual(0, funding.WagesPaidCents, "a household member working on their own house owes no wages");
            Assert.AreEqual(100000, payer.GetBalanceCents());
            Assert.AreEqual(0, obligations.Obligations.Count);
            Assert.AreEqual(0, record.TotalActualCostCents);
        }

        [Test]
        public void CancelWithShortCash_RecordsWageDebt_AndStillCompletes()
        {
            HouseholdLedger payer = RegisterHousehold(7, 100);
            HouseholdLedger worker = RegisterHousehold(9, 1000);
            var employments = new EmploymentRelationshipRegistry();
            Assert.IsNull(employments.Register(Hire(21, 4800)));
            var obligations = new FinancialObligationAuthority();
            var ids = new EntityIdRegistry();
            var commissioner = new NpcConstructionCommissioner();
            var (record, project, funding) = HiredPackage(NpcBuildLaborKind.HiredWorker, 240);

            // The household holds 100c; the 480c wage cannot be paid. The
            // cancel still completes — the unpaid wage becomes a claim.
            Assert.IsNull(commissioner.SettleCancellationWages(employments, obligations, ids,
                record, project, cash, "household:7",
                new Dictionary<int, int> { { 21, 9 } }, 12, diag), string.Join("; ", diag));

            Assert.AreEqual(0, funding.WagesPaidCents, "a refused payment moves nothing");
            Assert.AreEqual(100, payer.GetBalanceCents());
            Assert.AreEqual(1000, worker.GetBalanceCents());
            Assert.AreEqual(480, obligations.TotalOutstandingFor("household:7"));
            FinancialObligation debt = null;
            foreach (FinancialObligation o in obligations.Obligations) debt = o;
            Assert.IsNotNull(debt);
            Assert.AreEqual(FinancialObligationKind.Payable, debt.Kind);
            Assert.AreEqual("household:7", debt.Debtor);
            Assert.AreEqual("household:9", debt.Creditor);
            Assert.AreEqual(480, debt.OutstandingPrincipalCents);
            Assert.IsTrue(employments.TryGetById("emp-1", out EmploymentRelationship rel));
            Assert.IsTrue(!rel.IsActive, "the engagement ends even when its wage becomes a debt");
            Assert.AreEqual(EmploymentEndReason.EndedByAgreement, rel.EndReason);
        }

        // ---------- D4 fixture: billing (mirrors ConstructionProgressBillingTests) ----------

        private sealed class FakeConstructionBillingCashPort : IConstructionBillingCashPort
        {
            public readonly Dictionary<string, int> Balances =
                new Dictionary<string, int>(System.StringComparer.Ordinal);
            public readonly List<string> Moves = new List<string>();

            public void Fund(string partyKey, int cents)
            {
                Balances[partyKey] = cents;
            }

            public string MoveCash(string fromPartyKey, string toPartyKey, int amountCents,
                int dayIndex, string memo, List<string> diagnostics)
            {
                if (amountCents <= 0)
                    return "FakeConstructionBillingCashPort: a non-positive move is refused.";
                if (!Balances.TryGetValue(fromPartyKey, out int have))
                    return $"FakeConstructionBillingCashPort: unknown party '{fromPartyKey}' — refusing rather than minting.";
                if (have < amountCents)
                    return $"FakeConstructionBillingCashPort: '{fromPartyKey}' holds {have}c, cannot cover {amountCents}c — shortfall refused.";
                Balances[fromPartyKey] = have - amountCents;
                Balances.TryGetValue(toPartyKey, out int toHave);
                Balances[toPartyKey] = toHave + amountCents;
                Moves.Add($"{fromPartyKey}->{toPartyKey}:{amountCents}");
                return null;
            }

            public string TryGetCashBalance(string partyKey, out int balanceCents)
            {
                if (!Balances.TryGetValue(partyKey, out balanceCents))
                    return $"FakeConstructionBillingCashPort: unknown party '{partyKey}'.";
                return null;
            }
        }

        private FakeConstructionBillingCashPort port;
        private ConstructionProgressBillingService billing;

        private const string OwnerKey = "person:7";
        private const string BuilderKey = "business:build-1";

        private static ConstructionContract MakeContract(string id, int priceCents)
        {
            var contract = new ConstructionContract
            {
                ContractId = id,
                OwnerParty = ConstructionContractParty.Person(7, "Ada Owner"),
                BuilderParty = ConstructionContractParty.Business("build-1", BusinessType.Builder, "Alpha Builders"),
                WorksKind = ConstructionWorksKind.NewBuilding,
                WorksDescription = "24 by 36 ft general store building",
                PriceBasis = ConstructionContractPriceBasis.LumpSum,
                AgreedPriceCents = priceCents,
                Status = ConstructionContractStatus.Executed,
                FormedDayIndex = 4,
                PaymentTerms = new ConstructionPaymentTerms(),
            };
            contract.AcceptanceStages.Add(new ConstructionAcceptanceStage
            {
                StageId = "STAGE-0001",
                StageName = "Foundation",
                Status = ConstructionAcceptanceStageStatus.Pending,
            });
            contract.AcceptanceStages.Add(new ConstructionAcceptanceStage
            {
                StageId = "STAGE-0002",
                StageName = "Framing",
                Status = ConstructionAcceptanceStageStatus.Pending,
            });
            return contract;
        }

        private static void AddMilestone(ConstructionContract contract, string milestoneId,
            string name, int amountCents)
        {
            contract.PaymentTerms.Milestones.Add(new ConstructionPaymentMilestone
            {
                MilestoneId = milestoneId,
                Name = name,
                TriggerDescription = name + " accepted",
                AmountCents = amountCents,
            });
        }

        private static ConstructionPaymentObligation FindObligation(ConstructionContract contract, string kind)
        {
            foreach (ConstructionPaymentObligation o in contract.PaymentObligations)
            {
                if (o != null && o.Kind == kind) return o;
            }
            return null;
        }

        private void StartWorkAndAcceptBothStages(ConstructionContract contract)
        {
            Assert.IsNull(contract.StartWork(5, diag));
            Assert.IsNull(contract.AcceptStage("STAGE-0001", 10, "Ada Owner", diag));
            Assert.IsNull(contract.AcceptStage("STAGE-0002", 20, "Ada Owner", diag));
        }

        /// <summary>
        /// A contract with a Recorded deposit (the owner could not cover it),
        /// a settled 15000c milestone (13500c moved, 1500c retained), and a
        /// Released 20000c second milestone whose draw the owner could not
        /// cover — the obligation was created Released and stayed Released
        /// when the payment was refused. That is the only Released
        /// obligation a cancellable contract can carry: the final balance
        /// is released only by FinalAccept, and a finally accepted contract
        /// is terminal (IsTerminal) and cannot be cancelled at all.
        /// </summary>
        private ConstructionContract BuildContractReadyToSettle(
            out ConstructionPaymentObligation deposit, out ConstructionPaymentObligation releasedMilestone)
        {
            ConstructionContract contract = MakeContract("CTR-0001", 50000);
            contract.PaymentTerms.DepositCents = 10000;
            port.Fund(OwnerKey, 100); // the owner cannot cover the deposit
            Assert.IsNotNull(billing.RecordDeposit(contract, 6, diag));
            deposit = FindObligation(contract, "deposit");
            Assert.IsNotNull(deposit);
            Assert.AreEqual(ConstructionPaymentObligationStatus.Recorded, deposit.Status);
            port.Fund(OwnerKey, 100000);
            AddMilestone(contract, "MILE-0001", "foundation complete", 15000);
            StartWorkAndAcceptBothStages(contract);
            Assert.IsNull(billing.RecordMilestoneStageLink(contract, "MILE-0001", "STAGE-0001", 6, diag));
            Assert.IsNull(billing.RecordMilestone(contract, "MILE-0001", 21, diag));
            Assert.AreEqual(13500, port.Balances[BuilderKey]);
            Assert.AreEqual(1500, billing.RetainageBalanceCents("CTR-0001"));
            AddMilestone(contract, "MILE-0002", "framing complete", 20000);
            Assert.IsNull(billing.RecordMilestoneStageLink(contract, "MILE-0002", "STAGE-0002", 22, diag));
            port.Fund(OwnerKey, 100); // the owner cannot cover this draw either
            Assert.IsNotNull(billing.RecordMilestone(contract, "MILE-0002", 22, diag));
            releasedMilestone = FindObligation(contract, "milestone:MILE-0002");
            Assert.IsNotNull(releasedMilestone);
            Assert.AreEqual(ConstructionPaymentObligationStatus.Released, releasedMilestone.Status);
            Assert.AreEqual(1500, billing.RetainageBalanceCents("CTR-0001"),
                "a refused draw retains nothing");
            port.Fund(OwnerKey, 100000);
            return contract;
        }

        // ---------------- D4: contract cancel records, settlement settles ----------------

        [Test]
        public void ContractCancel_RequiresReason_RecordsIt_AndPersists()
        {
            ConstructionContract contract = MakeContract("CTR-0001", 50000);

            Assert.IsNotNull(contract.Cancel(6, diag), "cancelling without a cause must be refused");
            Assert.AreNotEqual(ConstructionContractStatus.Cancelled, contract.Status);
            Assert.IsNotNull(contract.Cancel(6, diag, "   ", "Ada Owner"), "a blank cause is no cause");
            Assert.IsNull(contract.Cancel(6, diag, "owner cancelled the build", "Ada Owner"));
            Assert.AreEqual(ConstructionContractStatus.Cancelled, contract.Status);
            Assert.AreEqual("owner cancelled the build", contract.CancellationReason);
            Assert.AreEqual("Ada Owner", contract.CancelledByDisplayName);
            Assert.AreEqual(6, contract.CancelledDayIndex);
            Assert.IsNotNull(contract.Cancel(7, diag, "again", "Ada Owner"), "a second cancel must be refused");

            // The cancellation record survives save/load.
            ConstructionContract.ConstructionContractSaveDto dto = contract.CaptureSaveDto();
            ConstructionContract restored = ConstructionContract.FromSaveDto(dto);
            Assert.AreEqual(ConstructionContractStatus.Cancelled, restored.Status);
            Assert.AreEqual("owner cancelled the build", restored.CancellationReason);
            Assert.AreEqual("Ada Owner", restored.CancelledByDisplayName);
            Assert.AreEqual(6, restored.CancelledDayIndex);
        }

        [Test]
        public void SettleCancellation_RefusesOnLiveContract()
        {
            ConstructionContract contract = MakeContract("CTR-0001", 50000);

            Assert.IsNotNull(billing.SettleCancellation(contract, 6, diag),
                "settling a live contract must be refused — cancel first, settle second");
            Assert.AreEqual(100000, port.Balances[OwnerKey]);
            Assert.AreEqual(0, port.Balances[BuilderKey]);
            Assert.AreEqual(0, billing.EventsForContract("CTR-0001").Count);
        }

        [Test]
        public void CancellationSettlement_PaysReleased_ReleasesRetainage_VoidsRecorded()
        {
            ConstructionPaymentObligation deposit;
            ConstructionPaymentObligation releasedMilestone;
            ConstructionContract contract = BuildContractReadyToSettle(out deposit, out releasedMilestone);

            Assert.IsNull(contract.Cancel(26, diag, "owner cancelled the build", "Ada Owner"));
            Assert.IsNull(billing.SettleCancellation(contract, 26, diag), string.Join("; ", diag));

            // The settlement pays the full Released milestone (20000c) plus
            // the retained 1500c: on cancellation there is no final
            // acceptance coming, so the holdback's window ends with the
            // cancel and the earned amount is owed in full.
            Assert.AreEqual(78500, port.Balances[OwnerKey]);   // 100000 - 13500 - 21500
            Assert.AreEqual(35000, port.Balances[BuilderKey]); // 13500 + 20000 + 1500
            Assert.AreEqual(0, billing.RetainageBalanceCents("CTR-0001"));
            Assert.AreEqual(ConstructionPaymentObligationStatus.SettledExternally, releasedMilestone.Status);
            Assert.AreEqual(ConstructionPaymentObligationStatus.Voided, deposit.Status,
                "the deposit was never earned — it dies with the contract, owed to nobody");
            Assert.AreEqual(ConstructionPaymentObligationStatus.SettledExternally,
                FindObligation(contract, "milestone:MILE-0001").Status,
                "an already-settled obligation is untouched");
            IReadOnlyList<ConstructionBillingEvent> events = billing.EventsForContract("CTR-0001");
            Assert.AreEqual(4, events.Count); // milestone + cancellation-settlement + void + retainage-release
            Assert.AreEqual(35000, billing.CumulativeBilledCents("CTR-0001"),
                "the void event records amount 0 — a void is not a billing");
        }

        [Test]
        public void SettleCancellation_RefusesWithoutCashPort_AndIsRetryable()
        {
            ConstructionPaymentObligation deposit;
            ConstructionPaymentObligation releasedMilestone;
            ConstructionContract contract = BuildContractReadyToSettle(out deposit, out releasedMilestone);
            Assert.IsNull(contract.Cancel(26, diag, "owner cancelled the build", "Ada Owner"));

            var unboundBilling = new ConstructionProgressBillingService(null);
            string refusal = unboundBilling.SettleCancellation(contract, 27, diag);
            Assert.IsNotNull(refusal, "settling with no cash port must refuse loudly");
            StringAssert.Contains("no cash port", refusal.ToLowerInvariant());
            Assert.AreEqual(ConstructionPaymentObligationStatus.Released, releasedMilestone.Status,
                "a refused settlement moves nothing and settles nothing");
            Assert.AreEqual(ConstructionPaymentObligationStatus.Recorded, deposit.Status);
            Assert.AreEqual(1500, billing.RetainageBalanceCents("CTR-0001"));

            // The settlement is retryable once a port is bound.
            Assert.IsNull(billing.SettleCancellation(contract, 28, diag), string.Join("; ", diag));
            Assert.AreEqual(35000, port.Balances[BuilderKey]);
            Assert.AreEqual(ConstructionPaymentObligationStatus.SettledExternally, releasedMilestone.Status);
            Assert.AreEqual(ConstructionPaymentObligationStatus.Voided, deposit.Status);
        }

        [Test]
        public void CancelBuilderContract_SettlesThroughBilling_AndRecordsDecision()
        {
            ConstructionPaymentObligation deposit;
            ConstructionPaymentObligation releasedMilestone;
            ConstructionContract contract = BuildContractReadyToSettle(out deposit, out releasedMilestone);
            var record = new NpcConstructionRecord
            {
                RecordId = "REC-1",
                ProjectId = "PROJ-1",
                ContractId = "CTR-0001",
            };
            var commissioner = new NpcConstructionCommissioner();

            Assert.IsNull(commissioner.CancelBuilderContract(contract, billing, record,
                "owner cancelled the build", "Ada Owner", 26, diag), string.Join("; ", diag));

            Assert.AreEqual(ConstructionContractStatus.Cancelled, contract.Status);
            Assert.AreEqual(35000, port.Balances[BuilderKey]);
            Assert.AreEqual(ConstructionPaymentObligationStatus.SettledExternally, releasedMilestone.Status);
            Assert.AreEqual(ConstructionPaymentObligationStatus.Voided, deposit.Status);
            Assert.IsTrue(record.Decisions.Count > 0);
            StringAssert.Contains("CTR-0001", record.Decisions[record.Decisions.Count - 1]);
        }

        [Test]
        public void CancelBuilderContract_RefusesWrongContract()
        {
            ConstructionPaymentObligation deposit;
            ConstructionPaymentObligation releasedMilestone;
            ConstructionContract contract = BuildContractReadyToSettle(out deposit, out releasedMilestone);
            var record = new NpcConstructionRecord
            {
                RecordId = "REC-1",
                ProjectId = "PROJ-1",
                ContractId = "CTR-9999",
            };
            var commissioner = new NpcConstructionCommissioner();

            Assert.IsNotNull(commissioner.CancelBuilderContract(contract, billing, record,
                "owner cancelled", "Ada Owner", 26, diag));
            Assert.AreNotEqual(ConstructionContractStatus.Cancelled, contract.Status,
                "refusing to cancel the wrong contract");
            Assert.AreEqual(13500, port.Balances[BuilderKey], "only the milestone draw moved");
        }
    }
}
