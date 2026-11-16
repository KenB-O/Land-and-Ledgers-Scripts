using System;
using System.Collections.Generic;
using LandLedgers.Economy;
using LandLedgers.Economy.Financing;
using LandLedgers.Population;
using LandLedgers.Primitives;
using LandLedgers.Tasks;
using LandLedgers.Time;
using LandLedgers.World.Property;
using NUnit.Framework;

namespace LandLedgers.EditorTests.Economy
{
    /// <summary>
    /// D-3: hired-labour production path. When no member of the
    /// commissioning household can work a package, the labour port falls
    /// back to hired workers from other households (never the player
    /// household): a real employment is registered at the agreed weekly
    /// wage, their time is committed and worked through the real task
    /// authorities, and the driver pays the wages when the package
    /// completes and ends the employments. Own labour is always tried
    /// first; a package with no available worker at all waits loudly.
    /// The production port lives in SimulationDrivers.cs
    /// (harness-excluded); these tests pin the driver hook and the
    /// hired-labour helper through a fake port that mirrors production
    /// (own first, then hired via NpcConstructionHiredLabor).
    /// </summary>
    [TestFixture]
    public sealed class NpcConstructionHiredLaborDef011Tests
    {
        private sealed class FakeParcelAuthority : IConstructionParcelAuthority
        {
            public readonly Dictionary<string, string> HolderByParcel = new Dictionary<string, string>();
            public string AuthorizeBuild(int householdId, string parcelId, int dayIndex, List<string> diagnostics)
            {
                if (string.IsNullOrWhiteSpace(parcelId)) return "no parcel id.";
                if (!HolderByParcel.TryGetValue(parcelId, out string holder))
                    return $"parcel '{parcelId}' is not a registered parcel.";
                if (!string.Equals(holder, "household:" + householdId, StringComparison.Ordinal))
                    return $"parcel '{parcelId}' is held by '{holder}', not 'household:{householdId}'.";
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

        private sealed class FakeToolCustody : IHouseholdToolCustody
        {
            private readonly HashSet<string> held = new HashSet<string>();
            public void Grant(int householdId, string toolId) => held.Add(householdId + ":" + toolId);
            public bool HouseholdHoldsTool(int householdId, string toolItemId) =>
                held.Contains(householdId + ":" + toolItemId);
        }

        private sealed class HiredChain
        {
            public List<string> Diag = new List<string>();
            public BuildingDesignCatalog Catalog = new BuildingDesignCatalog();
            public ConstructionProjectExecutor Executor = new ConstructionProjectExecutor();
            public NpcConstructionCommissioner Commissioner = new NpcConstructionCommissioner();
            public EmploymentRelationshipRegistry Employments = new EmploymentRelationshipRegistry();
            public FakeParcelAuthority Parcels = new FakeParcelAuthority();
            public FakePricePort Prices = new FakePricePort();
            public FakeMaterialSource Source = new FakeMaterialSource();
            public FakeToolCustody Custody = new FakeToolCustody();
            public PersonScheduleTracker Tracker = new PersonScheduleTracker();
            public TaskAuthority Tasks = new TaskAuthority();
            public WorkTimeBudgetStore Budgets = new WorkTimeBudgetStore();
            public HousingAuthority Housing = new HousingAuthority();
            public CreditCashBridge Cash = new CreditCashBridge();
            public FinancialObligationAuthority Obligations = new FinancialObligationAuthority();
            public EntityIdRegistry Ids = new EntityIdRegistry();
            public List<ConstructionProject> Projects = new List<ConstructionProject>();
            public readonly Dictionary<int, HouseholdLedger> Ledgers = new Dictionary<int, HouseholdLedger>();
            /// <summary>Hired candidates in try order (person ids).</summary>
            public List<int> HiredCandidates = new List<int> { 21 };
            /// <summary>Person id to the household their wages are paid to.</summary>
            public readonly Dictionary<int, int> HouseholdOfPerson = new Dictionary<int, int> { { 21, 9 } };
            /// <summary>When true the own member P101 is double-booked for the labour day.</summary>
            public bool BlockOwnMember;
            public int BlockDay = 12;

            public HiredChain()
            {
                Prices.Prices["fieldstone"] = 50;
                Prices.Prices["lumber"] = 40;
                Source.Stock["fieldstone"] = 1000;
                Source.Stock["lumber"] = 10000;
                Custody.Grant(7, "shovel");
                Custody.Grant(7, "saw");
                Parcels.HolderByParcel["parcel-9"] = "household:7";
            }

            public void FundHousehold(int householdId, int cashCents)
            {
                var ledger = new HouseholdLedger(householdId);
                Assert.IsNull(ledger.RecordInflow(0, cashCents, HouseholdIncomeSource.OwnerContribution,
                    "test-seed", "test seed capital", "test"));
                Assert.IsNull(Cash.Register("household:" + householdId,
                    new HouseholdCashStore(householdId, ledger)));
                Ledgers[householdId] = ledger;
                if (householdId == 7)
                {
                    var yard = new PurseCashStore("business:lumber-yard", 0, "test seed capital");
                    Assert.IsNull(Cash.Register("business:lumber-yard", yard));
                }
            }

            public NpcConstructionDriver NewDriver(bool bindSettlement = true)
            {
                var driver = new NpcConstructionDriver(Commissioner, Executor, Catalog, Projects,
                    Cash, Obligations, Ids, Housing, Prices, Source, "business:lumber-yard",
                    null, new ProductionMirrorLaborPort(this),
                    householdId => householdId == 7 ? new List<int> { 101 } : new List<int>());
                if (bindSettlement)
                {
                    driver.HiredLaborEmployments = Employments;
                    driver.WorkerHouseholdByPerson = personId =>
                        HouseholdOfPerson.TryGetValue(personId, out int h) ? h : -1;
                }
                return driver;
            }

            public static ConstructionMaterialRequirement Material(string name, int units, string unitLabel,
                ConstructionMaterialKind kind)
            {
                return new ConstructionMaterialRequirement
                {
                    RequirementId = "REQ-" + name,
                    MaterialKind = kind,
                    MaterialDisplayName = name,
                    RequiredUnits = units,
                    UnitLabel = unitLabel,
                    ProvenanceKind = ConstructionMaterialProvenanceKind.AnyRealSource,
                };
            }

            public void RegisterCabin()
            {
                var design = new BuildingDesign
                {
                    DesignId = "cabin-a",
                    DisplayName = "Settler cabin",
                    WorksKind = ConstructionWorksKind.Unspecified,
                    KindLabel = "cabin",
                };
                var foundation = new BuildingDesignPhase
                {
                    PhaseName = "Foundation", Sequence = 0, LaborMinutes = 240, Notes = "stone footings",
                };
                foundation.Materials.Add(Material("fieldstone", 40, "perch", ConstructionMaterialKind.Masonry));
                foundation.RequiredToolItemIds.Add("shovel");
                var framing = new BuildingDesignPhase
                {
                    PhaseName = "Framing", Sequence = 1, LaborMinutes = 480, Notes = "log walls and roof",
                };
                framing.Materials.Add(Material("lumber", 600, "board feet", ConstructionMaterialKind.Lumber));
                framing.RequiredToolItemIds.Add("saw");
                design.Phases.Add(foundation);
                design.Phases.Add(framing);
                design.SpaceSpecs.Add(new BuildingDesignSpaceSpec { Label = "main room", SleepingCapacity = 4 });
                Assert.IsNull(Catalog.RegisterDesign(design, Diag), string.Join("; ", Diag));
            }

            public NpcConstructionRequest CabinRequest()
            {
                return new NpcConstructionRequest
                {
                    HouseholdId = 7,
                    DesignId = "cabin-a",
                    ParcelId = "parcel-9",
                    MemberPersonIds = new List<int> { 101 },
                    Arrangement = AccommodationArrangement.OwnerOccupied,
                    Reason = NpcConstructionReason.Overcrowding,
                    ReasonNotes = "test fixture: household of 1 in a cabin sleeping 4",
                };
            }

            public ConstructionProject Commission(NpcConstructionDriver driver, int day)
            {
                Budgets.EnsureDay(day);
                ConstructionProject project = driver.Commission(CabinRequest(), Parcels, day, Diag);
                Assert.IsNotNull(project, string.Join("; ", Diag));
                return project;
            }

            /// <summary>Drives one day, blocking P101 first when the fixture says so.</summary>
            public void DriveDay(NpcConstructionDriver driver, int day)
            {
                Budgets.EnsureDay(day);
                if (BlockOwnMember && day == BlockDay)
                {
                    Assert.IsNull(Tracker.TryReserve(101, PersonActivityKind.Building,
                        day, 0, 1440, "test-block-own"), "the own-member block must reserve.");
                }
                driver.Drive(day, Diag);
            }
        }

        /// <summary>
        /// Mirrors the production labour port (SimulationDrivers
        /// DriversConstructionLaborPort): own members first, then hired
        /// candidates through the real NpcConstructionHiredLabor helper.
        /// </summary>
        private sealed class ProductionMirrorLaborPort : INpcConstructionLaborPort
        {
            private readonly HiredChain chain;
            public ProductionMirrorLaborPort(HiredChain chain) { this.chain = chain; }
            public string WorkPackage(ConstructionProject project, WorkPackage package, int dayIndex, List<string> diag)
            {
                NpcConstructionRecord record = chain.Commissioner.FindRecordForProject(project.ProjectId);
                string lastRefusal = "no active household member could work the package.";
                foreach (int personId in new List<int> { 101 })
                {
                    string commit = chain.Commissioner.CommitOwnLabor(chain.Executor, project, record, package,
                        personId, chain.Tracker, chain.Custody, chain.Tasks, chain.Budgets,
                        dayIndex, 360, package.LaborMinutesRequired, diag);
                    if (commit != null) { lastRefusal = commit; continue; }
                    string taskId = null;
                    for (int i = project.LaborTaskBindings.Count - 1; i >= 0; i--)
                    {
                        LaborTaskBinding binding = project.LaborTaskBindings[i];
                        if (binding != null && string.Equals(binding.PackageId, package.PackageId, StringComparison.Ordinal))
                        {
                            taskId = binding.TaskId;
                            break;
                        }
                    }
                    if (string.IsNullOrWhiteSpace(taskId)) return "no labor task bound to the package.";
                    return chain.Executor.WorkCommittedLabor(project, package, taskId,
                        package.LaborMinutesRequired, chain.Tasks, chain.Budgets, dayIndex, diag);
                }
                string hiredProblem = NpcConstructionHiredLabor.TryWorkPackage(
                    chain.Commissioner, chain.Employments, chain.Executor, project, record, package,
                    chain.HiredCandidates,
                    personId => chain.HouseholdOfPerson.TryGetValue(personId, out int h) ? h : -1,
                    NpcConstructionHiredLabor.HiredConstructionWeeklyWageCents,
                    chain.Tracker, chain.Custody, chain.Tasks, chain.Budgets,
                    dayIndex, 360, package.LaborMinutesRequired, diag);
                if (hiredProblem == null) return null;
                return lastRefusal + " No hired worker could work the package either — " + hiredProblem;
            }
        }

        private static bool DiagMentions(IReadOnlyList<string> diag, string fragment)
        {
            foreach (string line in diag)
                if (line != null && line.IndexOf(fragment, StringComparison.OrdinalIgnoreCase) >= 0) return true;
            return false;
        }

        [Test]
        public void HiredLabor_FallbackHiresWhenOwnLaborRefused()
        {
            var c = new HiredChain();
            c.RegisterCabin();
            c.FundHousehold(7, 100000);
            c.FundHousehold(9, 1000);
            c.BlockOwnMember = true;
            var driver = c.NewDriver();
            ConstructionProject project = c.Commission(driver, 10);
            c.DriveDay(driver, 11); // stage foundation
            Assert.AreEqual(WorkPackageStatus.MaterialsStaged, project.WorkPackages[0].Status);

            c.DriveDay(driver, 12); // own P101 double-booked; hired P21 works

            NpcConstructionRecord record = c.Commissioner.FindRecordForProject(project.ProjectId);
            NpcBuildPackageFunding funding = record.FindFunding(project.WorkPackages[0].PackageId);
            Assert.AreEqual(NpcBuildLaborKind.HiredWorker, funding.LaborKind);
            Assert.AreEqual(1, funding.EmploymentIds.Count);
            Assert.IsTrue(c.Employments.TryGetById(funding.EmploymentIds[0], out EmploymentRelationship rel));
            Assert.AreEqual(21, rel.EmployeePersonId);
            Assert.AreEqual(NpcConstructionHiredLabor.HiredConstructionWeeklyWageCents,
                rel.Compensation.AgreedWeeklyWageCents);
            Assert.AreEqual(240, funding.WorkerMinutesCommitted);
            Assert.AreEqual(240, project.WorkPackages[0].LaborMinutesCommitted);
            Assert.AreEqual(WorkPackageStatus.Complete, project.WorkPackages[0].Status);
        }

        [Test]
        public void HiredLabor_PaidOnCompletion_WagesMoveAndEmploymentEnds()
        {
            var c = new HiredChain();
            c.RegisterCabin();
            c.FundHousehold(7, 100000);
            c.FundHousehold(9, 1000);
            c.BlockOwnMember = true;
            var driver = c.NewDriver();
            ConstructionProject project = c.Commission(driver, 10);
            c.DriveDay(driver, 11); // stage foundation: 40 fieldstone x 50c = 2000c
            c.DriveDay(driver, 12); // hired P21 works the foundation to completion

            // 750c/week, 240 min of a 2400-min week = 75c.
            NpcConstructionRecord record = c.Commissioner.FindRecordForProject(project.ProjectId);
            NpcBuildPackageFunding funding = record.FindFunding(project.WorkPackages[0].PackageId);
            Assert.AreEqual(75, funding.WagesPaidCents);
            Assert.AreEqual(1000 + 75, c.Ledgers[9].GetBalanceCents());
            Assert.AreEqual(100000 - 2000 - 75, c.Ledgers[7].GetBalanceCents());
            Assert.AreEqual(2000 + 75, record.TotalActualCostCents,
                "the record accumulates actual costs spent, not the remaining balance.");
            Assert.IsTrue(c.Employments.TryGetById(funding.EmploymentIds[0], out EmploymentRelationship rel));
            Assert.IsTrue(!rel.IsActive, "the package employment ends when the wages are paid.");
            Assert.AreEqual(EmploymentLifecycleState.Ended, rel.LifecycleState);
        }

        [Test]
        public void HiredLabor_OwnLaborFirst_NoHireWhenOwnMemberWorks()
        {
            var c = new HiredChain();
            c.RegisterCabin();
            c.FundHousehold(7, 100000);
            c.FundHousehold(9, 1000);
            var driver = c.NewDriver();
            ConstructionProject project = c.Commission(driver, 10);
            c.DriveDay(driver, 11);
            c.DriveDay(driver, 12); // P101 free: own labour works

            NpcConstructionRecord record = c.Commissioner.FindRecordForProject(project.ProjectId);
            NpcBuildPackageFunding funding = record.FindFunding(project.WorkPackages[0].PackageId);
            Assert.AreEqual(NpcBuildLaborKind.OwnLabor, funding.LaborKind);
            Assert.AreEqual(0, funding.EmploymentIds.Count);
            Assert.AreEqual(0, funding.WagesPaidCents);
            Assert.AreEqual(WorkPackageStatus.Complete, project.WorkPackages[0].Status);
            Assert.AreEqual(1000, c.Ledgers[9].GetBalanceCents());
        }

        [Test]
        public void HiredLabor_NoCandidate_PackageWaitsLoudly()
        {
            var c = new HiredChain();
            c.RegisterCabin();
            c.FundHousehold(7, 100000);
            c.FundHousehold(9, 1000);
            c.BlockOwnMember = true;
            c.HiredCandidates = new List<int>();
            var driver = c.NewDriver();
            ConstructionProject project = c.Commission(driver, 10);
            c.DriveDay(driver, 11);
            c.DriveDay(driver, 12); // own refused, nobody to hire

            NpcConstructionRecord record = c.Commissioner.FindRecordForProject(project.ProjectId);
            NpcBuildPackageFunding funding = record.FindFunding(project.WorkPackages[0].PackageId);
            Assert.AreEqual(WorkPackageStatus.MaterialsStaged, project.WorkPackages[0].Status,
                "no worker, no phantom labour — the package waits.");
            Assert.AreEqual(0, project.WorkPackages[0].LaborMinutesCommitted);
            Assert.AreEqual(0, funding.EmploymentIds.Count);
            Assert.IsTrue(DiagMentions(c.Diag, "hired worker"),
                "the wait names the hired-labour failure, not just the own-labour refusal.");
        }

        [Test]
        public void HiredLabor_SameWorkerReusedAcrossDays_NoDuplicateEmployment()
        {
            var c = new HiredChain();
            c.RegisterCabin();
            c.FundHousehold(7, 100000);
            c.FundHousehold(9, 1000);
            var driver = c.NewDriver();
            ConstructionProject project = c.Commission(driver, 10);
            c.DriveDay(driver, 11); // stage foundation
            WorkPackage foundation = project.WorkPackages[0];
            NpcConstructionRecord record = c.Commissioner.FindRecordForProject(project.ProjectId);

            // Two half-days of hired work, calling the helper directly:
            // day 12 hires P21, day 13 must reuse that employment.
            c.Budgets.EnsureDay(12);
            Assert.IsNull(NpcConstructionHiredLabor.TryWorkPackage(
                c.Commissioner, c.Employments, c.Executor, project, record, foundation,
                new List<int> { 21 }, personId => 9,
                NpcConstructionHiredLabor.HiredConstructionWeeklyWageCents,
                c.Tracker, c.Custody, c.Tasks, c.Budgets, 12, 360, 120, c.Diag),
                string.Join("; ", c.Diag));
            Assert.AreEqual(120, foundation.LaborMinutesCommitted);
            c.Budgets.EnsureDay(13);
            Assert.IsNull(NpcConstructionHiredLabor.TryWorkPackage(
                c.Commissioner, c.Employments, c.Executor, project, record, foundation,
                new List<int> { 21 }, personId => 9,
                NpcConstructionHiredLabor.HiredConstructionWeeklyWageCents,
                c.Tracker, c.Custody, c.Tasks, c.Budgets, 13, 360, 120, c.Diag),
                string.Join("; ", c.Diag));
            Assert.AreEqual(240, foundation.LaborMinutesCommitted);

            NpcBuildPackageFunding funding = record.FindFunding(foundation.PackageId);
            Assert.AreEqual(1, funding.EmploymentIds.Count,
                "one worker, one package, one employment — no duplicate registration.");
            Assert.AreEqual(240, funding.WorkerMinutesCommitted);

            // The driver now advances the finished package and pays once.
            c.DriveDay(driver, 13);
            Assert.AreEqual(WorkPackageStatus.Complete, foundation.Status);
            Assert.AreEqual(75, funding.WagesPaidCents);
        }

        [Test]
        public void HiredLabor_UnpaidWhenNoRegistryBound_RecordedLoudly()
        {
            var c = new HiredChain();
            c.RegisterCabin();
            c.FundHousehold(7, 100000);
            c.FundHousehold(9, 1000);
            c.BlockOwnMember = true;
            var driver = c.NewDriver(bindSettlement: false);
            ConstructionProject project = c.Commission(driver, 10);
            c.DriveDay(driver, 11);
            c.DriveDay(driver, 12); // hired works, but nothing can settle the wages

            NpcConstructionRecord record = c.Commissioner.FindRecordForProject(project.ProjectId);
            NpcBuildPackageFunding funding = record.FindFunding(project.WorkPackages[0].PackageId);
            Assert.AreEqual(WorkPackageStatus.Complete, project.WorkPackages[0].Status,
                "the work is real — the package still completes.");
            Assert.AreEqual(0, funding.WagesPaidCents, "nothing is faked as paid.");
            Assert.IsTrue(c.Employments.TryGetById(funding.EmploymentIds[0], out EmploymentRelationship rel));
            Assert.IsTrue(rel.IsActive, "the employment stays active so a later settlement can find it.");
            Assert.AreEqual(1000, c.Ledgers[9].GetBalanceCents());
            Assert.IsTrue(DiagMentions(c.Diag, "cannot be settled"),
                "the unpaid wages are recorded loudly, not silently dropped.");
        }

        [Test]
        public void HiredLabor_NoDoublePayOnLaterDrives()
        {
            var c = new HiredChain();
            c.RegisterCabin();
            c.FundHousehold(7, 100000);
            c.FundHousehold(9, 1000);
            c.BlockOwnMember = true;
            var driver = c.NewDriver();
            ConstructionProject project = c.Commission(driver, 10);
            c.DriveDay(driver, 11);
            c.DriveDay(driver, 12); // foundation hired, completed, paid 75c
            c.DriveDay(driver, 13); // framing stages
            c.BlockOwnMember = false;
            c.DriveDay(driver, 14); // framing own labour, completes

            NpcConstructionRecord record = c.Commissioner.FindRecordForProject(project.ProjectId);
            NpcBuildPackageFunding foundationFunding = record.FindFunding(project.WorkPackages[0].PackageId);
            Assert.AreEqual(75, foundationFunding.WagesPaidCents);
            Assert.AreEqual(1000 + 75, c.Ledgers[9].GetBalanceCents(),
                "the foundation wages were paid once, never again.");
            Assert.AreEqual(WorkPackageStatus.Complete, project.WorkPackages[1].Status);
            Assert.AreEqual(NpcBuildLaborKind.OwnLabor,
                record.FindFunding(project.WorkPackages[1].PackageId).LaborKind);
        }

        [Test]
        public void HiredLabor_CandidateWithoutHouseholdSkipped()
        {
            var c = new HiredChain();
            c.RegisterCabin();
            c.FundHousehold(7, 100000);
            c.FundHousehold(9, 1000);
            c.BlockOwnMember = true;
            c.HiredCandidates = new List<int> { 99, 21 };
            var driver = c.NewDriver();
            ConstructionProject project = c.Commission(driver, 10);
            c.DriveDay(driver, 11);
            c.DriveDay(driver, 12); // P99 has no household to pay; P21 is hired instead

            NpcConstructionRecord record = c.Commissioner.FindRecordForProject(project.ProjectId);
            NpcBuildPackageFunding funding = record.FindFunding(project.WorkPackages[0].PackageId);
            Assert.AreEqual(1, funding.EmploymentIds.Count);
            Assert.IsTrue(c.Employments.TryGetById(funding.EmploymentIds[0], out EmploymentRelationship rel));
            Assert.AreEqual(21, rel.EmployeePersonId);
            Assert.AreEqual(75, funding.WagesPaidCents);
            Assert.AreEqual(WorkPackageStatus.Complete, project.WorkPackages[0].Status);
        }
    }
}
