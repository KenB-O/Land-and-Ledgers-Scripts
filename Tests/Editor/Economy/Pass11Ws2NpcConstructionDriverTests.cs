using System;
using System.Collections.Generic;
using LandLedgers.Economy;
using LandLedgers.Economy.Blacksmith;
using LandLedgers.Economy.Financing;
using LandLedgers.Economy.Logistics;
using LandLedgers.Population;
using LandLedgers.Primitives;
using LandLedgers.Tasks;
using LandLedgers.Time;
using LandLedgers.World.Property;
using NUnit.Framework;

namespace LandLedgers.EditorTests.Economy
{
    /// <summary>
    /// Pass XI WS2 (audit finding A-03, RUN-002): the NPC construction
    /// driver is the first production path for the construction
    /// commissioner and executor. Every test below drives through
    /// <see cref="NpcConstructionDriver"/> (commission, drive, cancel) —
    /// never by invoking CommissionProject or AdvancePackage directly —
    /// and proves cash, stock, labor and occupancy through the real
    /// authorities the driver wires together.
    /// </summary>
    [TestFixture]
    public sealed class Pass11Ws2NpcConstructionDriverTests
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

        private sealed class FakeRestock : IConstructionMaterialRestock
        {
            public readonly Dictionary<string, int> Returned = new Dictionary<string, int>();
            public string TryReturn(ConsumedMaterialLine line, string projectLabel, int dayIndex,
                List<string> diagnostics)
            {
                if (line == null || line.Units <= 0) return "nothing to return.";
                if (!Returned.ContainsKey(line.DisplayName)) Returned[line.DisplayName] = 0;
                Returned[line.DisplayName] += line.Units;
                return null;
            }
        }

        private sealed class FakeSalePort : IConstructionMaterialSalePort
        {
            public readonly Dictionary<string, int> Stock = new Dictionary<string, int>();
            public readonly List<ConstructionMaterialSale> Sales = new List<ConstructionMaterialSale>();
            private int sequence;
            public string TrySellToProject(string supplierBusinessId, string projectId, string packageId,
                List<ConstructionMaterialRequirement> requirements, IConstructionMaterialPricePort pricePort,
                int dayIndex, List<string> diagnostics, out ConstructionMaterialSale sale)
            {
                sale = null;
                if (requirements == null) return "no requirements.";
                foreach (ConstructionMaterialRequirement r in requirements)
                {
                    if (r == null || !r.IsCoherent) return "incoherent requirement.";
                    int have = Stock.TryGetValue(r.MaterialDisplayName, out int u) ? u : 0;
                    if (have < r.RequiredUnits)
                        return $"FakeSalePort: only {have} of '{r.MaterialDisplayName}' in stock — nothing sold.";
                }
                var s = new ConstructionMaterialSale
                {
                    SaleId = $"sale-{sequence++}",
                    SupplierBusinessId = supplierBusinessId,
                    ProjectId = projectId,
                    PackageId = packageId,
                    DayIndex = dayIndex,
                };
                foreach (ConstructionMaterialRequirement r in requirements)
                {
                    int unitPrice = pricePort != null ? pricePort.UnitPriceCents(r) : -1;
                    Stock[r.MaterialDisplayName] -= r.RequiredUnits;
                    s.Lines.Add(new ConstructionMaterialSaleLine
                    {
                        DisplayName = r.MaterialDisplayName,
                        Units = r.RequiredUnits,
                        UnitLabel = r.UnitLabel,
                        UnitPriceCents = unitPrice,
                        SupplierBusinessId = supplierBusinessId,
                        Provenance = $"yard lot '{r.MaterialDisplayName}' x{r.RequiredUnits} (sale {s.SaleId}, day {dayIndex})",
                    });
                }
                Sales.Add(s);
                sale = s;
                return null;
            }
        }

        private sealed class FakeGoodsSupplier : IGoodsSupplier
        {
            public string BusinessId;
            public readonly HashSet<string> Categories = new HashSet<string>();
            public string SupplierBusinessId => BusinessId;
            public string SupplierName => BusinessId;
            public string LocationId => "loc:" + BusinessId;
            public bool HasCategory(string categoryId) => Categories.Contains(categoryId);
            public int StockUnits(string categoryId) => 999999;
            public int PricePerUnitCents(string categoryId) => 1;
            public int Sell(string categoryId, int requestedUnits, int dayIndex, List<string> diagnostics) => 0;
        }

        private sealed class FakeToolCustody : IHouseholdToolCustody
        {
            private readonly HashSet<string> held = new HashSet<string>();
            public void Grant(int householdId, string toolId) => held.Add(householdId + ":" + toolId);
            public bool HouseholdHoldsTool(int householdId, string toolItemId) =>
                held.Contains(householdId + ":" + toolItemId);
        }

        private sealed class Chain
        {
            public List<string> Diag = new List<string>();
            public BuildingDesignCatalog Catalog = new BuildingDesignCatalog();
            public ConstructionProjectExecutor Executor = new ConstructionProjectExecutor();
            public NpcConstructionCommissioner Commissioner = new NpcConstructionCommissioner();
            public ConstructionProcurementService Procurement = new ConstructionProcurementService();
            public ConstructionMaterialFreightCoordinator FreightCoordinator = new ConstructionMaterialFreightCoordinator();
            public FakeParcelAuthority Parcels = new FakeParcelAuthority();
            public FakePricePort Prices = new FakePricePort();
            public FakeMaterialSource Source = new FakeMaterialSource();
            public FakeSalePort SalePort = new FakeSalePort();
            public FakeRestock Restock = new FakeRestock();
            public SupplierDirectory Directory = new SupplierDirectory();
            public PersonScheduleTracker Tracker = new PersonScheduleTracker();
            public FakeToolCustody Custody = new FakeToolCustody();
            public TaskAuthority Tasks = new TaskAuthority();
            public WorkTimeBudgetStore Budgets = new WorkTimeBudgetStore();
            public HousingAuthority Housing = new HousingAuthority();
            public CreditCashBridge Cash = new CreditCashBridge();
            public FinancialObligationAuthority Obligations = new FinancialObligationAuthority();
            public EntityIdRegistry Ids = new EntityIdRegistry();
            public List<ConstructionProject> Projects = new List<ConstructionProject>();
            public HouseholdLedger HouseholdLedger;
            public PurseCashStore YardPurse;

            public Chain()
            {
                Prices.Prices["fieldstone"] = 50;
                Prices.Prices["lumber"] = 40;
                Source.Stock["fieldstone"] = 1000;
                Source.Stock["lumber"] = 10000;
                SalePort.Stock["fieldstone"] = 1000;
                SalePort.Stock["lumber"] = 10000;
                Custody.Grant(7, "shovel");
                Custody.Grant(7, "saw");
                Parcels.HolderByParcel["parcel-9"] = "household:7";
                var yard = new FakeGoodsSupplier { BusinessId = "lumber-yard" };
                yard.Categories.Add("construction-lumber");
                yard.Categories.Add("construction-masonry");
                yard.Categories.Add("construction-materials");
                Directory.Register(yard);
            }

            public void FundHousehold(int householdId, int cashCents)
            {
                HouseholdLedger = new HouseholdLedger(householdId);
                Assert.IsNull(HouseholdLedger.RecordInflow(0, cashCents, HouseholdIncomeSource.OwnerContribution,
                    "test-seed", "test seed capital", "test"));
                Assert.IsNull(Cash.Register("household:" + householdId,
                    new HouseholdCashStore(householdId, HouseholdLedger)));
                YardPurse = new PurseCashStore("business:lumber-yard", 0, "test seed capital");
                Assert.IsNull(Cash.Register("business:lumber-yard", YardPurse));
            }

            public NpcConstructionDriver NewDriver(INpcConstructionProcurementPort procurement = null)
            {
                return new NpcConstructionDriver(Commissioner, Executor, Catalog, Projects,
                    Cash, Obligations, Ids, Housing, Prices, Source, "business:lumber-yard",
                    procurement, new OwnLaborPort(this),
                    householdId => householdId == 7 ? new List<int> { 101 } : new List<int>());
            }

            public ConstructionFreightPlan FreightPlan(float wagonMassKg = 100000f)
            {
                return new ConstructionFreightPlan
                {
                    Freight = new FreightShipmentService(),
                    Wagon = new EquipmentAsset
                    {
                        AssetId = "wagon-1", Kind = "wagon", DisplayName = "farm wagon",
                        PayloadMassCapacityKg = wagonMassKg, CargoVolumeCapacityM3 = 100f, Condition01 = 1f,
                    },
                    DriverId = EntityId.For(EntityKind.Person, 41),
                    LoaderId = EntityId.For(EntityKind.Person, 42),
                    UnloaderId = EntityId.For(EntityKind.Person, 43),
                    Tasks = new TaskAuthority(),
                    Budgets = new WorkTimeBudgetStore(),
                    Tracker = new PersonScheduleTracker(),
                    SiteLabel = "site",
                    FreightChargeCents = 0,
                    CargoSpec = new FreightCargoSpec { MassKgPerUnit = 1f, VolumeM3PerUnit = 0.01f },
                };
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
                };
            }

            public ConstructionProject DriveToComplete(NpcConstructionDriver driver, int startDay)
            {
                ConstructionProject project = driver.Commission(CabinRequest(), Parcels, startDay, Diag);
                Assert.IsNotNull(project, string.Join("; ", Diag));
                int day = startDay;
                int guard = 0;
                while (project.Status != ConstructionProjectStatus.Complete && guard++ < 30)
                {
                    Budgets.EnsureDay(day);
                    driver.Drive(day, Diag);
                    day++;
                }
                Assert.AreEqual(ConstructionProjectStatus.Complete, project.Status, string.Join("; ", Diag));
                return project;
            }
        }

        private sealed class OwnLaborPort : INpcConstructionLaborPort
        {
            private readonly Chain chain;
            public OwnLaborPort(Chain chain) { this.chain = chain; }
            public string WorkPackage(ConstructionProject project, WorkPackage package, int dayIndex, List<string> diag)
            {
                NpcConstructionRecord record = chain.Commissioner.FindRecordForProject(project.ProjectId);
                string commit = chain.Commissioner.CommitOwnLabor(chain.Executor, project, record, package,
                    101, chain.Tracker, chain.Custody, chain.Tasks, chain.Budgets,
                    dayIndex, 360, package.LaborMinutesRequired, diag);
                if (commit != null) return commit;
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
        }

        private sealed class ProcurementPort : INpcConstructionProcurementPort
        {
            private readonly Chain chain;
            public ConstructionSiteStockpile LastStockpile;
            public float WagonMassKg = 100000f;
            public ProcurementPort(Chain chain) { this.chain = chain; }
            public string ProcureAndStage(ConstructionProject project, WorkPackage package,
                NpcConstructionRecord record, int dayIndex, List<string> diag)
            {
                LastStockpile = new ConstructionSiteStockpile { ProjectId = project.ProjectId };
                return chain.Commissioner.ProcureAndStagePackage(chain.Executor, project, record, package,
                    chain.Procurement, chain.SalePort, chain.Prices, chain.Directory,
                    chain.FreightCoordinator, chain.FreightPlan(WagonMassKg), LastStockpile,
                    chain.Cash, "household:" + project.HouseholdId, chain.Obligations, chain.Ids,
                    dayIndex, diag);
            }
        }

        [Test]
        public void Driver_Commission_ParcelUnauthorized_Refused_NoProject()
        {
            var c = new Chain();
            c.RegisterCabin();
            c.FundHousehold(7, 100000);
            var driver = c.NewDriver();
            var request = c.CabinRequest();
            request.ParcelId = "parcel-other";
            Assert.IsNull(driver.Commission(request, c.Parcels, 10, c.Diag), string.Join("; ", c.Diag));
            Assert.AreEqual(0, c.Projects.Count);
        }

        [Test]
        public void Driver_Commission_UnfundedDesign_Refused_NoProject()
        {
            var c = new Chain();
            c.RegisterCabin();
            c.FundHousehold(7, 100);
            var driver = c.NewDriver();
            Assert.IsNull(driver.Commission(c.CabinRequest(), c.Parcels, 10, c.Diag), string.Join("; ", c.Diag));
            Assert.AreEqual(0, c.Projects.Count);
        }

        [Test]
        public void Driver_FullChainThroughDriver_CompletesAndOccupies_Conserved()
        {
            var c = new Chain();
            c.RegisterCabin();
            c.FundHousehold(7, 100000);
            var driver = c.NewDriver();
            ConstructionProject project = c.DriveToComplete(driver, 10);

            Assert.IsFalse(string.IsNullOrWhiteSpace(project.BuildingId));
            Assert.AreEqual(2, project.WorkPackages.Count);
            foreach (WorkPackage package in project.WorkPackages)
                Assert.AreEqual(WorkPackageStatus.Complete, package.Status);
            // Materials 40 x 50 + 600 x 40 = 26000c, paid exactly once.
            Assert.AreEqual(100000 - 26000, c.HouseholdLedger.GetBalanceCents());
            Assert.AreEqual(26000, c.YardPurse.ReadBalanceCents());
            Assert.AreEqual(960, c.Source.Stock["fieldstone"]);
            Assert.AreEqual(9400, c.Source.Stock["lumber"]);
            // Occupancy recorded in the new building, exactly once per member.
            var spaces = new HashSet<string>(StringComparer.Ordinal);
            foreach (AccommodationSpace space in c.Housing.SpacesForBuilding(project.BuildingId))
                spaces.Add(space.SpaceId);
            int occupancies = 0;
            foreach (ResidentialOccupancy occ in c.Housing.CurrentOccupanciesForPerson(101))
                if (spaces.Contains(occ.SpaceId)) occupancies++;
            Assert.AreEqual(1, occupancies);

            // Extra drives change nothing: no double staging, no double occupancy, no double payment.
            for (int day = 40; day < 43; day++) driver.Drive(day, c.Diag);
            Assert.AreEqual(100000 - 26000, c.HouseholdLedger.GetBalanceCents());
            Assert.AreEqual(960, c.Source.Stock["fieldstone"]);
            occupancies = 0;
            foreach (ResidentialOccupancy occ in c.Housing.CurrentOccupanciesForPerson(101))
                if (spaces.Contains(occ.SpaceId)) occupancies++;
            Assert.AreEqual(1, occupancies);
        }

        [Test]
        public void Driver_ProcurementMode_DiscoversSupplier_PaysAndStages_FreightCleared()
        {
            var c = new Chain();
            c.RegisterCabin();
            c.FundHousehold(7, 100000);
            var port = new ProcurementPort(c);
            var driver = c.NewDriver(port);
            ConstructionProject project = c.DriveToComplete(driver, 10);

            Assert.AreEqual(ConstructionProjectStatus.Complete, project.Status, string.Join("; ", c.Diag));
            Assert.AreEqual(2, c.SalePort.Sales.Count, "one real sale per package through the discovered supplier");
            Assert.AreEqual("lumber-yard", c.SalePort.Sales[0].SupplierBusinessId);
            Assert.AreEqual(100000 - 26000, c.HouseholdLedger.GetBalanceCents());
            Assert.AreEqual(26000, c.YardPurse.ReadBalanceCents());
            Assert.AreEqual(960, c.SalePort.Stock["fieldstone"]);
            Assert.AreEqual(9400, c.SalePort.Stock["lumber"]);
            Assert.IsNotNull(port.LastStockpile);
            Assert.AreEqual(0, port.LastStockpile.AvailableUnits("lumber"),
                "staging consumed the site stockpile — nothing left behind, nothing duplicated");
        }

        [Test]
        public void Driver_ProcurementFailure_BlocksBlindRetry_UntilExplicitRetry()
        {
            var c = new Chain();
            c.RegisterCabin();
            c.FundHousehold(7, 100000);
            var port = new ProcurementPort(c) { WagonMassKg = 10f };
            var driver = c.NewDriver(port);
            ConstructionProject project = driver.Commission(c.CabinRequest(), c.Parcels, 10, c.Diag);
            Assert.IsNotNull(project, string.Join("; ", c.Diag));

            // The wagon cannot carry the load: the sale stands at the
            // supplier, but nothing reaches the site and the package waits.
            Assert.AreEqual(0, driver.Drive(11, c.Diag));
            Assert.AreEqual(WorkPackageStatus.Ready, project.WorkPackages[0].Status);
            Assert.AreEqual(1, c.SalePort.Sales.Count, "the failed leg bought once");
            Assert.AreEqual(100000 - 2000, c.HouseholdLedger.GetBalanceCents());

            // The next drive must NOT buy the materials a second time.
            Assert.AreEqual(0, driver.Drive(12, c.Diag));
            Assert.AreEqual(1, c.SalePort.Sales.Count, "a blind retry would double-buy the stranded lot");

            // An explicit retry decision with a wagon that can carry the load
            // completes the package; the first lot stays supplier-held (the
            // service semantic), it is neither lost nor double-staged.
            port.WagonMassKg = 100000f;
            driver.AllowProcurementRetry(project, project.WorkPackages[0]);
            Assert.AreEqual(1, driver.Drive(13, c.Diag));
            Assert.AreEqual(WorkPackageStatus.MaterialsStaged, project.WorkPackages[0].Status);
            Assert.AreEqual(2, c.SalePort.Sales.Count);
        }

        [Test]
        public void Driver_InsufficientStock_Waits_NoProgressNoPayment_ThenAdvances()
        {
            var c = new Chain();
            c.RegisterCabin();
            c.FundHousehold(7, 100000);
            c.Source.Stock["fieldstone"] = 0;
            var driver = c.NewDriver();
            ConstructionProject project = driver.Commission(c.CabinRequest(), c.Parcels, 10, c.Diag);
            Assert.IsNotNull(project, string.Join("; ", c.Diag));
            Assert.AreEqual(0, driver.Drive(11, c.Diag));
            Assert.AreEqual(WorkPackageStatus.Ready, project.WorkPackages[0].Status);
            Assert.AreEqual(100000, c.HouseholdLedger.GetBalanceCents());

            c.Source.Stock["fieldstone"] = 1000;
            Assert.AreEqual(1, driver.Drive(12, c.Diag));
            Assert.AreEqual(WorkPackageStatus.MaterialsStaged, project.WorkPackages[0].Status);
            Assert.AreEqual(100000 - 2000, c.HouseholdLedger.GetBalanceCents());
        }

        [Test]
        public void Driver_SaveMidPackage_ResumeWithoutDoubleFunding()
        {
            var c = new Chain();
            c.RegisterCabin();
            c.FundHousehold(7, 100000);
            var driver = c.NewDriver();
            ConstructionProject project = driver.Commission(c.CabinRequest(), c.Parcels, 10, c.Diag);
            Assert.IsNotNull(project, string.Join("; ", c.Diag));
            Assert.AreEqual(1, driver.Drive(11, c.Diag));
            Assert.AreEqual(WorkPackageStatus.MaterialsStaged, project.WorkPackages[0].Status);
            Assert.AreEqual(1, project.WorkPackages[0].StagedMaterialLines.Count);

            // Save and load into fresh authorities, as the hub does.
            ConstructionProjectExecutor.ConstructionProjectSaveDto projectDto =
                c.Executor.CaptureSaveDto(new List<ConstructionProject>(c.Projects));
            NpcConstructionCommissioner.NpcConstructionCommissionerSaveDto recordDto =
                c.Commissioner.CaptureSaveDto();
            var executor2 = new ConstructionProjectExecutor();
            var commissioner2 = new NpcConstructionCommissioner();
            List<ConstructionProject> projects2 = executor2.LoadFromSaveDto(projectDto, c.Diag);
            commissioner2.LoadFromSaveDto(recordDto, c.Diag);
            Assert.AreEqual(1, projects2.Count);

            var c2 = new Chain();
            c2.Executor = executor2;
            c2.Commissioner = commissioner2;
            c2.Projects = projects2;
            c2.Cash = c.Cash;
            c2.Obligations = c.Obligations;
            c2.Ids = c.Ids;
            c2.Source = c.Source;
            c2.Prices = c.Prices;
            c2.Catalog = c.Catalog;
            var driver2 = c2.NewDriver();
            ConstructionProject resumed = projects2[0];
            int day = 12;
            int guard = 0;
            while (resumed.Status != ConstructionProjectStatus.Complete && guard++ < 30)
            {
                c2.Budgets.EnsureDay(day);
                driver2.Drive(day, c2.Diag);
                day++;
            }
            Assert.AreEqual(ConstructionProjectStatus.Complete, resumed.Status, string.Join("; ", c2.Diag));
            Assert.AreEqual(1, resumed.WorkPackages[0].StagedMaterialLines.Count,
                "the staged package was not staged twice after load");
            Assert.AreEqual(100000 - 26000, c.HouseholdLedger.GetBalanceCents(),
                "each package paid exactly once across the save boundary");
            Assert.AreEqual(960, c.Source.Stock["fieldstone"]);
        }

        [Test]
        public void Driver_CancelThroughDriver_ReturnsStagedMaterialsExactlyOnce()
        {
            var c = new Chain();
            c.RegisterCabin();
            c.FundHousehold(7, 100000);
            var driver = c.NewDriver();
            ConstructionProject project = driver.Commission(c.CabinRequest(), c.Parcels, 10, c.Diag);
            Assert.IsNotNull(project, string.Join("; ", c.Diag));
            Assert.AreEqual(1, driver.Drive(11, c.Diag));
            Assert.AreEqual(960, c.Source.Stock["fieldstone"]);

            Assert.IsNull(driver.Cancel(project, c.Restock, c.Tracker, c.Tasks, c.Budgets, 12, c.Diag),
                string.Join("; ", c.Diag));
            Assert.AreEqual(ConstructionProjectStatus.Cancelled, project.Status);
            Assert.AreEqual(40, c.Restock.Returned["fieldstone"]);
            Assert.IsTrue(project.WorkPackages[0].StagedMaterialsReturned);

            string second = driver.Cancel(project, c.Restock, c.Tracker, c.Tasks, c.Budgets, 13, c.Diag);
            Assert.IsNotNull(second, "a second cancel must be refused — no double return");
            Assert.AreEqual(40, c.Restock.Returned["fieldstone"]);
        }

        [Test]
        public void Driver_PausedProject_Skipped_NotAutoResumed()
        {
            var c = new Chain();
            c.RegisterCabin();
            c.FundHousehold(7, 100000);
            var driver = c.NewDriver();
            ConstructionProject project = driver.Commission(c.CabinRequest(), c.Parcels, 10, c.Diag);
            Assert.IsNotNull(project, string.Join("; ", c.Diag));
            project.Status = ConstructionProjectStatus.Paused;
            Assert.AreEqual(0, driver.Drive(11, c.Diag));
            Assert.AreEqual(ConstructionProjectStatus.Paused, project.Status);

            NpcConstructionRecord record = c.Commissioner.FindRecordForProject(project.ProjectId);
            Assert.IsNull(c.Commissioner.ResumeProject(project, record, 12, c.Diag), string.Join("; ", c.Diag));
            Assert.AreEqual(1, driver.Drive(13, c.Diag));
        }
    }
}
