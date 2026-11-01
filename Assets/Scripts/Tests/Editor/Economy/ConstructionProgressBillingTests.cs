using System.Collections.Generic;
using LandLedgers.Economy;
using NUnit.Framework;

namespace LandLedgers.EditorTests.Economy
{
    /// <summary>
    /// D4H: progress billing — deposits, milestone draws against accepted
    /// stages, retainage, the completion balance, overbilling guards, and
    /// external settlement recording. Canon Part VI §7.3 (payment timing as
    /// contract data); Audit 04 (no completion without acceptance).
    ///
    /// Money moves through a fake <see cref="IConstructionBillingCashPort"/>
    /// holding REAL discrete balances: shortfalls are refused, never minted.
    /// </summary>
    [TestFixture]
    public sealed class ConstructionProgressBillingTests
    {
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
        private List<string> diag;

        private const string OwnerKey = "person:7";
        private const string BuilderKey = "business:build-1";

        [SetUp]
        public void SetUp()
        {
            port = new FakeConstructionBillingCashPort();
            port.Fund(OwnerKey, 100000);
            port.Fund(BuilderKey, 0);
            billing = new ConstructionProgressBillingService(port);
            diag = new List<string>();
        }

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

        // ---------------- deposits ----------------

        [Test]
        public void Deposit_BillsThroughPort_AndMarksObligationSettled()
        {
            ConstructionContract contract = MakeContract("CTR-0001", 50000);
            contract.PaymentTerms.DepositCents = 10000;

            Assert.IsNull(billing.RecordDeposit(contract, 6, diag));

            Assert.AreEqual(90000, port.Balances[OwnerKey]);
            Assert.AreEqual(10000, port.Balances[BuilderKey]);
            ConstructionPaymentObligation obligation = FindObligation(contract, "deposit");
            Assert.IsNotNull(obligation);
            Assert.AreEqual(ConstructionPaymentObligationStatus.SettledExternally, obligation.Status);
            IReadOnlyList<ConstructionBillingEvent> events = billing.EventsForContract("CTR-0001");
            Assert.AreEqual(1, events.Count);
            Assert.AreEqual("deposit", events[0].Kind);
            Assert.AreEqual(10000, events[0].AmountCents);
        }

        [Test]
        public void Deposit_RefusesWhenTermsNameNoDeposit()
        {
            ConstructionContract contract = MakeContract("CTR-0001", 50000);

            string refusal = billing.RecordDeposit(contract, 6, diag);

            Assert.IsNotNull(refusal);
            Assert.AreEqual(0, billing.EventsForContract("CTR-0001").Count);
            Assert.AreEqual(100000, port.Balances[OwnerKey]);
            Assert.AreEqual(0, port.Balances[BuilderKey]);
        }

        [Test]
        public void Deposit_RefusesDoubleBilling()
        {
            ConstructionContract contract = MakeContract("CTR-0001", 50000);
            contract.PaymentTerms.DepositCents = 10000;

            Assert.IsNull(billing.RecordDeposit(contract, 6, diag));
            string refusal = billing.RecordDeposit(contract, 7, diag);

            Assert.IsNotNull(refusal);
            Assert.AreEqual(1, billing.EventsForContract("CTR-0001").Count);
            Assert.AreEqual(10000, port.Balances[BuilderKey]);
        }

        [Test]
        public void Deposit_RefusesShortfall_AndLeavesObligationRecorded()
        {
            ConstructionContract contract = MakeContract("CTR-0001", 50000);
            contract.PaymentTerms.DepositCents = 10000;
            port.Fund(OwnerKey, 100); // the owner cannot cover the deposit

            string refusal = billing.RecordDeposit(contract, 6, diag);

            Assert.IsNotNull(refusal);
            StringAssert.Contains("shortfall", refusal.ToLowerInvariant());
            ConstructionPaymentObligation obligation = FindObligation(contract, "deposit");
            Assert.IsNotNull(obligation);
            Assert.AreEqual(ConstructionPaymentObligationStatus.Recorded, obligation.Status);
            Assert.AreEqual(0, billing.EventsForContract("CTR-0001").Count);
            Assert.AreEqual(0, port.Balances[BuilderKey]);

            // A retry after the owner funds up succeeds against the same obligation.
            port.Fund(OwnerKey, 100000);
            Assert.IsNull(billing.RecordDeposit(contract, 7, diag));
            Assert.AreEqual(ConstructionPaymentObligationStatus.SettledExternally, obligation.Status);
            Assert.AreEqual(1, billing.EventsForContract("CTR-0001").Count);
        }

        [Test]
        public void Deposit_RefusesOnCancelledContract()
        {
            ConstructionContract contract = MakeContract("CTR-0001", 50000);
            contract.PaymentTerms.DepositCents = 10000;
            Assert.IsNull(contract.Cancel(6, diag));

            Assert.IsNotNull(billing.RecordDeposit(contract, 7, diag));
            Assert.AreEqual(0, port.Balances[BuilderKey]);
        }

        // ---------------- milestone draws ----------------

        [Test]
        public void RecordMilestoneStageLink_RefusesUnknownMilestoneAndStage()
        {
            ConstructionContract contract = MakeContract("CTR-0001", 50000);
            AddMilestone(contract, "MILE-0001", "foundation complete", 15000);

            Assert.IsNotNull(billing.RecordMilestoneStageLink(contract, "MILE-9999", "STAGE-0001", 8, diag));
            Assert.IsNotNull(billing.RecordMilestoneStageLink(contract, "MILE-0001", "STAGE-9999", 8, diag));
            Assert.AreEqual(0, billing.MilestoneStageLinks.Count);
        }

        [Test]
        public void RecordMilestone_RefusesWithoutStageLink()
        {
            ConstructionContract contract = MakeContract("CTR-0001", 50000);
            AddMilestone(contract, "MILE-0001", "foundation complete", 15000);
            StartWorkAndAcceptBothStages(contract);

            string refusal = billing.RecordMilestone(contract, "MILE-0001", 21, diag);

            Assert.IsNotNull(refusal);
            StringAssert.Contains("stage link", refusal);
            Assert.AreEqual(0, billing.EventsForContract("CTR-0001").Count);
        }

        [Test]
        public void RecordMilestone_RefusesWhenStageNotAccepted()
        {
            ConstructionContract contract = MakeContract("CTR-0001", 50000);
            AddMilestone(contract, "MILE-0001", "foundation complete", 15000);
            Assert.IsNull(contract.StartWork(5, diag));
            Assert.IsNull(billing.RecordMilestoneStageLink(contract, "MILE-0001", "STAGE-0001", 6, diag));

            string refusal = billing.RecordMilestone(contract, "MILE-0001", 7, diag);

            Assert.IsNotNull(refusal);
            StringAssert.Contains("never ahead of work", refusal);
            Assert.AreEqual(0, port.Balances[BuilderKey]);
        }

        [Test]
        public void RecordMilestone_BillsNinetyPercent_HoldsTenPercentRetainage()
        {
            ConstructionContract contract = MakeContract("CTR-0001", 50000);
            AddMilestone(contract, "MILE-0001", "foundation complete", 15000);
            StartWorkAndAcceptBothStages(contract);
            Assert.IsNull(billing.RecordMilestoneStageLink(contract, "MILE-0001", "STAGE-0001", 6, diag));

            Assert.IsNull(billing.RecordMilestone(contract, "MILE-0001", 21, diag));

            // Default policy: 10% retainage on milestone draws.
            Assert.AreEqual(13500, port.Balances[BuilderKey]);
            Assert.AreEqual(1500, billing.RetainageBalanceCents("CTR-0001"));
            ConstructionPaymentObligation obligation = FindObligation(contract, "milestone:MILE-0001");
            Assert.IsNotNull(obligation);
            Assert.AreEqual(15000, obligation.AmountCents);
            Assert.AreEqual(ConstructionPaymentObligationStatus.SettledExternally, obligation.Status);
            IReadOnlyList<ConstructionBillingEvent> events = billing.EventsForContract("CTR-0001");
            Assert.AreEqual(1, events.Count);
            Assert.AreEqual(13500, events[0].AmountCents);
        }

        [Test]
        public void RecordMilestone_RefusesDoubleBilling()
        {
            ConstructionContract contract = MakeContract("CTR-0001", 50000);
            AddMilestone(contract, "MILE-0001", "foundation complete", 15000);
            StartWorkAndAcceptBothStages(contract);
            Assert.IsNull(billing.RecordMilestoneStageLink(contract, "MILE-0001", "STAGE-0001", 6, diag));

            Assert.IsNull(billing.RecordMilestone(contract, "MILE-0001", 21, diag));
            string refusal = billing.RecordMilestone(contract, "MILE-0001", 22, diag);

            Assert.IsNotNull(refusal);
            Assert.AreEqual(1, billing.EventsForContract("CTR-0001").Count);
        }

        [Test]
        public void Policy_DisabledRetainage_MovesFullMilestoneAmount()
        {
            ConstructionContract contract = MakeContract("CTR-0001", 50000);
            AddMilestone(contract, "MILE-0001", "foundation complete", 15000);
            StartWorkAndAcceptBothStages(contract);
            Assert.IsNull(billing.RecordMilestoneStageLink(contract, "MILE-0001", "STAGE-0001", 6, diag));
            billing.Policy = new ConstructionProgressBillingPolicy { RetainageEnabled = false };

            Assert.IsNull(billing.RecordMilestone(contract, "MILE-0001", 21, diag));

            Assert.AreEqual(15000, port.Balances[BuilderKey]);
            Assert.AreEqual(0, billing.RetainageBalanceCents("CTR-0001"));
        }

        // ---------------- overbilling guards ----------------

        [Test]
        public void OverbillingGuard_RefusesDrawExceedingContractPrice()
        {
            ConstructionContract contract = MakeContract("CTR-0001", 50000);
            contract.PaymentTerms.DepositCents = 10000;
            AddMilestone(contract, "MILE-0001", "foundation complete", 45000);
            StartWorkAndAcceptBothStages(contract);
            Assert.IsNull(billing.RecordMilestoneStageLink(contract, "MILE-0001", "STAGE-0001", 6, diag));

            Assert.IsNull(billing.RecordDeposit(contract, 6, diag));
            string refusal = billing.RecordMilestone(contract, "MILE-0001", 21, diag);

            Assert.IsNotNull(refusal);
            StringAssert.Contains("overbilling", refusal.ToLowerInvariant());
            Assert.AreEqual(1, billing.EventsForContract("CTR-0001").Count);
            Assert.AreEqual(10000, port.Balances[BuilderKey]);
        }

        [Test]
        public void AcceptedChangeOrder_RaisesCeiling_AllowsBiggerDraw()
        {
            ConstructionContract contract = MakeContract("CTR-0001", 50000);
            contract.PaymentTerms.DepositCents = 10000;
            AddMilestone(contract, "MILE-0001", "foundation complete", 45000);
            StartWorkAndAcceptBothStages(contract);
            Assert.IsNull(billing.RecordMilestoneStageLink(contract, "MILE-0001", "STAGE-0001", 6, diag));

            Assert.IsNull(billing.RecordDeposit(contract, 6, diag));
            Assert.IsNotNull(billing.RecordMilestone(contract, "MILE-0001", 21, diag));

            Assert.IsNull(contract.ProposeChangeOrder("deeper foundation", 5000, 0,
                ConstructionContractSide.Builder, 22, diag));
            Assert.IsNull(contract.RecordChangeOrderAcceptance("CO-0001",
                ConstructionContractSide.Owner, 23, diag));
            Assert.IsNull(contract.RecordChangeOrderAcceptance("CO-0001",
                ConstructionContractSide.Builder, 23, diag));
            Assert.AreEqual(55000, contract.AgreedPriceCents);

            Assert.IsNull(billing.RecordMilestone(contract, "MILE-0001", 24, diag));
            Assert.AreEqual(10000 + 40500, port.Balances[BuilderKey]);
        }

        // ---------------- completion balance + retainage release ----------------

        [Test]
        public void RecordCompletionBalance_RefusesBeforeFinalAcceptance()
        {
            ConstructionContract contract = MakeContract("CTR-0001", 45000);
            contract.PaymentTerms.CompletionBalanceCents = 5000;
            Assert.IsNull(contract.StartWork(5, diag));

            Assert.IsNotNull(billing.RecordCompletionBalance(contract, 30, diag));
            Assert.AreEqual(0, port.Balances[BuilderKey]);
        }

        [Test]
        public void FullLifecycle_ReleasesRetainageOnFinalAcceptance()
        {
            ConstructionContract contract = MakeContract("CTR-0001", 45000);
            contract.PaymentTerms.DepositCents = 10000;
            contract.PaymentTerms.CompletionBalanceCents = 5000;
            AddMilestone(contract, "MILE-0001", "foundation complete", 15000);
            AddMilestone(contract, "MILE-0002", "framing complete", 15000);

            StartWorkAndAcceptBothStages(contract);
            Assert.IsNull(billing.RecordMilestoneStageLink(contract, "MILE-0001", "STAGE-0001", 6, diag));
            Assert.IsNull(billing.RecordMilestoneStageLink(contract, "MILE-0002", "STAGE-0002", 6, diag));

            Assert.IsNull(billing.RecordDeposit(contract, 6, diag));
            Assert.IsNull(billing.RecordMilestone(contract, "MILE-0001", 21, diag));
            Assert.IsNull(billing.RecordMilestone(contract, "MILE-0002", 22, diag));
            Assert.AreEqual(3000, billing.RetainageBalanceCents("CTR-0001"));

            Assert.IsNull(contract.FinalAccept(30, "Ada Owner", diag));
            ConstructionPaymentObligation balance = FindObligation(contract, "final-balance");
            Assert.IsNotNull(balance);
            Assert.AreEqual(5000, balance.AmountCents);

            Assert.IsNull(billing.RecordCompletionBalance(contract, 31, diag));

            // Builder received everything the contract scheduled: deposit
            // 10000 + milestones 30000 + balance 5000 = 45000 = price.
            Assert.AreEqual(45000, port.Balances[BuilderKey]);
            Assert.AreEqual(0, billing.RetainageBalanceCents("CTR-0001"));
            Assert.AreEqual(ConstructionPaymentObligationStatus.SettledExternally, balance.Status);

            IReadOnlyList<ConstructionBillingEvent> events = billing.EventsForContract("CTR-0001");
            Assert.AreEqual(5, events.Count); // deposit, 2 milestones, final-balance, retainage-release
            int moved = 0;
            bool sawRetainageRelease = false;
            foreach (ConstructionBillingEvent e in events)
            {
                moved += e.AmountCents;
                if (e.Kind == "retainage-release")
                {
                    sawRetainageRelease = true;
                    Assert.AreEqual(3000, e.AmountCents);
                }
            }
            Assert.IsTrue(sawRetainageRelease);
            Assert.AreEqual(45000, moved);
            Assert.AreEqual(45000, billing.CumulativeBilledCents("CTR-0001"));
        }

        [Test]
        public void RecordCompletionBalance_RefusesDoubleBilling()
        {
            ConstructionContract contract = MakeContract("CTR-0001", 45000);
            contract.PaymentTerms.CompletionBalanceCents = 5000;
            Assert.IsNull(contract.StartWork(5, diag));
            Assert.IsNull(contract.AcceptStage("STAGE-0001", 10, "Ada Owner", diag));
            Assert.IsNull(contract.AcceptStage("STAGE-0002", 20, "Ada Owner", diag));
            Assert.IsNull(contract.FinalAccept(30, "Ada Owner", diag));

            Assert.IsNull(billing.RecordCompletionBalance(contract, 31, diag));
            Assert.IsNotNull(billing.RecordCompletionBalance(contract, 32, diag));
            Assert.AreEqual(5000, port.Balances[BuilderKey]);
        }

        // ---------------- external settlement ----------------

        [Test]
        public void MarkObligationSettled_RecordsExternalSettlement_AndCountsTowardCeiling()
        {
            ConstructionContract contract = MakeContract("CTR-0001", 50000);
            contract.PaymentTerms.DepositCents = 10000;
            // The owner pays the deposit in cash, outside the billing service.
            var obligation = new ConstructionPaymentObligation
            {
                ObligationId = "PAY-0001",
                ContractId = "CTR-0001",
                Kind = "deposit",
                Description = "Contract deposit due at signing",
                AmountCents = 10000,
                RecordedDayIndex = 4,
                Status = ConstructionPaymentObligationStatus.Recorded,
            };
            contract.PaymentObligations.Add(obligation);

            Assert.IsNull(billing.MarkObligationSettled(contract, "PAY-0001", "Ada Owner (cash)", 6, diag));

            Assert.AreEqual(ConstructionPaymentObligationStatus.SettledExternally, obligation.Status);
            IReadOnlyList<ConstructionBillingEvent> events = billing.EventsForContract("CTR-0001");
            Assert.AreEqual(1, events.Count);
            Assert.AreEqual("settlement", events[0].Kind);
            Assert.AreEqual(10000, events[0].AmountCents);
            Assert.AreEqual(10000, billing.CumulativeBilledCents("CTR-0001"));
            // Nothing moved through the port — recording only.
            Assert.AreEqual(0, port.Balances[BuilderKey]);

            // ...and the billing service will not bill it again.
            Assert.IsNotNull(billing.RecordDeposit(contract, 7, diag));
        }

        [Test]
        public void MarkObligationSettled_RefusesUnknownAndAlreadySettled()
        {
            ConstructionContract contract = MakeContract("CTR-0001", 50000);

            Assert.IsNotNull(billing.MarkObligationSettled(contract, "PAY-9999", "Ada Owner", 6, diag));

            var obligation = new ConstructionPaymentObligation
            {
                ObligationId = "PAY-0001",
                ContractId = "CTR-0001",
                Kind = "deposit",
                AmountCents = 10000,
                Status = ConstructionPaymentObligationStatus.SettledExternally,
            };
            contract.PaymentObligations.Add(obligation);
            Assert.IsNotNull(billing.MarkObligationSettled(contract, "PAY-0001", "Ada Owner", 6, diag));
            Assert.AreEqual(0, billing.EventsForContract("CTR-0001").Count);
        }

        // ---------------- events + save ----------------

        [Test]
        public void EventsForContract_IsolatedPerContract()
        {
            ConstructionContract first = MakeContract("CTR-0001", 50000);
            first.PaymentTerms.DepositCents = 10000;
            ConstructionContract second = MakeContract("CTR-0002", 50000);
            second.PaymentTerms.DepositCents = 5000;

            Assert.IsNull(billing.RecordDeposit(first, 6, diag));
            Assert.IsNull(billing.RecordDeposit(second, 6, diag));

            Assert.AreEqual(1, billing.EventsForContract("CTR-0001").Count);
            Assert.AreEqual(1, billing.EventsForContract("CTR-0002").Count);
            Assert.AreEqual(0, billing.EventsForContract("CTR-9999").Count);
        }

        [Test]
        public void SaveLoad_RoundTripsEventsLinksRetainageAndPolicy()
        {
            ConstructionContract contract = MakeContract("CTR-0001", 45000);
            contract.PaymentTerms.DepositCents = 10000;
            AddMilestone(contract, "MILE-0001", "foundation complete", 15000);
            StartWorkAndAcceptBothStages(contract);
            Assert.IsNull(billing.RecordMilestoneStageLink(contract, "MILE-0001", "STAGE-0001", 6, diag));
            Assert.IsNull(billing.RecordDeposit(contract, 6, diag));
            Assert.IsNull(billing.RecordMilestone(contract, "MILE-0001", 21, diag));

            ConstructionProgressBillingService.ConstructionProgressBillingServiceSaveDto dto =
                billing.CaptureSaveDto();
            var restored = new ConstructionProgressBillingService(port);
            restored.LoadFromSaveDto(dto);

            Assert.AreEqual(2, restored.EventsForContract("CTR-0001").Count);
            Assert.AreEqual(1, restored.MilestoneStageLinks.Count);
            Assert.AreEqual(1500, restored.RetainageBalanceCents("CTR-0001"));
            Assert.AreEqual(23500, restored.CumulativeBilledCents("CTR-0001"));
            Assert.AreEqual(10, restored.Policy.RetainagePercentWhole);

            // The restored service keeps working: no double-bill, events keep numbering.
            Assert.IsNotNull(restored.RecordDeposit(contract, 22, diag));
            Assert.AreEqual(2, restored.EventsForContract("CTR-0001").Count);
        }
    }
}
