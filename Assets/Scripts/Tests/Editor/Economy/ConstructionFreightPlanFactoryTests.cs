// DEF-004 (Decision 4): the construction freight plan factory.
// The factory builds a ConstructionFreightPlan for one WorkPackage from
// real authorities — the live lumber yard, the real journey model, the
// real freight business, the real wagon and horses, and the real
// commissioning-household crew — quoted through the freight company's
// authored rate card (25c dispatch plus 1c per mile, round trip,
// provisional). Every missing link refuses loudly with a named reason;
// nothing is invented. The last test runs the factory's plan through
// the real adapter, commissioner, procurement service, and freight
// coordinator end to end (the sale port and price port are fakes standing
// in for the yard runtime adapters, following the pattern in
// NpcConstructionProcurementAdapterTests; the shipment service is real).
// Copyright (c) Land & Ledgers contributors. Licensed under the MIT License.

using System;
using System.Collections.Generic;
using LandLedgers.Animals;
using LandLedgers.Economy;
using LandLedgers.Economy.Blacksmith;
using LandLedgers.Economy.Financing;
using LandLedgers.Economy.Logistics;
using LandLedgers.Economy.Transport;
using LandLedgers.Orchestration.Systems;
using LandLedgers.Population;
using LandLedgers.Primitives;
using LandLedgers.Tasks;
using LandLedgers.Time;
using LandLedgers.World.Journeys;
using LandLedgers.World.Property;
using NUnit.Framework;
using UnityEngine;

namespace LandLedgers.EditorTests.Economy
{
    [TestFixture]
    public sealed class ConstructionFreightPlanFactoryTests
    {
        #region Factory fixture

        private sealed class Fixture
        {
            public JourneyModel Model;
            public string YardBusinessId = "lumber-yard";
            public string WagonAssetId = "opening-freight-wagon";
            public ConstructionProject Project = new ConstructionProject { ProjectId = "proj-1", HouseholdId = 7 };
            public BusinessInstanceState FreightBusiness;
            public EquipmentAsset Wagon;
            public List<AnimalState> Animals = new List<AnimalState>();
            public List<int> Members = new List<int> { 42, 43 };
            public Dictionary<int, int> HomeBuildings = new Dictionary<int, int> { { 42, 7 } };
            public FreightShipmentService Freight = new FreightShipmentService();
            public TaskAuthority Tasks = new TaskAuthority();
            public WorkTimeBudgetStore Budgets = new WorkTimeBudgetStore();
            public PersonScheduleTracker Tracker = new PersonScheduleTracker();
            public CreditCashBridge Cash = new CreditCashBridge();

            public Fixture()
            {
                RebuildModel(3.5f);
                FreightBusiness = BuildFreightBusiness("freight-1",
                    BusinessOwnerIdentity.Npc(41, "Ethan", "Boniecki"));
                Wagon = new EquipmentAsset
                {
                    AssetId = WagonAssetId,
                    Kind = "wagon",
                    DisplayName = "freight wagon",
                    Condition01 = 1f,
                    OwnerKind = "business",
                    OwnerId = "freight-1",
                    // The production opening wagon's capacity (OpeningTransportStock via
                    // WagonAuthority.Configure): 450 kg / 4 m3. The factory does not
                    // set capacity; the wagon carries what the wagon carries.
                    PayloadMassCapacityKg = WagonAuthority.DefaultMassCapacityKg,
                    CargoVolumeCapacityM3 = WagonAuthority.DefaultVolumeCapacityM3,
                };
                Animals.Add(new AnimalState
                {
                    AnimalId = EntityId.For(EntityKind.Animal, 1),
                    Species = AnimalSpecies.Horse,
                    IsActive = true,
                    OwnerKind = AnimalOwnerKind.Business,
                    OwnerId = "freight-1",
                });
            }

            public static BusinessInstanceState BuildFreightBusiness(string instanceId, BusinessOwnerIdentity owner)
            {
                BusinessProfileDefinition profile = ScriptableObject.CreateInstance<BusinessProfileDefinition>();
                return BusinessInstanceState.Create(instanceId, profile, -1, owner, BusinessType.LiveryFreight);
            }

            /// <summary>Rebuilds the journey model: yard location plus the household home location, joined by one road edge.</summary>
            public void RebuildModel(float miles, bool withEdge = true, bool withYardLocation = true, bool withSiteLocation = true)
            {
                Model = new JourneyModel();
                if (withYardLocation)
                {
                    Assert.IsNull(Model.RegisterLocation(new JourneyLocation("yard-loc", JourneyLocationKind.Store, "Lumber Yard", 0f, 0f)
                    {
                        BusinessId = YardBusinessId,
                    }));
                }
                if (withSiteLocation)
                    Assert.IsNull(Model.RegisterLocation(new JourneyLocation("building:7", JourneyLocationKind.TownBuilding, "Household 7 home", 3.5f, 0f)));
                if (withEdge && withYardLocation && withSiteLocation)
                    Assert.IsNull(Model.AddEdge("yard-loc", "building:7", miles, "yard road"));
            }

            public ConstructionFreightPlanFactory Build()
            {
                return new ConstructionFreightPlanFactory(
                    id => string.Equals(id, Project.ProjectId, StringComparison.Ordinal) ? Project : null,
                    YardBusinessId,
                    WagonAssetId,
                    () => Model,
                    () => FreightBusiness,
                    25, 1, true,
                    assetId => string.Equals(assetId, WagonAssetId, StringComparison.Ordinal) ? Wagon : null,
                    () => Animals,
                    householdId => householdId == Project.HouseholdId ? Members : new List<int>(),
                    personId => HomeBuildings.TryGetValue(personId, out int buildingId) ? buildingId : -1,
                    Freight, Tasks, Budgets, Tracker, Cash);
            }

            public WorkPackage Package()
            {
                return new WorkPackage { PackageId = "pkg-1", ProjectId = Project.ProjectId };
            }
        }

        #endregion

        [Test]
        public void QuoteMathOnRouteMiles_ChargeIsBasePlusRoundTripMileage()
        {
            var f = new Fixture();
            ConstructionFreightPlanFactory factory = f.Build();

            ConstructionFreightPlan plan = factory.Build(f.Package());

            Assert.IsNotNull(plan, factory.LastRefusal);
            // 3.5 miles round trip: 25c + ceil(2 x 3.5) x 1c = 25 + 7 = 32c.
            Assert.AreEqual(32, plan.FreightChargeCents);
            Assert.AreEqual("business:freight-1", plan.CarrierPayeeLabel);
            Assert.AreEqual("household:7", plan.BuyerOwnerName);
        }

        [Test]
        public void ChargeFollowsModelMiles_NotAFixedMileage()
        {
            var f = new Fixture();
            f.RebuildModel(0.2f);
            ConstructionFreightPlanFactory factory = f.Build();

            ConstructionFreightPlan plan = factory.Build(f.Package());

            Assert.IsNotNull(plan, factory.LastRefusal);
            // 0.2 miles round trip: 25c + ceil(2 x 0.2) x 1c = 25 + 1 = 26c.
            Assert.AreEqual(26, plan.FreightChargeCents);
        }

        [Test]
        public void Unroutable_Refuses_NoInventedMiles()
        {
            var f = new Fixture();
            f.RebuildModel(3.5f, withEdge: false);
            ConstructionFreightPlanFactory factory = f.Build();

            ConstructionFreightPlan plan = factory.Build(f.Package());

            Assert.IsNull(plan);
            Assert.IsFalse(string.IsNullOrWhiteSpace(factory.LastRefusal));
            StringAssert.Contains("no route", factory.LastRefusal);
        }

        [Test]
        public void NoYardLocation_Refuses()
        {
            var f = new Fixture();
            f.RebuildModel(3.5f, withYardLocation: false);
            ConstructionFreightPlanFactory factory = f.Build();

            ConstructionFreightPlan plan = factory.Build(f.Package());

            Assert.IsNull(plan);
            StringAssert.Contains("lumber yard", factory.LastRefusal);
        }

        [Test]
        public void NoSiteLocation_Refuses()
        {
            var f = new Fixture();
            ConstructionFreightPlanFactory factory = f.Build();

            // The home building has no journey location: the model cannot route to it.
            f.HomeBuildings[42] = 99;
            Assert.IsNull(factory.Build(f.Package()));
            StringAssert.Contains("no route", factory.LastRefusal);

            // The household's first active member has no home building at all.
            f.HomeBuildings.Clear();
            Assert.IsNull(factory.Build(f.Package()));
            StringAssert.Contains("home building", factory.LastRefusal);
        }

        [Test]
        public void NoWagonOrWrongOwnerWagon_Refuses_NoPhantomTransport()
        {
            var f = new Fixture();
            ConstructionFreightPlanFactory factory = f.Build();

            f.Wagon = null;
            Assert.IsNull(factory.Build(f.Package()));
            StringAssert.Contains("wagon", factory.LastRefusal);

            f = new Fixture();
            factory = f.Build();
            f.Wagon.OwnerId = "someone-else";
            Assert.IsNull(factory.Build(f.Package()));
            StringAssert.Contains("not owned", factory.LastRefusal);
        }

        [Test]
        public void NoHorse_Refuses()
        {
            var f = new Fixture();
            f.Animals.Clear();
            ConstructionFreightPlanFactory factory = f.Build();

            ConstructionFreightPlan plan = factory.Build(f.Package());

            Assert.IsNull(plan);
            StringAssert.Contains("horse", factory.LastRefusal);
        }

        [Test]
        public void NoDriver_Refuses()
        {
            var f = new Fixture();
            f.FreightBusiness = Fixture.BuildFreightBusiness("freight-1", BusinessOwnerIdentity.Town());
            ConstructionFreightPlanFactory factory = f.Build();

            ConstructionFreightPlan plan = factory.Build(f.Package());

            Assert.IsNull(plan);
            StringAssert.Contains("driver", factory.LastRefusal);
        }

        [Test]
        public void CrewRules_LoaderAndUnloaderAreDistinctHouseholdMembersExcludingTheDriver()
        {
            var f = new Fixture();
            ConstructionFreightPlanFactory factory = f.Build();

            // One member cannot staff both ends.
            f.Members = new List<int> { 42 };
            Assert.IsNull(factory.Build(f.Package()));
            StringAssert.Contains("fewer than two", factory.LastRefusal);

            // Two members staff both ends, neither is the driver.
            f.Members = new List<int> { 42, 43 };
            ConstructionFreightPlan plan = factory.Build(f.Package());
            Assert.IsNotNull(plan, factory.LastRefusal);
            Assert.AreEqual(EntityId.For(EntityKind.Person, 42), plan.LoaderId);
            Assert.AreEqual(EntityId.For(EntityKind.Person, 43), plan.UnloaderId);
            Assert.AreNotEqual(plan.LoaderId, plan.UnloaderId);
            Assert.AreNotEqual(plan.DriverId, plan.LoaderId);
            Assert.AreNotEqual(plan.DriverId, plan.UnloaderId);

            // The driver, when among the household members, is excluded and the next two are picked.
            // (The driver is homed like every household member, so the site still resolves from him.)
            f.HomeBuildings[41] = 7;
            f.Members = new List<int> { 41, 42, 43 };
            plan = factory.Build(f.Package());
            Assert.IsNotNull(plan, factory.LastRefusal);
            Assert.AreEqual(EntityId.For(EntityKind.Person, 42), plan.LoaderId);
            Assert.AreEqual(EntityId.For(EntityKind.Person, 43), plan.UnloaderId);
        }

        [Test]
        public void TransitSeconds_AreOneWayMinutesFromTheModel()
        {
            var f = new Fixture();
            ConstructionFreightPlanFactory factory = f.Build();

            ConstructionFreightPlan plan = factory.Build(f.Package());

            Assert.IsNotNull(plan, factory.LastRefusal);
            JourneyTravelEstimate estimate = JourneyTravel.EstimateDrive(f.Model, "yard-loc", "building:7");
            Assert.IsTrue(estimate.FromModel);
            Assert.Greater(estimate.MinutesOneWay, 0);
            Assert.AreEqual(estimate.MinutesOneWay * 60f, plan.TransitGameSeconds);
        }

        [Test]
        public void CargoSpec_IsTheDefault_NoInventedLumberMass()
        {
            var f = new Fixture();
            ConstructionFreightPlanFactory factory = f.Build();

            ConstructionFreightPlan plan = factory.Build(f.Package());

            Assert.IsNotNull(plan, factory.LastRefusal);
            Assert.AreEqual(1f, plan.CargoSpec.MassKgPerUnit);
            Assert.AreEqual(0.01f, plan.CargoSpec.VolumeM3PerUnit);
        }

        [Test]
        public void SiteLabel_IsTheProjectId()
        {
            var f = new Fixture();
            ConstructionFreightPlanFactory factory = f.Build();

            ConstructionFreightPlan plan = factory.Build(f.Package());

            Assert.IsNotNull(plan, factory.LastRefusal);
            Assert.AreEqual("proj-1", plan.SiteLabel);
            // The wagon, driver, and horse are the real freight authorities, not stand-ins.
            Assert.AreEqual("opening-freight-wagon", plan.Wagon.AssetId);
            Assert.AreEqual(EntityId.For(EntityKind.Person, 41), plan.DriverId);
            Assert.AreEqual(1, plan.DraftAnimalIds.Count);
            Assert.AreEqual(EntityId.For(EntityKind.Animal, 1), plan.DraftAnimalIds[0]);
        }

        #region End-to-end chain (real factory through the real adapter)

        // Adapted from NpcConstructionProcurementAdapterTests: the same
        // Chain scaffolding, with the fake freight plan replaced by the
        // real ConstructionFreightPlanFactory. The sale port and price port
        // remain fakes standing in for the yard runtime adapters; the
        // commissioner, procurement service, freight coordinator, and
        // shipment service are the real production authorities.

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
                    SaleId = "sale-0",
                    SupplierBusinessId = supplierBusinessId,
                    ProjectId = projectId,
                    PackageId = packageId,
                    DayIndex = dayIndex,
                };
                foreach (ConstructionMaterialRequirement r in requirements)
                {
                    Stock[r.MaterialDisplayName] -= r.RequiredUnits;
                    int unitPrice = pricePort != null ? pricePort.UnitPriceCents(r) : -1;
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

            public Chain()
            {
                Prices.Prices["lumber"] = 40;
                SalePort.Stock["lumber"] = 10000;
                var yard = new FakeGoodsSupplier { BusinessId = "lumber-yard" };
                yard.Categories.Add("construction-lumber");
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
                CarrierPurse = new PurseCashStore("business:freight-1", 0, "test seed capital");
                Assert.IsNull(Cash.Register("business:freight-1", CarrierPurse));
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
                var framing = new BuildingDesignPhase
                {
                    PhaseName = "Framing", Sequence = 0, LaborMinutes = 480, Notes = "log walls and roof",
                };
                // 300 units, not the adapter tests' 600: at the default cargo
                // spec (1 kg / 0.01 m3 per unit) 300 units is 300 kg / 3 m3,
                // which fits the production opening wagon (450 kg / 4 m3) in
                // one trip. A 600-unit load is 600 kg and the shipment service
                // honestly refuses it pre-load — a single construction shipment
                // is bounded by the wagon, and multi-trip or split-load support
                // is a follow-up (DEF-016 territory), not this slice.
                framing.Materials.Add(Material("lumber", 300, "board feet", ConstructionMaterialKind.Lumber));
                framing.RequiredToolItemIds.Add("saw");
                design.Phases.Add(framing);
                design.SpaceSpecs.Add(new BuildingDesignSpaceSpec { Label = "main room", SleepingCapacity = 4 });
                Assert.IsNull(Catalog.RegisterDesign(design, Diag), string.Join("; ", Diag));
                ConstructionProject project = Commissioner.CommissionProject(Executor, Catalog,
                    householdId, "cabin-a", "parcel-9", Prices, 10, Diag, out record);
                Assert.IsNotNull(project, string.Join("; ", Diag));
                Assert.IsNotNull(record);
                return project;
            }
        }

        [Test]
        public void RealFactoryPlan_ProcuresFreightsAndStages_ExactMoneyAndExactStock()
        {
            var c = new Chain();
            c.FundHousehold(7, 100000);
            NpcConstructionRecord record;
            ConstructionProject project = c.CommissionCabin(out record);
            WorkPackage framing = project.WorkPackages[0];
            Assert.AreEqual(WorkPackageStatus.Ready, framing.Status);

            // The real factory: yard 3.5 road miles from the household's home building.
            // The plan's cash authority is the chain's bridge (the binding passes hub.CreditCash),
            // so the freight charge pays out of the same funded stores as the materials.
            var f = new Fixture();
            f.Project = project;
            f.Cash = c.Cash;
            ConstructionFreightPlanFactory factory = f.Build();
            var adapter = new NpcConstructionProcurementAdapter(c.Commissioner, c.Procurement, c.SalePort,
                c.Prices, c.Directory, c.FreightCoordinator, package => factory.Build(package),
                c.Executor, c.Cash, c.Obligations, c.Ids);

            Assert.IsNull(adapter.ProcureAndStage(project, framing, record, 11, c.Diag),
                string.Join("; ", c.Diag));
            Assert.AreEqual(WorkPackageStatus.MaterialsStaged, framing.Status);
            Assert.AreEqual(1, framing.StagedMaterialLines.Count);
            Assert.AreEqual(300, framing.StagedMaterialLines[0].Units);
            Assert.AreEqual("lumber", framing.StagedMaterialLines[0].DisplayName);

            // Exact money: 300 lumber at 40c is 12000c materials plus the
            // factory's quoted 32c freight charge (25c + ceil(2 x 3.5) x 1c),
            // paid exactly once.
            Assert.AreEqual(1, c.SalePort.Sales.Count);
            Assert.AreEqual(12000, c.SalePort.Sales[0].TotalCents);
            Assert.AreEqual(100000 - 12000 - 32, c.HouseholdLedger.GetBalanceCents());
            Assert.AreEqual(12000, c.YardPurse.ReadBalanceCents());
            Assert.AreEqual(32, c.CarrierPurse.ReadBalanceCents());
            Assert.AreEqual(9700, c.SalePort.Stock["lumber"]);
            Assert.AreEqual(12000, record.FindFunding(framing.PackageId).ActualMaterialCostCents);
            Assert.AreEqual(32, record.FindFunding(framing.PackageId).FreightCostCents);

            // Staging consumed the transient stockpile: nothing left behind.
            Assert.IsNotNull(adapter.LastStockpile);
            Assert.AreEqual(0, adapter.LastStockpile.AvailableUnits("lumber"));
        }

        #endregion
    }
}
