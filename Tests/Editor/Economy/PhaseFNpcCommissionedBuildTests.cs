using System.Collections.Generic;
using LandLedgers.Economy;
using LandLedgers.Economy.Financing;
using LandLedgers.Population;
using LandLedgers.Primitives;
using LandLedgers.World.Property;
using NUnit.Framework;

namespace LandLedgers.EditorTests.Economy
{
    /// <summary>
    /// Phase F (F3): the NPC as construction principal — commissioning the
    /// design, funding material staging from real lots with real cash,
    /// committing own/hired labor with person-time respected, answering
    /// cost overruns explicitly, or hiring a builder business under a real
    /// contract with gated billing. All run in the Roslyn harness.
    /// </summary>
    [TestFixture]
    public sealed class PhaseFNpcCommissionedBuildTests
    {
        private sealed class FakeMaterialSource : IConstructionMaterialSource
        {
            public readonly Dictionary<string, int> Stock = new Dictionary<string, int>();
            public int AvailableUnits(ConstructionMaterialRequirement requirement)
            {
                return requirement == null ? 0
                    : Stock.TryGetValue(requirement.MaterialDisplayName, out int units) ? units : 0;
            }
            public string TryConsume(ConstructionMaterialRequirement requirement, int units,
                string projectLabel, int dayIndex, List<string> diagnostics, out string provenanceLabel)
            {
                provenanceLabel = string.Empty;
                int have = AvailableUnits(requirement);
                if (have < units) return $"FakeMaterialSource: only {have} of '{requirement.MaterialDisplayName}'.";
                Stock[requirement.MaterialDisplayName] = have - units;
                provenanceLabel = $"lot '{requirement.MaterialDisplayName}' x{units} ({projectLabel}, day {dayIndex})";
                return null;
            }
        }

        private sealed class FakePricePort : IConstructionMaterialPricePort
        {
            public readonly Dictionary<string, int> Prices = new Dictionary<string, int>();
            public int UnitPriceCents(ConstructionMaterialRequirement requirement)
            {
                if (requirement == null) return -1;
                return Prices.TryGetValue(requirement.MaterialDisplayName, out int price) ? price : -1;
            }
        }

        private sealed class FakeToolCustody : IHouseholdToolCustody
        {
            private readonly HashSet<string> held = new HashSet<string>();
            public void Grant(int householdId, string toolId) => held.Add(householdId + ":" + toolId);
            public bool HouseholdHoldsTool(int householdId, string toolItemId) =>
                held.Contains(householdId + ":" + toolItemId);
        }

        private sealed class BridgeCashPort : IConstructionBillingCashPort
        {
            private readonly CreditCashBridge cash;
            public BridgeCashPort(CreditCashBridge cash) { this.cash = cash; }
            public string MoveCash(string fromPartyKey, string toPartyKey, int amountCents,
                int dayIndex, string memo, List<string> diagnostics)
            {
                CreditCashAccount from = cash.OpenWindow(fromPartyKey, diagnostics);
                CreditCashAccount to = cash.OpenWindow(toPartyKey, diagnostics);
                if (from == null || to == null)
                {
                    cash.DiscardWindow(from); cash.DiscardWindow(to);
                    return $"unknown party '{fromPartyKey}' or '{toPartyKey}'.";
                }
                if (from.BalanceCents < amountCents)
                {
                    cash.DiscardWindow(from); cash.DiscardWindow(to);
                    return $"'{fromPartyKey}' holds {from.BalanceCents}c, needs {amountCents}c.";
                }
                from.BalanceCents -= amountCents;
                to.BalanceCents += amountCents;
                string p1 = cash.CommitWindow(from, dayIndex, memo, toPartyKey, diagnostics);
                string p2 = cash.CommitWindow(to, dayIndex, memo, fromPartyKey, diagnostics);
                return p1 ?? p2;
            }
            public string TryGetCashBalance(string partyKey, out int balanceCents)
            {
                IRealCashStore store = cash.FindStore(partyKey);
                if (store == null) { balanceCents = 0; return $"unknown party '{partyKey}'."; }
                balanceCents = store.ReadBalanceCents();
                return null;
            }
        }

        private sealed class Fixture
        {
            public EntityIdRegistry Ids = new EntityIdRegistry();
            public FinancialObligationAuthority Authority = new FinancialObligationAuthority();
            public CreditCashBridge Cash = new CreditCashBridge();
            public BuildingDesignCatalog Catalog = new BuildingDesignCatalog();
            public FakeMaterialSource Materials = new FakeMaterialSource();
            public FakePricePort Prices = new FakePricePort();
            public PersonScheduleTracker Tracker = new PersonScheduleTracker();
            public FakeToolCustody Custody = new FakeToolCustody();
            public EmploymentRelationshipRegistry Employments = new EmploymentRelationshipRegistry();
            public ConstructionProjectExecutor Executor = new ConstructionProjectExecutor();
            public NpcConstructionCommissioner Commissioner = new NpcConstructionCommissioner();
            public HousingAuthority Housing = new HousingAuthority();
            public ConstructionContractBook ContractBook = new ConstructionContractBook();
            public List<string> Diag = new List<string>();

            public Fixture()
            {
                Materials.Stock["fieldstone"] = 1000;
                Materials.Stock["lumber"] = 10000;
                Prices.Prices["fieldstone"] = 50;  // 40 x 50 = 2000c
                Prices.Prices["lumber"] = 40;      // 600 x 40 = 24000c
                Custody.Grant(7, "shovel");
                Custody.Grant(7, "saw");
            }

            public HouseholdLedger RegisterHousehold(int householdId, int cashCents)
            {
                var ledger = new HouseholdLedger(householdId);
                Assert.IsNull(ledger.RecordInflow(0, cashCents, HouseholdIncomeSource.OwnerContribution,
                    "test-seed", "test seed capital", "test"));
                Assert.IsNull(Cash.Register("household:" + householdId,
                    new HouseholdCashStore(householdId, ledger)));
                return ledger;
            }

            public PurseCashStore RegisterPurse(string owner, int balanceCents)
            {
                var purse = new PurseCashStore(owner, balanceCents, "test seed capital");
                Assert.IsNull(Cash.Register(owner, purse));
                return purse;
            }

            public BuildingDesign CabinDesign()
            {
                var design = new BuildingDesign
                {
                    DesignId = "cabin-a", DisplayName = "Settler cabin", KindLabel = "cabin",
                };
                var foundation = new BuildingDesignPhase { PhaseName = "Foundation", Sequence = 0, LaborMinutes = 240 };
                foundation.Materials.Add(new ConstructionMaterialRequirement
                {
                    RequirementId = "REQ-stone", MaterialKind = ConstructionMaterialKind.Other,
                    MaterialDisplayName = "fieldstone", RequiredUnits = 40, UnitLabel = "perch",
                    ProvenanceKind = ConstructionMaterialProvenanceKind.AnyRealSource,
                    SupplierBusinessId = "lumber-yard", SupplierDisplayName = "Lumber Yard",
                });
                foundation.RequiredToolItemIds.Add("shovel");
                var framing = new BuildingDesignPhase { PhaseName = "Framing", Sequence = 1, LaborMinutes = 480 };
                framing.Materials.Add(new ConstructionMaterialRequirement
                {
                    RequirementId = "REQ-lumber", MaterialKind = ConstructionMaterialKind.Other,
                    MaterialDisplayName = "lumber", RequiredUnits = 600, UnitLabel = "board feet",
                    ProvenanceKind = ConstructionMaterialProvenanceKind.AnyRealSource,
                    SupplierBusinessId = "lumber-yard", SupplierDisplayName = "Lumber Yard",
                });
                framing.RequiredToolItemIds.Add("saw");
                design.Phases.Add(foundation);
                design.Phases.Add(framing);
                design.SpaceSpecs.Add(new BuildingDesignSpaceSpec { Label = "main room", SleepingCapacity = 4 });
                return design;
            }

            public ConstructionProject CommissionCabin(out NpcConstructionRecord record, int householdId = 7)
            {
                Assert.IsNull(Catalog.RegisterDesign(CabinDesign(), Diag));
                ConstructionProject project = Commissioner.CommissionProject(Executor, Catalog,
                    householdId, "cabin-a", "parcel-9", Prices, 10, Diag, out record);
                Assert.IsNotNull(project, string.Join("; ", Diag));
                Assert.IsNotNull(record);
                return project;
            }
        }

        private string FundStage(Fixture f, ConstructionProject project, NpcConstructionRecord record,
            WorkPackage package, int day)
        {
            return f.Commissioner.FundAndStagePackage(f.Executor, project, record, package,
                f.Materials, f.Prices, f.Cash, "household:7", "business:lumber-yard",
                f.Authority, f.Ids, day, f.Diag);
        }

        [Test]
        public void SelfBuild_EndToEnd_OwnLabor_MaterialsPaidFromRealCash()
        {
            var f = new Fixture();
            HouseholdLedger ledger = f.RegisterHousehold(7, 100000);
            PurseCashStore yard = f.RegisterPurse("business:lumber-yard", 0);

            NpcConstructionRecord record;
            ConstructionProject project = f.CommissionCabin(out record);
            Assert.AreEqual(26000, record.TotalEstimatedCostCents);

            WorkPackage foundation = project.WorkPackages[0];
            WorkPackage framing = project.WorkPackages[1];

            Assert.IsNull(FundStage(f, project, record, foundation, 11), string.Join("; ", f.Diag));
            Assert.AreEqual(2000, record.FindFunding(foundation.PackageId).ActualMaterialCostCents);
            Assert.IsNull(f.Commissioner.CommitOwnLabor(f.Executor, project, record, foundation,
                5, f.Tracker, f.Custody, 11, 360, 240, f.Diag), string.Join("; ", f.Diag));
            Assert.IsNull(f.Executor.AdvancePackage(project, foundation, 11, f.Diag));

            Assert.IsNull(FundStage(f, project, record, framing, 12), string.Join("; ", f.Diag));
            Assert.IsNull(f.Commissioner.CommitOwnLabor(f.Executor, project, record, framing,
                5, f.Tracker, f.Custody, 12, 360, 480, f.Diag), string.Join("; ", f.Diag));
            Assert.IsNull(f.Executor.AdvancePackage(project, framing, 12, f.Diag));

            Assert.IsNull(f.Executor.CompleteProject(project, f.Catalog.FindDesign("cabin-a"),
                f.Housing, 12, f.Diag));
            Assert.AreEqual(ConstructionProjectStatus.Complete, project.Status);
            Assert.IsNull(f.Executor.OccupyCompletedHome(project, f.Housing,
                new List<int> { 5, 6 }, AccommodationArrangement.OwnerOccupied, 13, f.Diag));

            // Conservation: the household paid exactly the staged material
            // cost; the supplier received it; lots were really consumed.
            Assert.AreEqual(100000 - 26000, ledger.GetBalanceCents());
            Assert.AreEqual(26000, yard.ReadBalanceCents());
            Assert.AreEqual(960, f.Materials.Stock["fieldstone"]);
            Assert.AreEqual(9400, f.Materials.Stock["lumber"]);
            Assert.AreEqual(2, project.MaterialsConsumed.Count);
            Assert.IsFalse(string.IsNullOrWhiteSpace(project.MaterialsConsumed[0].Provenance));
            Assert.AreEqual(AccommodationArrangement.OwnerOccupied,
                f.Housing.CurrentOccupanciesForPerson(5)[0].Arrangement);
            // The principal's record tells the story.
            Assert.AreEqual(2, record.PackageFundings.Count);
            Assert.IsTrue(record.Decisions.Count >= 3);
        }

        [Test]
        public void HiredWorker_EmploymentRegistered_WagesPaid_PersonTimeRespected()
        {
            var f = new Fixture();
            HouseholdLedger ledger = f.RegisterHousehold(7, 100000);
            HouseholdLedger workerLedger = f.RegisterHousehold(9, 1000);
            f.RegisterPurse("business:lumber-yard", 0);

            NpcConstructionRecord record;
            ConstructionProject project = f.CommissionCabin(out record);
            WorkPackage foundation = project.WorkPackages[0];
            WorkPackage framing = project.WorkPackages[1];
            Assert.IsNull(FundStage(f, project, record, foundation, 11), string.Join("; ", f.Diag));
            Assert.IsNull(FundStage(f, project, record, framing, 11), string.Join("; ", f.Diag));

            // Hire P21 (of H9) at 4800c/week for the foundation's 240 minutes.
            Assert.IsNull(f.Commissioner.HireWorker(f.Employments, f.Executor, project, record,
                foundation, 21, 9, "household:7 construction", 4800,
                f.Tracker, f.Custody, 11, 360, 240, f.Diag), string.Join("; ", f.Diag));
            Assert.IsTrue(f.Employments.TryGetById(
                $"emp-{project.ProjectId}-{foundation.PackageId}-21-11", out EmploymentRelationship rel));
            Assert.AreEqual(4800, rel.Compensation.AgreedWeeklyWageCents);

            // The same person cannot be double-booked on overlapping time —
            // the schedule tracker refuses loudly through the executor.
            string doubleBook = f.Commissioner.HireWorker(f.Employments, f.Executor, project, record,
                framing, 21, 9, "household:7 construction", 4800,
                f.Tracker, f.Custody, 11, 400, 240, f.Diag);
            Assert.IsNotNull(doubleBook, "overlapping person-time must be refused.");
            StringAssert.Contains("refused", doubleBook.ToLowerInvariant());

            Assert.IsNull(f.Executor.AdvancePackage(project, foundation, 11, f.Diag));

            // Wages on completion: 240 min of a 2400-min week at 4800c/week = 480c.
            var workerHouseholds = new Dictionary<int, int> { { 21, 9 } };
            Assert.IsNull(f.Commissioner.PayWorkerForPackage(f.Employments, record, foundation,
                f.Cash, "household:7", workerHouseholds, 12, f.Diag), string.Join("; ", f.Diag));
            Assert.AreEqual(480, record.FindFunding(foundation.PackageId).WagesPaidCents);
            Assert.AreEqual(1000 + 480, workerLedger.GetBalanceCents());
            Assert.AreEqual(100000 - 26000 - 480, ledger.GetBalanceCents());
        }

        [Test]
        public void CostOverrun_InjectCash_ResolvesAndContinues()
        {
            var f = new Fixture();
            f.RegisterHousehold(7, 100000);
            f.RegisterPurse("business:lumber-yard", 0);

            NpcConstructionRecord record;
            ConstructionProject project = f.CommissionCabin(out record);
            WorkPackage foundation = project.WorkPackages[0];
            Assert.IsNull(FundStage(f, project, record, foundation, 11), string.Join("; ", f.Diag));

            // Lumber prices spike before the framing package is funded:
            // 600 x 60 = 36000c vs 24000c estimated → overrun.
            f.Prices.Prices["lumber"] = 60;
            string overrun = FundStage(f, project, record, project.WorkPackages[1], 12);
            Assert.IsNotNull(overrun, "the overrun must block funding, not be absorbed.");
            StringAssert.Contains("overrun", overrun.ToLowerInvariant());
            Assert.AreEqual(1, record.Overruns.Count);
            Assert.IsFalse(record.Overruns[0].Resolved);
            Assert.AreEqual(WorkPackageStatus.Ready, project.WorkPackages[1].Status,
                "nothing staged while the overrun is open.");

            // The NPC injects the extra 12000c from real cash.
            Assert.IsNull(f.Commissioner.ResolveOverrun(record, project, 0,
                NpcBuildOverrunResolution.InjectCash, "lumber spike covered from reserves",
                f.Cash, "household:7", null, ConstructionContractSide.Owner, 12, f.Diag),
                string.Join("; ", f.Diag));
            Assert.IsTrue(record.Overruns[0].Resolved);

            Assert.IsNull(FundStage(f, project, record, project.WorkPackages[1], 12),
                string.Join("; ", f.Diag));
            Assert.AreEqual(36000, record.FindFunding(project.WorkPackages[1].PackageId).ActualMaterialCostCents);
        }

        [Test]
        public void CostOverrun_Pause_BlocksFundingUntilResumed()
        {
            var f = new Fixture();
            f.RegisterHousehold(7, 100000);
            f.RegisterPurse("business:lumber-yard", 0);

            NpcConstructionRecord record;
            ConstructionProject project = f.CommissionCabin(out record);
            f.Prices.Prices["lumber"] = 60;

            // The framing package overruns (600 x 60 = 36000c vs 24000c estimated).
            string overrun = FundStage(f, project, record, project.WorkPackages[1], 11);
            Assert.IsNotNull(overrun);
            Assert.IsNull(f.Commissioner.ResolveOverrun(record, project, 0,
                NpcBuildOverrunResolution.PauseProject, "waiting for prices to settle",
                f.Cash, "household:7", null, ConstructionContractSide.Owner, 11, f.Diag));
            Assert.AreEqual(ConstructionProjectStatus.Paused, project.Status);

            string blocked = FundStage(f, project, record, project.WorkPackages[1], 12);
            Assert.IsNotNull(blocked);
            StringAssert.Contains("paused", blocked.ToLowerInvariant());

            Assert.IsNull(f.Commissioner.ResumeProject(project, record, 13, f.Diag));
            Assert.AreNotEqual(ConstructionProjectStatus.Paused, project.Status);
        }

        [Test]
        public void HireBuilder_Contract_NoDrawWithoutAcceptedStage()
        {
            var f = new Fixture();
            f.RegisterPurse("person:5", 100000);
            PurseCashStore builderPurse = f.RegisterPurse("business:builder-1", 5000);

            NpcConstructionRecord record;
            ConstructionProject project = f.CommissionCabin(out record);

            var terms = new ConstructionPaymentTerms { DepositCents = 0, CompletionBalanceCents = 20000 };
            terms.Milestones.Add(new ConstructionPaymentMilestone
            {
                MilestoneId = "M-1", Name = "Foundation complete",
                TriggerDescription = "foundation accepted", AmountCents = 40000,
            });
            terms.Milestones.Add(new ConstructionPaymentMilestone
            {
                MilestoneId = "M-2", Name = "Framing complete",
                TriggerDescription = "framing accepted", AmountCents = 40000,
            });
            var capacity = new ConstructionContractCapacityReading
            {
                BuilderBusinessId = "builder-1", BuilderDisplayName = "Builder One",
                ActiveCrewCount = 4, TargetCrewCount = 4, SourceLabel = "builder crew roster",
            };
            ConstructionContract contract = f.Commissioner.CommissionBuilderContract(
                f.Executor, project, record, f.ContractBook, capacity,
                5, "Head of H7", "builder-1", "Builder One",
                "build cabin-a on parcel-9", 100000, terms,
                new List<string> { "Foundation", "Framing" }, 10, f.Diag);
            Assert.IsNotNull(contract, string.Join("; ", f.Diag));
            Assert.AreEqual("CTR-0001", contract.ContractId);
            Assert.AreEqual(contract.ContractId, project.ContractId);
            Assert.AreEqual(ConstructionContractStatus.Executed, contract.Status);
            Assert.IsNull(contract.StartWork(11, f.Diag));

            var billing = new ConstructionProgressBillingService(new BridgeCashPort(f.Cash));
            string stage1 = contract.AcceptanceStages[0].StageId;

            // No draw without an accepted stage — the billing service refuses.
            string refused = billing.RecordMilestone(contract, "M-1", 12, f.Diag);
            Assert.IsNotNull(refused, "a draw ahead of accepted work must be refused.");

            // The NPC accepts the builder's finished stage; the linked
            // milestone draw releases real cash to the builder.
            int builderBefore = builderPurse.ReadBalanceCents();
            Assert.IsNull(f.Commissioner.AcceptBuilderStageAndReleaseDraw(contract, billing,
                stage1, "M-1", "P5 (owner)", 13, f.Diag), string.Join("; ", f.Diag));
            Assert.IsTrue(builderPurse.ReadBalanceCents() > builderBefore,
                "the milestone draw must move real cash to the builder.");
            Assert.IsTrue(billing.EventsForContract(contract.ContractId).Count > 0);
        }

        [Test]
        public void SaveLoad_RoundTrip_NoDuplicatedRecords()
        {
            var f = new Fixture();
            f.RegisterHousehold(7, 100000);
            f.RegisterPurse("business:lumber-yard", 0);
            NpcConstructionRecord record;
            ConstructionProject project = f.CommissionCabin(out record);
            Assert.IsNull(FundStage(f, project, record, project.WorkPackages[0], 11),
                string.Join("; ", f.Diag));

            NpcConstructionCommissioner.NpcConstructionCommissionerSaveDto dto =
                f.Commissioner.CaptureSaveDto();
            var restored = new NpcConstructionCommissioner();
            restored.LoadFromSaveDto(dto, f.Diag);
            // Loading the same snapshot twice must not duplicate records.
            restored.LoadFromSaveDto(dto, f.Diag);
            NpcConstructionRecord found = restored.FindRecord(record.RecordId);
            Assert.IsNotNull(found);
            Assert.AreEqual(2, found.PackageFundings.Count,
                "one funding line per package — no duplication on re-load.");
            Assert.AreEqual(project.ProjectId, found.ProjectId);
            Assert.AreEqual("cabin-a", found.DesignId);
        }

        [Test]
        public void SelectDesign_RefusesWhatTheNpcCannotFund()
        {
            var f = new Fixture();
            Assert.IsNull(f.Catalog.RegisterDesign(f.CabinDesign(), f.Diag));
            var diag = new List<string>();

            // 26000c of materials against 10000c funds → refused.
            BuildingDesign refused = f.Commissioner.SelectDesign(f.Catalog, "cabin-a", 3,
                f.Prices, 10000, diag);
            Assert.IsNull(refused);
            // Unknown design → refused.
            Assert.IsNull(f.Commissioner.SelectDesign(f.Catalog, "mansion-x", 3, f.Prices, 100000, diag));
            // Funded → selected.
            BuildingDesign chosen = f.Commissioner.SelectDesign(f.Catalog, "cabin-a", 3,
                f.Prices, 100000, diag);
            Assert.IsNotNull(chosen);
        }
    }
}
