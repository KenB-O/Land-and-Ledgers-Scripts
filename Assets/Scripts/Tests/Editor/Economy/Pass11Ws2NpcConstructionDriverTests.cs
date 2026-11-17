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

        private sealed class FakeOwnedMaterialSource : IAlreadyOwnedConstructionMaterialSource
        {
            public readonly Dictionary<string, int> Stock = new Dictionary<string, int>();
            public int AvailableUnits(ConstructionMaterialRequirement requirement) => requirement != null
                && Stock.TryGetValue(requirement.MaterialDisplayName, out int units) ? units : 0;
            public string TryConsume(ConstructionMaterialRequirement requirement, int units,
                string projectLabel, int dayIndex, List<string> diagnostics, out string provenanceLabel)
            {
                provenanceLabel = string.Empty;
                int available = AvailableUnits(requirement);
                if (available < units) return $"Only {available} owned '{requirement.MaterialDisplayName}' units are available.";
                Stock[requirement.MaterialDisplayName] = available - units;
                provenanceLabel = $"previously owned household lot: {requirement.MaterialDisplayName} x{units}";
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

            // G1: voids a recorded sale in full — every unit returns to
            // stock, the record stays for audit.
            public readonly HashSet<string> VoidedSaleIds = new HashSet<string>();
            public string TryVoidSale(ConstructionMaterialSale sale, int dayIndex, List<string> diagnostics, out int voidedCents)
            {
                voidedCents = 0;
                if (sale == null || string.IsNullOrWhiteSpace(sale.SaleId)) return "no sale to void.";
                if (!Sales.Contains(sale)) return $"FakeSalePort: unknown sale '{sale.SaleId}' — nothing to void.";
                if (VoidedSaleIds.Contains(sale.SaleId)) return $"FakeSalePort: sale '{sale.SaleId}' is already voided.";
                foreach (ConstructionMaterialSaleLine line in sale.Lines)
                {
                    if (line == null) continue;
                    if (!Stock.ContainsKey(line.DisplayName)) Stock[line.DisplayName] = 0;
                    Stock[line.DisplayName] += Math.Max(0, line.Units);
                    voidedCents += line.LineTotalCents;
                }
                VoidedSaleIds.Add(sale.SaleId);
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
                    Reason = NpcConstructionReason.Overcrowding,
                    ReasonNotes = "test fixture: household of 1 in a cabin sleeping 4",
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
        public void Driver_HouseholdOwnedMaterials_ConsumesAtStage_WithoutSecondSupplierPayment()
        {
            var c = new Chain();
            c.RegisterCabin();
            c.FundHousehold(7, 100000);
            var owned = new FakeOwnedMaterialSource();
            owned.Stock["fieldstone"] = 40;
            owned.Stock["lumber"] = 600;
            NpcConstructionDriver driver = c.NewDriver();
            driver.MaterialSourceByHousehold = _ => owned;

            ConstructionProject project = c.DriveToComplete(driver, 10);
            Assert.AreEqual(ConstructionProjectStatus.Complete, project.Status);
            Assert.AreEqual(0, owned.Stock["fieldstone"]);
            Assert.AreEqual(0, owned.Stock["lumber"]);
            Assert.AreEqual(100000, c.HouseholdLedger.GetBalanceCents(),
                "already-owned stock must not be charged again to the household");
            Assert.AreEqual(0, c.YardPurse.ReadBalanceCents(),
                "no new supplier sale occurred for household-owned lots");
            NpcConstructionRecord record = c.Commissioner.FindRecordForProject(project.ProjectId);
            Assert.IsTrue(record.Decisions.Exists(x => x.Contains("household-owned lots")));
            Assert.IsTrue(record.FindFunding(project.WorkPackages[0].PackageId).Notes.Contains("cost basis is not exposed"));
        }

        [Test]
        public void TitleConstructionParcelAuthority_RequiresCurrentHouseholdTitle()
        {
            var titles = new TitleAuthority();
            var diag = new List<string>();
            Assert.IsNotNull(titles.RegisterParcel("parcel-c", "construction parcel", 1f,
                "household:7", TitleBasis.Purchase, 0, diag));
            var authority = new TitleConstructionParcelAuthority(titles);
            Assert.IsNull(authority.AuthorizeBuild(7, "parcel-c", 1, diag));
            StringAssert.Contains("held by", authority.AuthorizeBuild(8, "parcel-c", 1, diag));
            StringAssert.Contains("not registered", authority.AuthorizeBuild(7, "unknown", 1, diag));
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

            // The wagon cannot carry the load: the failed leg unwinds
            // the sale (units back to the supplier, buyer refunded in
            // full), nothing reaches the site, and the package waits.
            Assert.AreEqual(0, driver.Drive(11, c.Diag));
            Assert.AreEqual(WorkPackageStatus.Ready, project.WorkPackages[0].Status);
            Assert.AreEqual(1, c.SalePort.Sales.Count, "the failed leg bought once");
            Assert.IsTrue(c.SalePort.VoidedSaleIds.Contains(c.SalePort.Sales[0].SaleId),
                "the failed sale was voided, not left standing");
            Assert.AreEqual(100000, c.HouseholdLedger.GetBalanceCents());
            Assert.AreEqual(0, c.YardPurse.ReadBalanceCents());

            // The next drive must NOT buy the materials a second time.
            Assert.AreEqual(0, driver.Drive(12, c.Diag));
            Assert.AreEqual(1, c.SalePort.Sales.Count, "a blind retry would double-buy the stranded lot");

            // An explicit retry decision with a wagon that can carry the load
            // completes the package; the first sale stays voided (units
            // returned, cash refunded) and the retry buys fresh — the lot
            // is neither lost nor double-staged.
            port.WagonMassKg = 100000f;
            driver.AllowProcurementRetry(project, project.WorkPackages[0]);
            Assert.AreEqual(1, driver.Drive(13, c.Diag));
            Assert.AreEqual(WorkPackageStatus.MaterialsStaged, project.WorkPackages[0].Status);
            Assert.AreEqual(2, c.SalePort.Sales.Count);
            Assert.AreEqual(1, c.SalePort.VoidedSaleIds.Count);
            Assert.IsFalse(c.SalePort.VoidedSaleIds.Contains(c.SalePort.Sales[1].SaleId));
            Assert.AreEqual(100000 - 2000, c.HouseholdLedger.GetBalanceCents());
            Assert.AreEqual(2000, c.YardPurse.ReadBalanceCents());
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

        // DEF-071 (Decision 14, D3/T12): cancelling through the driver without
        // an employment registry records loudly that wages could not be
        // settled — and still returns the staged materials. The wages are
        // not silently dropped; the commissioner can settle them when a
        // registry is available.
        [Test]
        public void Driver_CancelWithoutRegistry_RecordsUnsettledWages_AndStillReturnsMaterials()
        {
            var c = new Chain();
            c.RegisterCabin();
            c.FundHousehold(7, 100000);
            var driver = c.NewDriver();
            ConstructionProject project = driver.Commission(c.CabinRequest(), c.Parcels, 10, c.Diag);
            Assert.IsNotNull(project, string.Join("; ", c.Diag));
            Assert.AreEqual(1, driver.Drive(11, c.Diag));

            int diagBefore = c.Diag.Count;
            Assert.IsNull(driver.Cancel(project, c.Restock, c.Tracker, c.Tasks, c.Budgets, 12, c.Diag),
                string.Join("; ", c.Diag));
            Assert.AreEqual(ConstructionProjectStatus.Cancelled, project.Status);
            Assert.AreEqual(40, c.Restock.Returned["fieldstone"]);
            Assert.IsTrue(project.WorkPackages[0].StagedMaterialsReturned);
            bool recorded = false;
            for (int i = diagBefore; i < c.Diag.Count; i++)
                if (c.Diag[i].Contains("could not be settled")) recorded = true;
            Assert.IsTrue(recorded, "the cancel must record loudly that wages could not be settled: " + string.Join("; ", c.Diag));
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

        // ---------------- Plan 26 (Q30a/Q30b) ----------------

        // Plan 26 (Q30a): captures the commissioner DTO, restores it into a
        // fresh commissioner (loads times), then commissions once more through
        // a fresh driver and returns the minted record id. The executor
        // sequence is burned past the restored project ids so the new
        // commission mints a fresh project id and the restored records stay
        // addressable by project id. The burn count is derived from the
        // DTO's own project ids: each commission advances the executor
        // sequence by 3 (one project id plus one per work package — the
        // cabin has two phases), so a fixed burn of 3 would collide with
        // the restored cproj-3 record.
        private static int ProjectSuffix(string projectId)
        {
            if (string.IsNullOrEmpty(projectId)) return -1;
            int dash = projectId.LastIndexOf('-');
            if (dash < 0 || dash == projectId.Length - 1) return -1;
            int value;
            return int.TryParse(projectId.Substring(dash + 1), out value) ? value : -1;
        }

        private static string MintRecordIdAfterRestore(
            NpcConstructionCommissioner.NpcConstructionCommissionerSaveDto dto, int loads,
            out NpcConstructionCommissioner restoredCommissioner)
        {
            restoredCommissioner = new NpcConstructionCommissioner();
            var loadDiag = new List<string>();
            for (int i = 0; i < loads; i++) restoredCommissioner.LoadFromSaveDto(dto, loadDiag);
            var c = new Chain();
            c.RegisterCabin();
            c.FundHousehold(7, 100000);
            c.Commissioner = restoredCommissioner;
            int maxProjectSuffix = -1;
            foreach (NpcConstructionRecord r in dto.Records)
                if (r != null) maxProjectSuffix = Math.Max(maxProjectSuffix, ProjectSuffix(r.ProjectId));
            for (int i = 0; i <= maxProjectSuffix; i++)
                c.Executor.CreateProject(c.Catalog, 7, "cabin-a", "parcel-9", 10, c.Diag);
            var driver = c.NewDriver();
            ConstructionProject project = driver.Commission(c.CabinRequest(), c.Parcels, 10, c.Diag);
            Assert.IsNotNull(project, string.Join("; ", c.Diag));
            NpcConstructionRecord record = restoredCommissioner.FindRecordForProject(project.ProjectId);
            Assert.IsNotNull(record, "the new commission must open a commissioner record");
            return record.RecordId;
        }

        [Test]
        public void CommissionerSequence_RestoresAfterLoad_NextMintDoesNotOverwrite()
        {
            var c = new Chain();
            c.RegisterCabin();
            c.FundHousehold(7, 100000);
            var driver = c.NewDriver();
            Assert.IsNotNull(driver.Commission(c.CabinRequest(), c.Parcels, 10, c.Diag), string.Join("; ", c.Diag));
            Assert.IsNotNull(driver.Commission(c.CabinRequest(), c.Parcels, 10, c.Diag), string.Join("; ", c.Diag));
            Assert.IsNotNull(driver.Commission(c.CabinRequest(), c.Parcels, 10, c.Diag), string.Join("; ", c.Diag));

            NpcConstructionCommissioner.NpcConstructionCommissionerSaveDto dto =
                c.Commissioner.CaptureSaveDto();
            NpcConstructionCommissioner restored;
            string minted = MintRecordIdAfterRestore(dto, 1, out restored);
            Assert.AreEqual("npcb-3", minted, "the sequence survives the round trip");
            NpcConstructionRecord first = restored.FindRecord("npcb-0");
            Assert.IsNotNull(first, "the restored npcb-0 record must survive the new commission");
            Assert.AreEqual("cproj-0", first.ProjectId,
                "the restored npcb-0 record was not overwritten by the new commission");
        }

        [Test]
        public void CommissionerSequence_DoubleLoad_NoDuplicates_SequenceStable()
        {
            var c = new Chain();
            c.RegisterCabin();
            c.FundHousehold(7, 100000);
            var driver = c.NewDriver();
            Assert.IsNotNull(driver.Commission(c.CabinRequest(), c.Parcels, 10, c.Diag), string.Join("; ", c.Diag));
            Assert.IsNotNull(driver.Commission(c.CabinRequest(), c.Parcels, 10, c.Diag), string.Join("; ", c.Diag));
            Assert.IsNotNull(driver.Commission(c.CabinRequest(), c.Parcels, 10, c.Diag), string.Join("; ", c.Diag));

            NpcConstructionCommissioner.NpcConstructionCommissionerSaveDto dto =
                c.Commissioner.CaptureSaveDto();

            // A second load of the same DTO adds no duplicate records.
            var twice = new NpcConstructionCommissioner();
            twice.LoadFromSaveDto(dto, c.Diag);
            twice.LoadFromSaveDto(dto, c.Diag);
            Assert.IsNotNull(twice.FindRecord("npcb-0"));
            Assert.IsNotNull(twice.FindRecord("npcb-1"));
            Assert.IsNotNull(twice.FindRecord("npcb-2"));

            // The next mint is the same after one load as after two.
            NpcConstructionCommissioner once;
            string mintedOnce = MintRecordIdAfterRestore(dto, 1, out once);
            NpcConstructionCommissioner twiceMore;
            string mintedTwice = MintRecordIdAfterRestore(dto, 2, out twiceMore);
            Assert.AreEqual(mintedOnce, mintedTwice, "the sequence is stable across a double load");
        }

        [Test]
        public void Driver_NoStrandedPackages_RestoresWithNoPhantomBlock()
        {
            var c = new Chain();
            c.RegisterCabin();
            c.FundHousehold(7, 100000);
            var driver = c.NewDriver();
            ConstructionProject project = driver.Commission(c.CabinRequest(), c.Parcels, 10, c.Diag);
            Assert.IsNotNull(project, string.Join("; ", c.Diag));

            ConstructionProjectExecutor.ConstructionProjectSaveDto projectDto =
                c.Executor.CaptureSaveDto(new List<ConstructionProject>(c.Projects));
            NpcConstructionCommissioner.NpcConstructionCommissionerSaveDto recordDto =
                c.Commissioner.CaptureSaveDto();
            var executor2 = new ConstructionProjectExecutor();
            var commissioner2 = new NpcConstructionCommissioner();
            List<ConstructionProject> projects2 = executor2.LoadFromSaveDto(projectDto, c.Diag);
            commissioner2.LoadFromSaveDto(recordDto, c.Diag);

            // A null DTO loads cleanly and leaves an empty commissioner.
            var empty = new NpcConstructionCommissioner();
            empty.LoadFromSaveDto(null, c.Diag);
            Assert.IsNull(empty.FindRecord("npcb-0"));

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
            c2.Budgets.EnsureDay(11);
            Assert.AreEqual(1, driver2.Drive(11, c2.Diag),
                "no phantom block: the unblocked package stages after restore");
            Assert.AreEqual(WorkPackageStatus.MaterialsStaged, projects2[0].WorkPackages[0].Status);
        }

        [Test]
        public void CommissionerSequence_OldSaveFloor_RecomputedFromRestoredIds()
        {
            var dto = new NpcConstructionCommissioner.NpcConstructionCommissionerSaveDto
            {
                CommissionerSequence = -1,
            };
            dto.Records.Add(new NpcConstructionRecord { RecordId = "npcb-0", ProjectId = "proj-a" });
            dto.Records.Add(new NpcConstructionRecord { RecordId = "npcb-1", ProjectId = "proj-b" });
            dto.Records.Add(new NpcConstructionRecord { RecordId = "npcb-2", ProjectId = "proj-c" });

            NpcConstructionCommissioner restored;
            string minted = MintRecordIdAfterRestore(dto, 1, out restored);
            Assert.AreEqual("npcb-3", minted, "an old save without the sequence recomputes the floor from restored ids");
        }

        [Test]
        public void CommissionerSequence_PersistedAhead_WinsOverRestoredIds()
        {
            var dto = new NpcConstructionCommissioner.NpcConstructionCommissionerSaveDto
            {
                CommissionerSequence = 5,
            };
            dto.Records.Add(new NpcConstructionRecord { RecordId = "npcb-0", ProjectId = "proj-a" });
            dto.Records.Add(new NpcConstructionRecord { RecordId = "npcb-1", ProjectId = "proj-b" });

            NpcConstructionCommissioner restored;
            string minted = MintRecordIdAfterRestore(dto, 1, out restored);
            Assert.AreEqual("npcb-5", minted, "a persisted sequence ahead of the restored ids wins");
        }

        [Test]
        public void Driver_BlockedPackage_SurvivesLoad_StillBlockedAfterRestore()
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
            NpcConstructionRecord record = c.Commissioner.FindRecordForProject(project.ProjectId);
            Assert.IsNotNull(record);
            NpcBuildPackageFunding funding = record.FindFunding(project.WorkPackages[0].PackageId);
            Assert.IsNotNull(funding);
            Assert.IsTrue(funding.Stranded, "the failed procurement marks the funding line stranded");
            Assert.IsTrue(!string.IsNullOrWhiteSpace(funding.StrandedReason));

            // Save and load into fresh authorities, as the hub does.
            ConstructionProjectExecutor.ConstructionProjectSaveDto projectDto =
                c.Executor.CaptureSaveDto(new List<ConstructionProject>(c.Projects));
            NpcConstructionCommissioner.NpcConstructionCommissionerSaveDto recordDto =
                c.Commissioner.CaptureSaveDto();
            var executor2 = new ConstructionProjectExecutor();
            var commissioner2 = new NpcConstructionCommissioner();
            List<ConstructionProject> projects2 = executor2.LoadFromSaveDto(projectDto, c.Diag);
            commissioner2.LoadFromSaveDto(recordDto, c.Diag);

            // The stranded marker survives the round trip (the deep-copy).
            NpcConstructionRecord record2 = commissioner2.FindRecordForProject(projects2[0].ProjectId);
            Assert.IsNotNull(record2);
            NpcBuildPackageFunding funding2 = record2.FindFunding(projects2[0].WorkPackages[0].PackageId);
            Assert.IsNotNull(funding2);
            Assert.IsTrue(funding2.Stranded, "the stranded marker survives the round trip");

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
            c2.SalePort = c.SalePort;
            var port2 = new ProcurementPort(c2) { WagonMassKg = 10f };
            var driver2 = c2.NewDriver(port2);

            // The restored driver re-derives the block from the persisted
            // stranded marker: the package stays blocked, nothing is bought
            // a second time.
            c2.Budgets.EnsureDay(12);
            Assert.AreEqual(0, driver2.Drive(12, c2.Diag));
            Assert.AreEqual(WorkPackageStatus.Ready, projects2[0].WorkPackages[0].Status);
            Assert.AreEqual(1, c2.SalePort.Sales.Count, "a blind retry would double-buy the stranded lot");
        }

        [Test]
        public void Driver_BlockedPackage_ExplicitRetryAfterRestore_UnblocksAndStages()
        {
            var c = new Chain();
            c.RegisterCabin();
            c.FundHousehold(7, 100000);
            var port = new ProcurementPort(c) { WagonMassKg = 10f };
            var driver = c.NewDriver(port);
            ConstructionProject project = driver.Commission(c.CabinRequest(), c.Parcels, 10, c.Diag);
            Assert.IsNotNull(project, string.Join("; ", c.Diag));
            Assert.AreEqual(0, driver.Drive(11, c.Diag));
            Assert.AreEqual(1, c.SalePort.Sales.Count, "the failed leg bought once");

            ConstructionProjectExecutor.ConstructionProjectSaveDto projectDto =
                c.Executor.CaptureSaveDto(new List<ConstructionProject>(c.Projects));
            NpcConstructionCommissioner.NpcConstructionCommissionerSaveDto recordDto =
                c.Commissioner.CaptureSaveDto();
            var executor2 = new ConstructionProjectExecutor();
            var commissioner2 = new NpcConstructionCommissioner();
            List<ConstructionProject> projects2 = executor2.LoadFromSaveDto(projectDto, c.Diag);
            commissioner2.LoadFromSaveDto(recordDto, c.Diag);

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
            c2.SalePort = c.SalePort;
            var port2 = new ProcurementPort(c2) { WagonMassKg = 10f };
            var driver2 = c2.NewDriver(port2);

            // The block survived the restore.
            NpcConstructionRecord record2 = commissioner2.FindRecordForProject(projects2[0].ProjectId);
            Assert.IsNotNull(record2);
            Assert.IsTrue(record2.FindFunding(projects2[0].WorkPackages[0].PackageId).Stranded);

            // An explicit retry decision with a wagon that can carry the load
            // clears the flag and the key, and the package stages; the first
            // lot stays supplier-held (the service semantic).
            port2.WagonMassKg = 100000f;
            driver2.AllowProcurementRetry(projects2[0], projects2[0].WorkPackages[0]);
            Assert.IsTrue(!record2.FindFunding(projects2[0].WorkPackages[0].PackageId).Stranded,
                "AllowProcurementRetry clears the stranded marker");
            c2.Budgets.EnsureDay(13);
            Assert.AreEqual(1, driver2.Drive(13, c2.Diag));
            Assert.AreEqual(WorkPackageStatus.MaterialsStaged, projects2[0].WorkPackages[0].Status);
            Assert.AreEqual(2, c2.SalePort.Sales.Count, "the retry buys the lot again; the first lot stays supplier-held");
        }

        [Test]
        public void Driver_BlockedPackageKey_IsProjectAndPackage_DeriveIsIdempotent()
        {
            var c = new Chain();
            c.RegisterCabin();
            c.FundHousehold(7, 100000);
            var port = new ProcurementPort(c) { WagonMassKg = 10f };
            var driver = c.NewDriver(port);
            ConstructionProject project = driver.Commission(c.CabinRequest(), c.Parcels, 10, c.Diag);
            Assert.IsNotNull(project, string.Join("; ", c.Diag));

            Assert.AreEqual(0, driver.Drive(11, c.Diag));
            var keysAfterFirst = new List<string>(driver.BlockedProcurementPackageKeys);
            Assert.AreEqual(1, keysAfterFirst.Count);
            Assert.AreEqual(project.ProjectId + "|" + project.WorkPackages[0].PackageId, keysAfterFirst[0],
                "the blocked key is projectId|packageId");

            // A second drive re-derives the same set (idempotent).
            Assert.AreEqual(0, driver.Drive(12, c.Diag));
            var keysAfterSecond = new List<string>(driver.BlockedProcurementPackageKeys);
            Assert.AreEqual(keysAfterFirst.Count, keysAfterSecond.Count);
            Assert.AreEqual(keysAfterFirst[0], keysAfterSecond[0]);
        }
    }
}
