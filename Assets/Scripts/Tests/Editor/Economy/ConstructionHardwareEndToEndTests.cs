using System;
using System.Collections.Generic;
using LandLedgers.Economy;
using LandLedgers.Economy.Blacksmith;
using LandLedgers.Economy.Businesses.LumberYard;
using LandLedgers.Economy.Equipment;
using LandLedgers.Economy.Financing;
using LandLedgers.Economy.Logistics;
using LandLedgers.Economy.Trade;
using LandLedgers.Orchestration.Systems;
using LandLedgers.Population;
using LandLedgers.Primitives;
using LandLedgers.Skills;
using LandLedgers.Tasks;
using LandLedgers.Time;
using LandLedgers.World.Property;
using NUnit.Framework;

namespace LandLedgers.EditorTests.Economy
{
    /// <summary>
    /// D2 (brief end-to-end scenario 4): the smith makes hardware, the yard
    /// buys it into real stock, the household buys it from the yard through
    /// the real sale adapter, the freight coordinator carries it to the
    /// site stockpile, the roof package stages it from the stockpile, and
    /// the carpenter installs it with real labour — a full settler cabin
    /// built with real door hardware at every link. A second test proves
    /// the negative: with the yard's hardware shelves empty, the roof
    /// package cannot be procured, no door appears from a completion
    /// percentage, and nothing is invented to fill the gap.
    /// </summary>
    [TestFixture]
    public sealed class ConstructionHardwareEndToEndTests
    {
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
            public SupplierDirectory Directory = new SupplierDirectory();
            public PersonScheduleTracker Tracker = new PersonScheduleTracker();
            public FakeToolCustody Custody = new FakeToolCustody();
            public TaskAuthority Tasks = new TaskAuthority();
            public WorkTimeBudgetStore Budgets = new WorkTimeBudgetStore();
            public HousingAuthority Housing = new HousingAuthority();
            public CreditCashBridge Cash = new CreditCashBridge();
            public FinancialObligationAuthority Obligations = new FinancialObligationAuthority();
            public EntityIdRegistry Ids = new EntityIdRegistry();
            public LumberYardShopRuntime Yard;
            public LumberYardConstructionPricePort Prices;
            public LumberYardConstructionSaleAdapter SalePort;
            public HouseholdLedger HouseholdLedger;
            public PurseCashStore YardPurse;

            public Chain()
            {
                Yard = new LumberYardShopRuntime("lumber-yard", Ids);
                Prices = new LumberYardConstructionPricePort(Yard);
                SalePort = new LumberYardConstructionSaleAdapter(Yard);
                Custody.Grant(7, "saw");
                var yard = new FakeGoodsSupplier { BusinessId = "lumber-yard" };
                yard.Categories.Add("construction-lumber");
                yard.Categories.Add("construction-hardware");
                yard.Categories.Add("construction-materials");
                Directory.Register(yard);

                HouseholdLedger = new HouseholdLedger(7);
                Assert.IsNull(HouseholdLedger.RecordInflow(0, 100000, HouseholdIncomeSource.OwnerContribution,
                    "test-seed", "test seed capital", "test"));
                Assert.IsNull(Cash.Register("household:7", new HouseholdCashStore(7, HouseholdLedger)));
                YardPurse = new PurseCashStore("business:lumber-yard", 0, "test seed capital");
                Assert.IsNull(Cash.Register("business:lumber-yard", YardPurse));
            }

            /// <summary>Stocks the yard the way the real chain stocks it: lumber bought at 32c (retails at 40c).</summary>
            public void StockLumber(int units)
            {
                Assert.IsNull(Yard.ReceiveLumberPurchase(units, "pine", "merchantable", "sawmill-1",
                    "sawmill manifest", 10, 32, Diag), string.Join("; ", Diag));
            }

            /// <summary>
            /// The smith forges the hardware the cabin needs and the yard takes
            /// it into real stock — the D2 supply leg, driven here the way a
            /// delivery driver will drive it in production.
            /// </summary>
            public void StockHardwareFromSmith(int hinges, int latches, int handles, int dayIndex)
            {
                var smithy = new BlacksmithRuntime("smith-1", "Test Smithy", true);
                smithy.ReceiveMaterial(new ImportLot
                {
                    LotId = "IMP-TEST-1", MaterialId = ImportCatalog.IronStockId,
                    MaterialName = "Iron stock", Units = 100,
                    OriginName = "Pittsburgh ironworks, via railhead", OrderId = "IMP-ORD-1",
                }, Diag);
                smithy.ReceiveMaterial(new ImportLot
                {
                    LotId = "IMP-TEST-2", MaterialId = ImportCatalog.ForgeCoalId,
                    MaterialName = "Forge coal", Units = 50,
                    OriginName = "Off-map coal dealer", OrderId = "IMP-ORD-2",
                }, Diag);
                EntityId smith = EntityId.For(EntityKind.Person, 1);
                var skills = new SkillService();
                BlacksmithRuntime.RegisterSkills(skills, Diag);
                var pieces = new List<EquipmentAsset>();
                for (int i = 0; i < hinges; i++)
                    pieces.Add(smithy.ForgeHardware(BlacksmithRuntime.DoorHingeKind, smith, dayIndex, skills, Diag, out _));
                for (int i = 0; i < latches; i++)
                    pieces.Add(smithy.ForgeHardware(BlacksmithRuntime.IronLatchKind, smith, dayIndex, skills, Diag, out _));
                for (int i = 0; i < handles; i++)
                    pieces.Add(smithy.ForgeHardware(BlacksmithRuntime.DoorHandleKind, smith, dayIndex, skills, Diag, out _));
                foreach (EquipmentAsset piece in pieces)
                    Assert.IsNotNull(piece, string.Join("; ", Diag));
                foreach (EquipmentAsset piece in pieces)
                    Assert.IsNotNull(smithy.TakeHardware(piece.AssetId, Diag), string.Join("; ", Diag));

                int total = hinges + latches + handles;
                Assert.IsNull(Yard.ReceiveHardwareFromBlacksmith(
                    LumberYardHardwareKind.SimpleHardware, total, "smith-1", dayIndex, "IMP-ORD-1", dayIndex, 45, Diag),
                    string.Join("; ", Diag));
            }

            public ConstructionProject CommissionSettlerCabin(out NpcConstructionRecord record)
            {
                Assert.IsNull(Catalog.RegisterDesign(ProductionDesignPresets.SettlerCabin(), Diag),
                    string.Join("; ", Diag));
                ConstructionProject project = Commissioner.CommissionProject(Executor, Catalog,
                    7, "settler-cabin", "parcel-9", Prices, 10, Diag, out record);
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

            /// <summary>Runs one package through the real chain: sale, freight, stockpile, stage.</summary>
            public string RunMaterialLeg(ConstructionProject project, WorkPackage package,
                ConstructionSiteStockpile stockpile, ConstructionFreightPlan plan, int day)
            {
                int priced = 0;
                foreach (ConstructionMaterialRequirement requirement in package.Materials)
                {
                    int unitPrice = Prices.UnitPriceCents(requirement);
                    Assert.GreaterOrEqual(unitPrice, 0, $"a real price exists for '{requirement.MaterialDisplayName}'");
                    priced += unitPrice * requirement.RequiredUnits;
                }
                ConstructionMaterialSale sale;
                return Procurement.ProcureFreightAndStage(
                    Executor, project, package, SalePort, Prices, Directory,
                    FreightCoordinator, plan, stockpile,
                    Cash, "household:7", Obligations, Ids,
                    priced, day, Diag, out sale);
            }

            /// <summary>Stages one package from the site stockpile, works it with real labour, and advances it. E2: labour is total person-minutes and one worker-day is 600 minutes, so a recalibrated package takes several days of real work. Commit and work one day at a time until the package is fully worked; the day cursor advances past every worked day so the next package never double-books the worker.</summary>
            public void DrivePackageToComplete(ConstructionProject project, WorkPackage package,
                ConstructionSiteStockpile stockpile, ConstructionFreightPlan plan, ref int day)
            {
                Assert.IsNull(RunMaterialLeg(project, package, stockpile, plan, day),
                    $"package '{package.PackageId}' procures, freights and stages: " + string.Join("; ", Diag));
                Assert.AreEqual(WorkPackageStatus.MaterialsStaged, package.Status);
                int lastWorkedDay = day;
                while (package.LaborMinutesCommitted < package.LaborMinutesRequired)
                {
                    Assert.IsNull(Executor.CommitLabor(project, package, 5, Tracker,
                        Custody, Tasks, Budgets, day, 360, package.LaborMinutesRequired, "build", Diag),
                        string.Join("; ", Diag));
                    string taskId = project.LaborTaskIds[project.LaborTaskIds.Count - 1];
                    Assert.IsNull(Executor.WorkCommittedLabor(project, package, taskId,
                        package.LaborMinutesRequired, Tasks, Budgets, day, Diag), string.Join("; ", Diag));
                    lastWorkedDay = day;
                    day++;
                }
                Assert.IsNull(Executor.AdvancePackage(project, package, lastWorkedDay, Diag), string.Join("; ", Diag));
                Assert.AreEqual(WorkPackageStatus.Complete, package.Status);
            }
        }

        // Scenario 4 end to end: smith makes hardware, the household buys it,
        // freight carries it, the site receives it, and the roof package
        // installs it — a full cabin with real doors hung on real iron.
        [Test]
        public void Scenario4_SmithHardware_BuildsSettlerCabinEndToEnd()
        {
            var c = new Chain();
            // The cabin needs 9 door-hardware units: 1 door (4 pieces + 2
            // nails) + 1 window (2 pieces + 1 nail). The smith forges the 6
            // pieces; the 3 nails units arrive on the import order the yard
            // already registers (LumberYardSupply, 30c).
            c.StockHardwareFromSmith(hinges: 2, latches: 2, handles: 2, dayIndex: 10);
            // The nails arrive a day later, so the smith delivery stays first
            // in the yard's FIFO lot order and prices the hardware at 45c.
            Assert.IsNull(c.Yard.ReceiveHardwareImportArrival(
                LumberYardHardwareKind.Nails, 3, "IMP-ORD-N", "Pittsburgh ironworks, via railhead", 11, 30, c.Diag),
                string.Join("; ", c.Diag));
            c.StockLumber(640);
            Assert.AreEqual(9, c.Yard.HardwareStock.TotalHardwareUnits, "6 forged pieces + 3 imported nails");
            Assert.AreEqual(45, c.Prices.UnitPriceCents(new ConstructionMaterialRequirement
            {
                RequirementId = "REQ-check", MaterialKind = ConstructionMaterialKind.Hardware,
                MaterialDisplayName = "door hardware", RequiredUnits = 1, UnitLabel = "units",
                ProvenanceKind = ConstructionMaterialProvenanceKind.AnyRealSource,
                SupplierBusinessId = "lumber-yard", SupplierDisplayName = "Lumber Yard",
            }), "hardware prices from the first stocked lot (the smith delivery, 45c)");

            NpcConstructionRecord record;
            ConstructionProject project = c.CommissionSettlerCabin(out record);
            Assert.AreEqual(3, project.WorkPackages.Count);
            var stockpile = new ConstructionSiteStockpile();
            var plan = c.FreightPlan(c.Wagon("wagon-1", 100000f, 100f), "parcel-9 site", 5, 6, 7);

            int day = 11;
            c.DrivePackageToComplete(project, project.WorkPackages[0], stockpile, plan, ref day);
            c.DrivePackageToComplete(project, project.WorkPackages[1], stockpile, plan, ref day);
            // The roof package is the doors/openings phase: 300 lumber + 9 hardware.
            WorkPackage roof = project.WorkPackages[2];
            Assert.AreEqual(2, roof.Materials.Count);
            Assert.AreEqual(9, roof.Materials[1].RequiredUnits);
            c.DrivePackageToComplete(project, roof, stockpile, plan, ref day);

            Assert.IsNull(c.Executor.CompleteProject(project, ProductionDesignPresets.SettlerCabin(), c.Housing, day, c.Diag),
                string.Join("; ", c.Diag));
            Assert.AreEqual(ConstructionProjectStatus.Complete, project.Status);
            Assert.IsFalse(string.IsNullOrWhiteSpace(project.BuildingId));
            Assert.AreEqual(1, c.Housing.BuildingsForParcel("parcel-9").Count, "the finished cabin registers on the parcel");

            // Conservation, end to end: 640 lumber + 9 hardware leave the yard,
            // the household pays exactly the priced total, and nothing is left
            // stranded at the site.
            Assert.AreEqual(0, c.Yard.HardwareStock.TotalHardwareUnits, "all 9 hardware units were sold and installed");
            Assert.AreEqual(100000 - 26005, c.HouseholdLedger.GetBalanceCents(), "640 lumber at 40c + 9 hardware at 45c");
            Assert.AreEqual(26005, c.YardPurse.ReadBalanceCents());
            Assert.AreEqual(4, project.MaterialsConsumed.Count, "sills + walls + roof lumber lines, plus the roof hardware line");
            ConsumedMaterialLine hardwareLine = null;
            foreach (ConsumedMaterialLine line in project.MaterialsConsumed)
                if (line != null && string.Equals(line.DisplayName, "door hardware", StringComparison.Ordinal))
                    hardwareLine = line;
            Assert.IsNotNull(hardwareLine, "the roof's door-hardware consumption is recorded");
            Assert.AreEqual(9, hardwareLine.Units);
            Assert.IsFalse(string.IsNullOrWhiteSpace(hardwareLine.Provenance), "the hardware line carries its lot provenance");
        }

        // The negative: with the yard's hardware shelves empty, the roof
        // package cannot be procured — no door appears from a completion
        // percentage, the sills and walls still build, and the household
        // is never charged for hardware that does not exist.
        [Test]
        public void Scenario4_EmptyYardHardware_RoofRefuses_NoDoorWithoutHardware()
        {
            var c = new Chain();
            c.StockLumber(640);
            Assert.AreEqual(0, c.Yard.HardwareStock.TotalHardwareUnits);

            NpcConstructionRecord record;
            ConstructionProject project = c.CommissionSettlerCabin(out record);
            var stockpile = new ConstructionSiteStockpile();
            var plan = c.FreightPlan(c.Wagon("wagon-1", 100000f, 100f), "parcel-9 site", 5, 6, 7);

            // Sills and walls build: they need lumber only.
            int day = 11;
            c.DrivePackageToComplete(project, project.WorkPackages[0], stockpile, plan, ref day);
            c.DrivePackageToComplete(project, project.WorkPackages[1], stockpile, plan, ref day);

            // The roof package cannot even be priced — the price port reads
            // the yard's real lots and there are none.
            WorkPackage roof = project.WorkPackages[2];
            Assert.AreEqual(-1, c.Prices.UnitPriceCents(roof.Materials[1]),
                "no hardware lot, no price — the cost is unknowable, never guessed");
            // Procurement itself refuses through the real sale adapter (the
            // priced total is unknowable, so it is not computed first).
            ConstructionMaterialSale noSale;
            string refusal = c.Procurement.ProcureFreightAndStage(
                c.Executor, project, roof, c.SalePort, c.Prices, c.Directory,
                c.FreightCoordinator, plan, stockpile,
                c.Cash, "household:7", c.Obligations, c.Ids,
                0, day, c.Diag, out noSale);
            Assert.IsNotNull(refusal, "procurement refuses with the shelves empty");
            Assert.IsTrue(refusal.Contains("door hardware"),
                "the refusal names the missing hardware: " + refusal);
            Assert.IsNull(noSale, "no sale is recorded for materials that do not exist");
            Assert.AreNotEqual(WorkPackageStatus.Complete, roof.Status);
            Assert.AreNotEqual(WorkPackageStatus.MaterialsStaged, roof.Status,
                "nothing stages without the hardware");
            Assert.AreEqual(0, c.Yard.HardwareStock.TotalHardwareUnits);
            // Sills (40 lumber) + walls (300 lumber) at 40c = 13600c. The
            // roof's 300 lumber and 9 hardware were never bought.
            Assert.AreEqual(100000 - 13600, c.HouseholdLedger.GetBalanceCents(),
                "only the lumber packages were paid for — no hardware charge invented");
            Assert.AreEqual(13600, c.YardPurse.ReadBalanceCents());
            string completionRefusal = c.Executor.CompleteProject(project, ProductionDesignPresets.SettlerCabin(), c.Housing, day, c.Diag);
            Assert.IsNotNull(completionRefusal,
                "completion refuses while the roof package is unfinished: " + string.Join("; ", c.Diag));
            Assert.AreNotEqual(ConstructionProjectStatus.Complete, project.Status,
                "no finished cabin, no door, without the hardware");
        }
    }
}
