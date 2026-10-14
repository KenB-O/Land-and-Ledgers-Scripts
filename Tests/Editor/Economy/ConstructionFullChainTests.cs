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
    /// <summary>
    /// Pass VIII Worker 3 (6.2): production-authority proofs for the full
    /// NPC construction chain — project creation → parcel authorization →
    /// planning → WorkPackages → materials → supplier discovery →
    /// procurement → freight → site staging → equipment → workers → labor
    /// minutes → progress → completion → usable spaces. Every proof runs
    /// against the real production authorities; fakes stand in only for
    /// the runtime adapters (supplier lots, parcel titles) that Unity-side
    /// code will implement.
    /// </summary>
    [TestFixture]
    public sealed class ConstructionFullChainTests
    {
        #region Fakes (runtime-adapter stand-ins)

        private sealed class FakeParcelAuthority : IConstructionParcelAuthority
        {
            public readonly Dictionary<string, string> HolderByParcel = new Dictionary<string, string>();
            public string AuthorizeBuild(int householdId, string parcelId, int dayIndex, List<string> diagnostics)
            {
                if (string.IsNullOrWhiteSpace(parcelId))
                    return "no parcel id.";
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
            public readonly List<string> ReturnLog = new List<string>();
            public string TryReturn(ConsumedMaterialLine line, string projectLabel, int dayIndex,
                List<string> diagnostics)
            {
                if (line == null || line.Units <= 0) return "nothing to return.";
                if (!Returned.ContainsKey(line.DisplayName)) Returned[line.DisplayName] = 0;
                Returned[line.DisplayName] += line.Units;
                ReturnLog.Add($"{line.DisplayName} x{line.Units} from {projectLabel} (day {dayIndex})");
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
                // All-or-nothing: verify first, consume second.
                foreach (ConstructionMaterialRequirement r in requirements)
                {
                    if (r == null || !r.IsCoherent) return $"incoherent requirement.";
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

        #endregion

        #region Fixture builders

        private static ConstructionMaterialRequirement Material(string name, int units, string unitLabel,
            ConstructionMaterialKind kind = ConstructionMaterialKind.Other, string supplier = "")
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

        private static BuildingDesign CabinDesign(string designId)
        {
            var design = new BuildingDesign
            {
                DesignId = designId,
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
            return design;
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
            public EquipmentReservationService Equipment = new EquipmentReservationService();
            public TaskAuthority Tasks = new TaskAuthority();
            public WorkTimeBudgetStore Budgets = new WorkTimeBudgetStore();
            public HousingAuthority Housing = new HousingAuthority();
            public CreditCashBridge Cash = new CreditCashBridge();
            public FinancialObligationAuthority Obligations = new FinancialObligationAuthority();
            public EntityIdRegistry Ids = new EntityIdRegistry();

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

            public ConstructionProject CommissionCabin(out NpcConstructionRecord record, int householdId = 7)
            {
                Assert.IsNull(Catalog.RegisterDesign(CabinDesign("cabin-a"), Diag));
                ConstructionProject project = Commissioner.CommissionProject(Executor, Catalog,
                    householdId, "cabin-a", "parcel-9", Prices, 10, Diag, out record);
                Assert.IsNotNull(project, string.Join("; ", Diag));
                Assert.IsNotNull(record);
                return project;
            }

            public EquipmentAsset Wagon(string assetId, float massKg, float volM3)
            {
                return new EquipmentAsset
                {
                    AssetId = assetId,
                    Kind = "wagon",
                    DisplayName = "farm wagon",
                    PayloadMassCapacityKg = massKg,
                    CargoVolumeCapacityM3 = volM3,
                    Condition01 = 1f,
                };
            }

            public ConstructionFreightPlan FreightPlan(EquipmentAsset wagon, string siteLabel,
                int driverPersonId, int loaderPersonId, int unloaderPersonId)
            {
                return new ConstructionFreightPlan
                {
                    Freight = new FreightShipmentService(),
                    Wagon = wagon,
                    DriverId = EntityId.For(EntityKind.Person, driverPersonId),
                    LoaderId = EntityId.For(EntityKind.Person, loaderPersonId),
                    UnloaderId = EntityId.For(EntityKind.Person, unloaderPersonId),
                    Tasks = new TaskAuthority(),
                    Budgets = new WorkTimeBudgetStore(),
                    Tracker = new PersonScheduleTracker(),
                    SiteLabel = siteLabel,
                    FreightChargeCents = 0,
                    CargoSpec = new FreightCargoSpec { MassKgPerUnit = 1f, VolumeM3PerUnit = 0.01f },
                };
            }
        }

        /// <summary>Runs the full material leg: sale → freight → stockpile → stage.</summary>
        private string RunMaterialLeg(Chain c, ConstructionProject project, WorkPackage package,
            ConstructionSiteStockpile stockpile, ConstructionFreightPlan plan, int day)
        {
            int priced = 0;
            foreach (ConstructionMaterialRequirement requirement in package.Materials)
            {
                int unitPrice = c.Prices.UnitPriceCents(requirement);
                Assert.GreaterOrEqual(unitPrice, 0);
                priced += unitPrice * requirement.RequiredUnits;
            }
            ConstructionMaterialSale sale;
            string problem = c.Procurement.ProcureFreightAndStage(
                c.Executor, project, package, c.SalePort, c.Prices, c.Directory,
                c.FreightCoordinator, plan, stockpile,
                c.Cash, "household:7", c.Obligations, c.Ids,
                priced, day, c.Diag, out sale);
            return problem;
        }

        #endregion

        #region Parcel authorization

        [Test]
        public void ParcelAuthorization_UnknownParcel_Refused()
        {
            var c = new Chain();
            Assert.IsNull(c.Catalog.RegisterDesign(CabinDesign("cabin-a"), c.Diag));
            ConstructionProject project = c.Executor.CreateProject(c.Catalog, 7, "cabin-a",
                "parcel-void", c.Parcels, 10, c.Diag);
            Assert.IsNull(project, "a project on an unregistered parcel must not exist.");
            StringAssert.Contains("not a registered parcel", string.Join("; ", c.Diag));
        }

        [Test]
        public void ParcelAuthorization_ParcelHeldByAnother_Refused()
        {
            var c = new Chain();
            Assert.IsNull(c.Catalog.RegisterDesign(CabinDesign("cabin-a"), c.Diag));
            // parcel-9 is held by household:7 — household 8 may not build on it.
            ConstructionProject project = c.Executor.CreateProject(c.Catalog, 8, "cabin-a",
                "parcel-9", c.Parcels, 10, c.Diag);
            Assert.IsNull(project);
            StringAssert.Contains("household:7", string.Join("; ", c.Diag));
        }

        [Test]
        public void ParcelAuthorization_HeldBySelf_Allowed()
        {
            var c = new Chain();
            Assert.IsNull(c.Catalog.RegisterDesign(CabinDesign("cabin-a"), c.Diag));
            ConstructionProject project = c.Executor.CreateProject(c.Catalog, 7, "cabin-a",
                "parcel-9", c.Parcels, 10, c.Diag);
            Assert.IsNotNull(project, string.Join("; ", c.Diag));
        }

        #endregion

        #region Gate proofs: no materials / no labor / no equipment → no advance

        [Test]
        public void NoMaterialsStaged_LaborAndAdvanceRefused()
        {
            var c = new Chain();
            NpcConstructionRecord record;
            ConstructionProject project = c.CommissionCabin(out record);
            WorkPackage foundation = project.WorkPackages[0];

            // No materials staged: labor has nothing to work on.
            string laborRefusal = c.Executor.CommitLabor(project, foundation, 5, c.Tracker,
                c.Custody, c.Tasks, c.Budgets, 11, 360, 240, "build", c.Diag);
            Assert.IsNotNull(laborRefusal);
            StringAssert.Contains("stage materials before labor", laborRefusal);

            string advanceRefusal = c.Executor.AdvancePackage(project, foundation, 11, c.Diag);
            Assert.IsNotNull(advanceRefusal);
            Assert.AreNotEqual(WorkPackageStatus.Complete, foundation.Status);
        }

        [Test]
        public void StagedButNoLaborMinutes_AdvanceRefused()
        {
            var c = new Chain();
            NpcConstructionRecord record;
            ConstructionProject project = c.CommissionCabin(out record);
            WorkPackage foundation = project.WorkPackages[0];
            Assert.IsNull(c.Executor.StagePackageMaterials(project, foundation, c.Source, 11, c.Diag));

            // Explicit zero-labor verdict: a package with 0 committed minutes
            // NEVER advances, no matter how the test got there.
            Assert.AreEqual(0, foundation.LaborMinutesCommitted);
            string refusal = c.Executor.AdvancePackage(project, foundation, 11, c.Diag);
            Assert.IsNotNull(refusal);
            StringAssert.Contains("0 of 240 labor minutes", refusal);
            Assert.AreNotEqual(WorkPackageStatus.Complete, foundation.Status);
        }

        [Test]
        public void MissingEquipment_LaborRefused_ProjectDoesNotAdvance()
        {
            var c = new Chain();
            c.Executor.EquipmentReservations = c.Equipment;
            NpcConstructionRecord record;
            ConstructionProject project = c.CommissionCabin(out record);
            WorkPackage foundation = project.WorkPackages[0];
            Assert.IsNull(c.Executor.StagePackageMaterials(project, foundation, c.Source, 11, c.Diag));

            // Another project holds the shovel — this project's labor is refused.
            Assert.IsNull(c.Equipment.Reserve(
                ConstructionProjectExecutor.EquipmentKeyFor(7, "shovel"),
                "project:other/package:wpkg-x", "other project holds it"));

            string refusal = c.Executor.CommitLabor(project, foundation, 5, c.Tracker,
                c.Custody, c.Tasks, c.Budgets, 11, 360, 240, "build", c.Diag);
            Assert.IsNotNull(refusal);
            StringAssert.Contains("equipment", refusal.ToLowerInvariant());
            Assert.AreEqual(0, foundation.LaborMinutesCommitted);

            string advanceRefusal = c.Executor.AdvancePackage(project, foundation, 11, c.Diag);
            Assert.IsNotNull(advanceRefusal, "without equipment there is no labor, and without labor there is no advance.");
        }

        [Test]
        public void EquipmentHold_ReleasedOnPackageComplete()
        {
            var c = new Chain();
            c.Executor.EquipmentReservations = c.Equipment;
            NpcConstructionRecord record;
            ConstructionProject project = c.CommissionCabin(out record);
            WorkPackage foundation = project.WorkPackages[0];
            Assert.IsNull(c.Executor.StagePackageMaterials(project, foundation, c.Source, 11, c.Diag));
            Assert.IsNull(c.Executor.CommitLabor(project, foundation, 5, c.Tracker,
                c.Custody, c.Tasks, c.Budgets, 11, 360, 240, "build", c.Diag));
            string taskId = project.LaborTaskIds[project.LaborTaskIds.Count - 1];
            Assert.IsNull(c.Executor.WorkCommittedLabor(project, foundation, taskId, 240,
                c.Tasks, c.Budgets, 11, c.Diag));

            string key = ConstructionProjectExecutor.EquipmentKeyFor(7, "shovel");
            Assert.IsTrue(c.Equipment.IsReserved(key), "the shovel is held while the package is worked.");
            Assert.IsNull(c.Executor.AdvancePackage(project, foundation, 11, c.Diag));

            Assert.IsFalse(c.Equipment.IsReserved(key), "completion releases the equipment hold.");
            // And a second project can now take it.
            Assert.IsNull(c.Equipment.Reserve(key, "project:next/package:wpkg-y", "next project"));
        }

        #endregion

        #region Material conservation

        [Test]
        public void MaterialsConsumedExactlyOnce_NoDoubleStaging()
        {
            var c = new Chain();
            NpcConstructionRecord record;
            ConstructionProject project = c.CommissionCabin(out record);
            WorkPackage foundation = project.WorkPackages[0];

            Assert.IsNull(c.Executor.StagePackageMaterials(project, foundation, c.Source, 11, c.Diag));
            Assert.AreEqual(960, c.Source.Stock["fieldstone"]);

            // Staging twice is refused — the same lots are never consumed twice.
            string second = c.Executor.StagePackageMaterials(project, foundation, c.Source, 11, c.Diag);
            Assert.IsNotNull(second);
            StringAssert.Contains("stage once", second);
            Assert.AreEqual(960, c.Source.Stock["fieldstone"]);
            Assert.AreEqual(1, project.MaterialsConsumed.Count);
            Assert.AreEqual(1, foundation.StagedMaterialLines.Count);
        }

        [Test]
        public void StagedMaterials_SurviveSaveLoad_WithoutDuplication()
        {
            var c = new Chain();
            NpcConstructionRecord record;
            ConstructionProject project = c.CommissionCabin(out record);
            WorkPackage foundation = project.WorkPackages[0];
            Assert.IsNull(c.Executor.StagePackageMaterials(project, foundation, c.Source, 11, c.Diag));
            string provenanceBefore = foundation.StagedMaterialLines[0].Provenance;

            var dto = c.Executor.CaptureSaveDto(new List<ConstructionProject> { project });
            List<ConstructionProject> loaded = c.Executor.LoadFromSaveDto(dto, c.Diag);
            // Loading the same snapshot again must not duplicate anything.
            List<ConstructionProject> loadedTwice = c.Executor.LoadFromSaveDto(dto, c.Diag);

            Assert.AreEqual(1, loaded.Count);
            Assert.AreEqual(1, loadedTwice.Count);
            WorkPackage restored = loaded[0].FindPackage(foundation.PackageId);
            Assert.IsNotNull(restored);
            Assert.AreEqual(WorkPackageStatus.MaterialsStaged, restored.Status);
            Assert.AreEqual(1, restored.StagedMaterialLines.Count);
            Assert.AreEqual(provenanceBefore, restored.StagedMaterialLines[0].Provenance);
            Assert.AreEqual(1, loaded[0].MaterialsConsumed.Count);

            // The restored package cannot stage again — no double consumption after load.
            string restage = c.Executor.StagePackageMaterials(loaded[0], restored, c.Source, 12, c.Diag);
            Assert.IsNotNull(restage);
            Assert.AreEqual(960, c.Source.Stock["fieldstone"]);
        }

        [Test]
        public void SiteStockpile_SaveLoad_NoDuplicatedUnits()
        {
            var stockpile = new ConstructionSiteStockpile { ProjectId = "cproj-1" };
            var sale = new ConstructionMaterialSale { SaleId = "sale-0", ProjectId = "cproj-1" };
            sale.Lines.Add(new ConstructionMaterialSaleLine
            {
                DisplayName = "lumber", Units = 600, UnitLabel = "board feet",
                Provenance = "yard lot L-1",
            });
            var diag = new List<string>();
            Assert.IsNull(stockpile.Receive(sale, diag));
            Assert.AreEqual(600, stockpile.AvailableUnits("lumber"));

            var dto = stockpile.CaptureSaveDto();
            var restored = new ConstructionSiteStockpile();
            restored.LoadFromSaveDto(dto, diag);
            // Loading twice must not duplicate units.
            restored.LoadFromSaveDto(dto, diag);
            Assert.AreEqual(600, restored.AvailableUnits("lumber"));
            Assert.AreEqual(1, restored.Lines.Count);
        }

        #endregion

        #region Labor integrity

        [Test]
        public void LaborTaskBinding_MismatchedTask_Refused()
        {
            var c = new Chain();
            NpcConstructionRecord record;
            ConstructionProject project = c.CommissionCabin(out record);
            WorkPackage foundation = project.WorkPackages[0];
            WorkPackage framing = project.WorkPackages[1];
            Assert.IsNull(c.Executor.StagePackageMaterials(project, foundation, c.Source, 11, c.Diag));
            Assert.IsNull(c.Executor.StagePackageMaterials(project, framing, c.Source, 11, c.Diag));
            Assert.IsNull(c.Executor.CommitLabor(project, foundation, 5, c.Tracker,
                c.Custody, c.Tasks, c.Budgets, 11, 360, 240, "build", c.Diag));
            Assert.IsNull(c.Executor.CommitLabor(project, framing, 6, c.Tracker,
                c.Custody, c.Tasks, c.Budgets, 11, 360, 480, "build", c.Diag));
            string foundationTask = project.LaborTaskIds[0];

            // Minutes worked on the FOUNDATION task must not credit FRAMING.
            string refusal = c.Executor.WorkCommittedLabor(project, framing, foundationTask, 240,
                c.Tasks, c.Budgets, 11, c.Diag);
            Assert.IsNotNull(refusal);
            StringAssert.Contains("not bound", refusal);
            Assert.AreEqual(0, framing.LaborMinutesCommitted);
        }

        [Test]
        public void WorkerTimeExclusive_BuildLaborVsFreightCrew()
        {
            var c = new Chain();
            NpcConstructionRecord record;
            ConstructionProject project = c.CommissionCabin(out record);
            WorkPackage foundation = project.WorkPackages[0];
            Assert.IsNull(c.Executor.StagePackageMaterials(project, foundation, c.Source, 11, c.Diag));

            // P5 works the build 6:00-10:00 on day 11.
            Assert.IsNull(c.Executor.CommitLabor(project, foundation, 5, c.Tracker,
                c.Custody, c.Tasks, c.Budgets, 11, 360, 240, "build", c.Diag));

            // The same person cannot simultaneously load the freight wagon.
            var coordinator = new ConstructionMaterialFreightCoordinator();
            var sale = new ConstructionMaterialSale
            {
                SaleId = "sale-x", SupplierBusinessId = "lumber-yard",
                ProjectId = project.ProjectId, PackageId = foundation.PackageId, DayIndex = 11,
            };
            sale.Lines.Add(new ConstructionMaterialSaleLine
            {
                DisplayName = "lumber", Units = 600, UnitLabel = "board feet",
                UnitPriceCents = 40, Provenance = "yard lot L-9",
            });
            var stockpile = new ConstructionSiteStockpile { ProjectId = project.ProjectId };
            var plan = c.FreightPlan(c.Wagon("wagon-1", 100000f, 100f),
                "site:" + project.ProjectId, 41, 5, 42);
            plan.Tracker = c.Tracker; // shared schedule: P5 is already building.

            string refusal = coordinator.FreightSaleToSite(sale, plan, stockpile, 11, c.Diag);
            Assert.IsNotNull(refusal);
            StringAssert.Contains("double-booked", refusal);
            Assert.AreEqual(0, stockpile.AvailableUnits("lumber"));
        }

        #endregion

        #region Supplier discovery and procurement

        [Test]
        public void SupplierDiscovery_UnnamedRequirement_DiscoversRealSupplier()
        {
            var c = new Chain();
            var requirement = Material("lumber", 600, "board feet", ConstructionMaterialKind.Lumber);
            string supplier = c.Procurement.DiscoverSupplier(requirement, c.Directory, c.Diag);
            Assert.AreEqual("lumber-yard", supplier);
            Assert.AreEqual("lumber-yard", requirement.SupplierBusinessId);
        }

        [Test]
        public void SupplierDiscovery_NoSupplier_Refused()
        {
            var c = new Chain();
            c.Directory = new SupplierDirectory(); // nobody registered
            var requirement = Material("lumber", 600, "board feet", ConstructionMaterialKind.Lumber);
            string supplier = c.Procurement.DiscoverSupplier(requirement, c.Directory, c.Diag);
            Assert.IsNull(supplier);
        }

        [Test]
        public void SupplierDiscovery_NamedPhantomSupplier_Refused()
        {
            var c = new Chain();
            var requirement = Material("lumber", 600, "board feet", ConstructionMaterialKind.Lumber,
                "phantom-yard");
            string supplier = c.Procurement.DiscoverSupplier(requirement, c.Directory, c.Diag);
            Assert.IsNull(supplier, "a named supplier that is not discoverable must not be used.");
        }

        [Test]
        public void ProcurementPaysRealCash_ExactlyOnce_EndToEnd()
        {
            var c = new Chain();
            HouseholdLedger ledger = c.RegisterHousehold(7, 100000);
            PurseCashStore yard = c.RegisterPurse("business:lumber-yard", 0);

            NpcConstructionRecord record;
            ConstructionProject project = c.CommissionCabin(out record);
            WorkPackage foundation = project.WorkPackages[0];
            var stockpile = new ConstructionSiteStockpile { ProjectId = project.ProjectId };
            var plan = c.FreightPlan(c.Wagon("wagon-1", 100000f, 100f),
                "site:" + project.ProjectId, 41, 42, 43);

            // Full chain through the commissioning principal: discovery →
            // sale → cash → freight → stockpile → stage, with the overrun
            // gate and the funding record kept honestly.
            Assert.IsNull(c.Commissioner.ProcureAndStagePackage(
                c.Executor, project, record, foundation, c.Procurement,
                c.SalePort, c.Prices, c.Directory, c.FreightCoordinator, plan, stockpile,
                c.Cash, "household:7", c.Obligations, c.Ids, 11, c.Diag),
                string.Join("; ", c.Diag));
            Assert.AreEqual(WorkPackageStatus.MaterialsStaged, foundation.Status);
            Assert.AreEqual(2000, record.FindFunding(foundation.PackageId).ActualMaterialCostCents);

            // Conservation: the household paid exactly the sale; the supplier
            // received it; the supplier's lots went down exactly once.
            Assert.AreEqual(1, c.SalePort.Sales.Count);
            int saleTotal = c.SalePort.Sales[0].TotalCents; // 40 x 50 = 2000c
            Assert.AreEqual(2000, saleTotal);
            Assert.AreEqual(100000 - 2000, ledger.GetBalanceCents());
            Assert.AreEqual(2000, yard.ReadBalanceCents());
            Assert.AreEqual(960, c.SalePort.Stock["fieldstone"]);
            Assert.IsFalse(string.IsNullOrWhiteSpace(foundation.StagedMaterialLines[0].Provenance));
            StringAssert.Contains("yard lot", foundation.StagedMaterialLines[0].Provenance);
            Assert.AreEqual(0, stockpile.AvailableUnits("fieldstone"),
                "staging consumed the stockpile — nothing left behind, nothing duplicated.");
        }

        [Test]
        public void FreightChain_OverCapacity_BlockedPreLoad_NothingFreighted()
        {
            var c = new Chain();
            c.RegisterHousehold(7, 100000);
            c.RegisterPurse("business:lumber-yard", 0);
            NpcConstructionRecord record;
            ConstructionProject project = c.CommissionCabin(out record);
            WorkPackage framing = project.WorkPackages[1]; // 600 units of lumber
            var stockpile = new ConstructionSiteStockpile { ProjectId = project.ProjectId };

            // A toy wagon that cannot carry 600 units (600kg at 1kg/unit).
            var plan = c.FreightPlan(c.Wagon("wagon-tiny", 10f, 100f),
                "site:" + project.ProjectId, 41, 42, 43);

            string problem = RunMaterialLeg(c, project, framing, stockpile, plan, 11);
            Assert.IsNotNull(problem);
            StringAssert.Contains("blocked pre-load", problem);
            // The sale stands (real purchase, buyer's goods at the yard) but
            // NOTHING reached the site and the package cannot stage.
            Assert.AreEqual(1, c.SalePort.Sales.Count);
            Assert.AreEqual(0, stockpile.AvailableUnits("lumber"));
            Assert.AreEqual(WorkPackageStatus.Ready, framing.Status);
        }

        #endregion

        #region Cancellation

        [Test]
        public void Cancellation_ReturnsStagedMaterials_ExactlyOnce()
        {
            var c = new Chain();
            NpcConstructionRecord record;
            ConstructionProject project = c.CommissionCabin(out record);
            WorkPackage foundation = project.WorkPackages[0];
            Assert.IsNull(c.Executor.StagePackageMaterials(project, foundation, c.Source, 11, c.Diag));
            Assert.AreEqual(960, c.Source.Stock["fieldstone"]);

            Assert.IsNull(c.Executor.CancelProject(project, null, c.Restock, c.Tracker,
                c.Tasks, c.Budgets, 12, c.Diag), string.Join("; ", c.Diag));
            Assert.AreEqual(ConstructionProjectStatus.Cancelled, project.Status);

            // Staged lines returned exactly once.
            Assert.AreEqual(40, c.Restock.Returned["fieldstone"]);
            Assert.AreEqual(1, c.Restock.ReturnLog.Count);
            Assert.IsTrue(foundation.StagedMaterialsReturned);

            // Cancelling again is refused — no double return.
            string second = c.Executor.CancelProject(project, null, c.Restock, c.Tracker,
                c.Tasks, c.Budgets, 13, c.Diag);
            Assert.IsNotNull(second);
            StringAssert.Contains("already cancelled", second);
            Assert.AreEqual(40, c.Restock.Returned["fieldstone"]);
            Assert.AreEqual(1, c.Restock.ReturnLog.Count);
        }

        [Test]
        public void Cancellation_WithoutRestock_Refused_NoLoss()
        {
            var c = new Chain();
            NpcConstructionRecord record;
            ConstructionProject project = c.CommissionCabin(out record);
            WorkPackage foundation = project.WorkPackages[0];
            var stockpile = new ConstructionSiteStockpile { ProjectId = project.ProjectId };
            Assert.IsNull(c.Executor.StagePackageMaterials(project, foundation, c.Source, 11, c.Diag));

            // Refused: staged lines cannot be accounted for without a restock port.
            string refusal = c.Executor.CancelProject(project, stockpile, null, c.Tracker,
                c.Tasks, c.Budgets, 12, c.Diag);
            Assert.IsNotNull(refusal);
            StringAssert.Contains("restock", refusal);
            Assert.AreNotEqual(ConstructionProjectStatus.Cancelled, project.Status);
            Assert.AreEqual(WorkPackageStatus.MaterialsStaged, foundation.Status,
                "the refusal leaves the project exactly where it was.");
            Assert.AreEqual(960, c.Source.Stock["fieldstone"]);
        }

        [Test]
        public void Cancellation_ReleasesLaborAndEquipment()
        {
            var c = new Chain();
            c.Executor.EquipmentReservations = c.Equipment;
            NpcConstructionRecord record;
            ConstructionProject project = c.CommissionCabin(out record);
            WorkPackage foundation = project.WorkPackages[0];
            Assert.IsNull(c.Executor.StagePackageMaterials(project, foundation, c.Source, 11, c.Diag));
            Assert.IsNull(c.Executor.CommitLabor(project, foundation, 5, c.Tracker,
                c.Custody, c.Tasks, c.Budgets, 11, 360, 240, "build", c.Diag));
            string key = ConstructionProjectExecutor.EquipmentKeyFor(7, "shovel");
            Assert.IsTrue(c.Equipment.IsReserved(key));
            Assert.AreEqual(1, project.LaborReservationIds.Count);

            Assert.IsNull(c.Executor.CancelProject(project, null, c.Restock, c.Tracker,
                c.Tasks, c.Budgets, 12, c.Diag), string.Join("; ", c.Diag));

            Assert.IsFalse(c.Equipment.IsReserved(key), "equipment holds release on cancellation.");
            Assert.IsTrue(c.Tracker.IsFree(5, 11, 360, 240), "the worker's time is freed on cancellation.");
            // The open labor task was failed honestly through the task authority.
            WorkTask task = c.Tasks.FindTask(project.LaborTaskIds[0]);
            Assert.IsNotNull(task);
            Assert.AreEqual(TaskStatus.Failed, task.Status);
        }

        [Test]
        public void Cancellation_CompletedProject_Refused()
        {
            var c = new Chain();
            NpcConstructionRecord record;
            ConstructionProject project = c.CommissionCabin(out record);
            // Drive the project to completion through the real chain —
            // one package per day so the worker's person-time never overlaps.
            for (int i = 0; i < project.WorkPackages.Count; i++)
            {
                int day = 11 + i;
                WorkPackage package = project.WorkPackages[i];
                Assert.IsNull(c.Executor.StagePackageMaterials(project, package, c.Source, day, c.Diag));
                Assert.IsNull(c.Executor.CommitLabor(project, package, 5, c.Tracker,
                    c.Custody, c.Tasks, c.Budgets, day, 360, package.LaborMinutesRequired, "build", c.Diag));
                string taskId = project.LaborTaskIds[project.LaborTaskIds.Count - 1];
                Assert.IsNull(c.Executor.WorkCommittedLabor(project, package, taskId,
                    package.LaborMinutesRequired, c.Tasks, c.Budgets, day, c.Diag));
                Assert.IsNull(c.Executor.AdvancePackage(project, package, day, c.Diag));
            }
            Assert.IsNull(c.Executor.CompleteProject(project, c.Catalog.FindDesign("cabin-a"),
                c.Housing, 12, c.Diag));

            string refusal = c.Executor.CancelProject(project, null, c.Restock, c.Tracker,
                c.Tasks, c.Budgets, 13, c.Diag);
            Assert.IsNotNull(refusal);
            StringAssert.Contains("demolition", refusal);
            Assert.AreEqual(ConstructionProjectStatus.Complete, project.Status);
        }

        #endregion

        #region Completion → usable spaces

        [Test]
        public void Completion_RegistersUsableSpace_ThroughHousingAuthority()
        {
            var c = new Chain();
            NpcConstructionRecord record;
            // Parcel-authorized creation: the household holds parcel-9.
            Assert.IsNull(c.Catalog.RegisterDesign(CabinDesign("cabin-a"), c.Diag));
            ConstructionProject project = c.Executor.CreateProject(c.Catalog, 7, "cabin-a",
                "parcel-9", c.Parcels, 10, c.Diag);
            Assert.IsNotNull(project, string.Join("; ", c.Diag));
            Assert.IsNull(c.Executor.CreateWorkPackages(project, c.Catalog.FindDesign("cabin-a"), 10, c.Diag));

            for (int i = 0; i < project.WorkPackages.Count; i++)
            {
                int day = 11 + i;
                WorkPackage package = project.WorkPackages[i];
                Assert.IsNull(c.Executor.StagePackageMaterials(project, package, c.Source, day, c.Diag));
                Assert.IsNull(c.Executor.CommitLabor(project, package, 5, c.Tracker,
                    c.Custody, c.Tasks, c.Budgets, day, 360, package.LaborMinutesRequired, "build", c.Diag));
                string taskId = project.LaborTaskIds[project.LaborTaskIds.Count - 1];
                Assert.IsNull(c.Executor.WorkCommittedLabor(project, package, taskId,
                    package.LaborMinutesRequired, c.Tasks, c.Budgets, day, c.Diag));
                Assert.IsNull(c.Executor.AdvancePackage(project, package, day, c.Diag));
            }
            Assert.IsNull(c.Executor.CompleteProject(project, c.Catalog.FindDesign("cabin-a"),
                c.Housing, 12, c.Diag));
            Assert.AreEqual(ConstructionProjectStatus.Complete, project.Status);
            Assert.IsFalse(string.IsNullOrWhiteSpace(project.BuildingId));

            // The housing worker's consumption boundary: a registered
            // building with real accommodation spaces.
            List<AccommodationSpace> spaces = c.Housing.SpacesForBuilding(project.BuildingId);
            Assert.AreEqual(1, spaces.Count);
            Assert.AreEqual(4, spaces[0].SleepingCapacity);
            Assert.AreEqual(project.BuildingId, spaces[0].BuildingId);

            Assert.IsNull(c.Executor.OccupyCompletedHome(project, c.Housing,
                new List<int> { 5, 6 }, AccommodationArrangement.OwnerOccupied, 13, c.Diag));
            Assert.AreEqual(AccommodationArrangement.OwnerOccupied,
                c.Housing.CurrentOccupanciesForPerson(5)[0].Arrangement);
        }

        #endregion
    }
}
