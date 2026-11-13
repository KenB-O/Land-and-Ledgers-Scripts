// Ported verbatim from forestry Slice 07, commit 8fa6aca0, onto the
// economic-integration line (baseline 2cb4781) as C4 Tier 1. No logic
// change; this header note is the only addition.
// Copyright (c) Land & Ledgers contributors. Licensed under the MIT License.
// Slice 07: the procurement port adapter. The adapter is the only new
// production code under test here; the commissioner, the procurement
// service, and the freight coordinator are the real production
// authorities, and the sale port and the freight plan are fakes standing
// in for the runtime adapters (the yard sale port and the Sol-owned plan
// factory) that Unity-side code will supply. No production binding is
// exercised: Sol owns the SimulationDrivers binding.

using System;
using System.Collections.Generic;
using LandLedgers.Economy;
using LandLedgers.Economy.Blacksmith;
using LandLedgers.Economy.Equipment;
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
    [TestFixture]
    public sealed class NpcConstructionProcurementAdapterTests
    {
        #region Fakes (runtime-adapter stand-ins)

        private sealed class FakePricePort : IConstructionMaterialPricePort
        {
            public readonly Dictionary<string, int> Prices = new Dictionary<string, int>();
            public int UnitPriceCents(ConstructionMaterialRequirement requirement)
            {
                if (requirement == null) return -1;
                return Prices.TryGetValue(requirement.MaterialDisplayName, out int price) ? price : -1;
            }
        }

        private sealed class FakeSalePort : IConstructionMaterialSalePort
        {
            public readonly Dictionary<string, int> Stock = new Dictionary<string, int>();
            public readonly List<ConstructionMaterialSale> Sales = new List<ConstructionMaterialSale>();
            public bool Misprice;
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
                    Stock[r.MaterialDisplayName] -= r.RequiredUnits;
                    int unitPrice = pricePort != null ? pricePort.UnitPriceCents(r) : -1;
                    if (Misprice && unitPrice >= 0) unitPrice += 1;
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

        #endregion

        #region Fixture builders

        private static ConstructionMaterialRequirement Material(string name, int units, string unitLabel,
            ConstructionMaterialKind kind, string supplier = "")
        {
            return new ConstructionMaterialRequirement
            {
                RequirementId = "REQ-" + name,
                MaterialKind = kind,
                MaterialDisplayName = name,
                RequiredUnits = units,
                UnitLabel = unitLabel,
                ProvenanceKind = string.IsNullOrWhiteSpace(supplier)
                    ? ConstructionMaterialProvenanceKind.AnyRealSource
                    : ConstructionMaterialProvenanceKind.NamedSupplier,
                SupplierBusinessId = supplier ?? string.Empty,
            };
        }

        private sealed class Chain
        {
            public List<string> Diag = new List<string>();
            public BuildingDesignCatalog Catalog = new BuildingDesignCatalog();
            public ConstructionProjectExecutor Executor = new ConstructionProjectExecutor();
            public NpcConstructionCommissioner Commissioner = new NpcConstructionCommissioner();
            public ConstructionProcurementService Procurement = new ConstructionProcurementService();
            public ConstructionMaterialFreightCoordinator FreightCoordinator = new ConstructionMaterialFreightCoordinator();
            public FakePricePort Prices = new FakePricePort();
            public FakeSalePort SalePort = new FakeSalePort();
            public SupplierDirectory Directory = new SupplierDirectory();
            public CreditCashBridge Cash = new CreditCashBridge();
            public FinancialObligationAuthority Obligations = new FinancialObligationAuthority();
            public EntityIdRegistry Ids = new EntityIdRegistry();
            public HouseholdLedger HouseholdLedger;
            public PurseCashStore YardPurse;
            public PurseCashStore CarrierPurse;
            public int FactoryCalls;

            public Chain()
            {
                Prices.Prices["fieldstone"] = 50;
                Prices.Prices["lumber"] = 40;
                SalePort.Stock["fieldstone"] = 1000;
                SalePort.Stock["lumber"] = 10000;
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
                CarrierPurse = new PurseCashStore("business:carrier", 0, "test seed capital");
                Assert.IsNull(Cash.Register("business:carrier", CarrierPurse));
            }

            public void RegisterSecondSupplier()
            {
                var sawmill = new FakeGoodsSupplier { BusinessId = "sawmill-a" };
                sawmill.Categories.Add("construction-lumber");
                sawmill.Categories.Add("construction-materials");
                Directory.Register(sawmill);
            }

            public ConstructionProject CommissionCabin(out NpcConstructionRecord record, int householdId = 7)
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
                ConstructionProject project = Commissioner.CommissionProject(Executor, Catalog,
                    householdId, "cabin-a", "parcel-9", Prices, 10, Diag, out record);
                Assert.IsNotNull(project, string.Join("; ", Diag));
                Assert.IsNotNull(record);
                return project;
            }

            public ConstructionFreightPlan FreightPlan(ConstructionProject project, float wagonMassKg = 100000f,
                int freightChargeCents = 0, bool withLoader = true, bool withWagon = true)
            {
                FactoryCalls++;
                return new ConstructionFreightPlan
                {
                    Freight = new FreightShipmentService(),
                    Wagon = withWagon ? new EquipmentAsset
                    {
                        AssetId = "wagon-1", Kind = "wagon", DisplayName = "farm wagon",
                        PayloadMassCapacityKg = wagonMassKg, CargoVolumeCapacityM3 = 100f, Condition01 = 1f,
                    } : null,
                    DriverId = EntityId.For(EntityKind.Person, 41),
                    LoaderId = withLoader ? EntityId.For(EntityKind.Person, 42) : EntityId.Invalid,
                    UnloaderId = EntityId.For(EntityKind.Person, 43),
                    Tasks = new TaskAuthority(),
                    Budgets = new WorkTimeBudgetStore(),
                    Tracker = new PersonScheduleTracker(),
                    SiteLabel = "site:" + project.ProjectId,
                    FreightChargeCents = freightChargeCents,
                    CarrierPayeeLabel = "business:carrier",
                    Cash = Cash,
                    BuyerOwnerName = "household:7",
                    CargoSpec = new FreightCargoSpec { MassKgPerUnit = 1f, VolumeM3PerUnit = 0.01f },
                };
            }

            public NpcConstructionProcurementAdapter NewAdapter(ConstructionProject project,
                Func<WorkPackage, ConstructionFreightPlan> factory = null)
            {
                if (factory == null) factory = package => FreightPlan(project);
                return new NpcConstructionProcurementAdapter(Commissioner, Procurement, SalePort,
                    Prices, Directory, FreightCoordinator, factory, Executor, Cash, Obligations, Ids);
            }
        }

        #endregion

        [Test]
        public void LumberOnlyPackage_ProcuresFreightsAndStages_ExactMoneyAndExactStock()
        {
            var c = new Chain();
            c.FundHousehold(7, 100000);
            NpcConstructionRecord record;
            ConstructionProject project = c.CommissionCabin(out record);
            WorkPackage framing = project.WorkPackages[1];
            Assert.AreEqual(WorkPackageStatus.Ready, framing.Status);
            var adapter = c.NewAdapter(project,
                package => c.FreightPlan(project, 100000f, 500));

            Assert.IsNull(adapter.ProcureAndStage(project, framing, record, 11, c.Diag),
                string.Join("; ", c.Diag));
            Assert.AreEqual(WorkPackageStatus.MaterialsStaged, framing.Status);
            Assert.AreEqual(1, framing.StagedMaterialLines.Count);
            Assert.AreEqual(600, framing.StagedMaterialLines[0].Units);
            Assert.AreEqual("lumber", framing.StagedMaterialLines[0].DisplayName);
            Assert.IsFalse(string.IsNullOrWhiteSpace(framing.StagedMaterialLines[0].Provenance));
            StringAssert.Contains("yard lot", framing.StagedMaterialLines[0].Provenance);

            // Exact money: 600 lumber at 40c is 24000c materials plus the
            // 500c quoted freight charge, paid exactly once.
            Assert.AreEqual(1, c.SalePort.Sales.Count);
            Assert.AreEqual(24000, c.SalePort.Sales[0].TotalCents);
            Assert.AreEqual(100000 - 24000 - 500, c.HouseholdLedger.GetBalanceCents());
            Assert.AreEqual(24000, c.YardPurse.ReadBalanceCents());
            Assert.AreEqual(500, c.CarrierPurse.ReadBalanceCents());
            Assert.AreEqual(9400, c.SalePort.Stock["lumber"]);
            Assert.AreEqual(24000, record.FindFunding(framing.PackageId).ActualMaterialCostCents);
            Assert.AreEqual(500, record.FindFunding(framing.PackageId).FreightCostCents);

            // Staging consumed the transient stockpile: nothing left behind.
            Assert.IsNotNull(adapter.LastStockpile);
            Assert.AreEqual(0, adapter.LastStockpile.AvailableUnits("lumber"));
            Assert.AreEqual(1, c.FactoryCalls);
        }

        [Test]
        public void PackageSpanningTwoSuppliers_RefusesWhole_NothingSold()
        {
            var c = new Chain();
            c.RegisterSecondSupplier();
            c.FundHousehold(7, 100000);
            NpcConstructionRecord record;
            ConstructionProject project = c.CommissionCabin(out record);
            WorkPackage framing = project.WorkPackages[1];
            framing.Materials.Clear();
            framing.Materials.Add(Material("lumber", 10, "board feet", ConstructionMaterialKind.Lumber, "lumber-yard"));
            framing.Materials.Add(Material("lumber", 10, "board feet", ConstructionMaterialKind.Lumber, "sawmill-a"));
            var adapter = c.NewAdapter(project);

            string problem = adapter.ProcureAndStage(project, framing, record, 11, c.Diag);
            Assert.IsNotNull(problem);
            StringAssert.Contains("spans 2 suppliers", problem);
            Assert.AreEqual(0, c.SalePort.Sales.Count);
            Assert.AreEqual(10000, c.SalePort.Stock["lumber"]);
            Assert.AreEqual(100000, c.HouseholdLedger.GetBalanceCents());
            Assert.AreEqual(0, c.YardPurse.ReadBalanceCents());
            Assert.AreEqual(WorkPackageStatus.Ready, framing.Status);
            Assert.AreEqual(0, framing.StagedMaterialLines.Count);
        }

        [Test]
        public void SaleTotalMismatch_Refuses_NothingStaged_NoCashMoved()
        {
            var c = new Chain();
            c.SalePort.Misprice = true;
            c.FundHousehold(7, 100000);
            NpcConstructionRecord record;
            ConstructionProject project = c.CommissionCabin(out record);
            WorkPackage framing = project.WorkPackages[1];
            var adapter = c.NewAdapter(project);

            string problem = adapter.ProcureAndStage(project, framing, record, 11, c.Diag);
            Assert.IsNotNull(problem);
            StringAssert.Contains("totals 24600c against the priced 24000c", problem);
            // The sale stands at the supplier (a real purchase record) but
            // no cash moved and nothing was staged.
            Assert.AreEqual(1, c.SalePort.Sales.Count);
            Assert.AreEqual(100000, c.HouseholdLedger.GetBalanceCents());
            Assert.AreEqual(0, c.YardPurse.ReadBalanceCents());
            Assert.AreEqual(WorkPackageStatus.Ready, framing.Status);
            Assert.AreEqual(0, framing.StagedMaterialLines.Count);
            Assert.IsNotNull(adapter.LastStockpile);
            Assert.AreEqual(0, adapter.LastStockpile.AvailableUnits("lumber"));
        }

        [Test]
        public void FreightRefusal_LeavesSaleStanding_NothingInStockpile()
        {
            var c = new Chain();
            c.FundHousehold(7, 100000);
            NpcConstructionRecord record;
            ConstructionProject project = c.CommissionCabin(out record);
            WorkPackage framing = project.WorkPackages[1];
            // A toy wagon that cannot carry 600 units at 1kg each.
            var adapter = c.NewAdapter(project,
                package => c.FreightPlan(project, 10f, 0));

            string problem = adapter.ProcureAndStage(project, framing, record, 11, c.Diag);
            Assert.IsNotNull(problem);
            StringAssert.Contains("blocked pre-load", problem);
            // The sale stands and was paid (real purchase, buyer goods at
            // the yard) but NOTHING reached the site stockpile.
            Assert.AreEqual(1, c.SalePort.Sales.Count);
            Assert.AreEqual(100000 - 24000, c.HouseholdLedger.GetBalanceCents());
            Assert.AreEqual(24000, c.YardPurse.ReadBalanceCents());
            Assert.IsNotNull(adapter.LastStockpile);
            Assert.AreEqual(0, adapter.LastStockpile.AvailableUnits("lumber"));
            Assert.AreEqual(WorkPackageStatus.Ready, framing.Status);
            Assert.AreEqual(0, framing.StagedMaterialLines.Count);
        }

        [Test]
        public void SecondCallAfterSuccess_Refuses_PackageNoLongerReady()
        {
            var c = new Chain();
            c.FundHousehold(7, 100000);
            NpcConstructionRecord record;
            ConstructionProject project = c.CommissionCabin(out record);
            WorkPackage framing = project.WorkPackages[1];
            var adapter = c.NewAdapter(project);

            Assert.IsNull(adapter.ProcureAndStage(project, framing, record, 11, c.Diag),
                string.Join("; ", c.Diag));
            Assert.AreEqual(WorkPackageStatus.MaterialsStaged, framing.Status);
            int salesAfterFirst = c.SalePort.Sales.Count;
            int balanceAfterFirst = c.HouseholdLedger.GetBalanceCents();

            string problem = adapter.ProcureAndStage(project, framing, record, 12, c.Diag);
            Assert.IsNotNull(problem);
            StringAssert.Contains("MaterialsStaged", problem);
            Assert.AreEqual(salesAfterFirst, c.SalePort.Sales.Count);
            Assert.AreEqual(balanceAfterFirst, c.HouseholdLedger.GetBalanceCents());
            Assert.AreEqual(1, c.FactoryCalls);
        }

        [Test]
        public void NullPlanFromFactory_Refuses_NothingSold()
        {
            var c = new Chain();
            c.FundHousehold(7, 100000);
            NpcConstructionRecord record;
            ConstructionProject project = c.CommissionCabin(out record);
            WorkPackage framing = project.WorkPackages[1];
            var adapter = c.NewAdapter(project, package => null);

            string problem = adapter.ProcureAndStage(project, framing, record, 11, c.Diag);
            Assert.IsNotNull(problem);
            StringAssert.Contains("no plan", problem);
            Assert.AreEqual(0, c.SalePort.Sales.Count);
            Assert.AreEqual(100000, c.HouseholdLedger.GetBalanceCents());
            Assert.AreEqual(WorkPackageStatus.Ready, framing.Status);
        }

        [Test]
        public void PlanWithNoWagon_Refuses_NothingSold()
        {
            var c = new Chain();
            c.FundHousehold(7, 100000);
            NpcConstructionRecord record;
            ConstructionProject project = c.CommissionCabin(out record);
            WorkPackage framing = project.WorkPackages[1];
            var adapter = c.NewAdapter(project,
                package => c.FreightPlan(project, 100000f, 0, true, false));

            string problem = adapter.ProcureAndStage(project, framing, record, 11, c.Diag);
            Assert.IsNotNull(problem);
            StringAssert.Contains("no wagon", problem);
            Assert.AreEqual(0, c.SalePort.Sales.Count);
            Assert.AreEqual(100000, c.HouseholdLedger.GetBalanceCents());
            Assert.AreEqual(WorkPackageStatus.Ready, framing.Status);
        }

        [Test]
        public void PlanWithNoLoader_Refuses_NothingSold()
        {
            var c = new Chain();
            c.FundHousehold(7, 100000);
            NpcConstructionRecord record;
            ConstructionProject project = c.CommissionCabin(out record);
            WorkPackage framing = project.WorkPackages[1];
            var adapter = c.NewAdapter(project,
                package => c.FreightPlan(project, 100000f, 0, false, true));

            string problem = adapter.ProcureAndStage(project, framing, record, 11, c.Diag);
            Assert.IsNotNull(problem);
            StringAssert.Contains("no loader", problem);
            Assert.AreEqual(0, c.SalePort.Sales.Count);
            Assert.AreEqual(100000, c.HouseholdLedger.GetBalanceCents());
            Assert.AreEqual(WorkPackageStatus.Ready, framing.Status);
        }
    }
}
