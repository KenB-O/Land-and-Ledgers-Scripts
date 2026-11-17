using System;
using System.Collections.Generic;
using LandLedgers.Economy;
using LandLedgers.Economy.Blacksmith;
using LandLedgers.Economy.Businesses.LumberYard;
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
    /// G1 (lane G1, forestry-to-construction chain): the failed-procurement
    /// unwind. A sale that stands after a later leg fails used to strand
    /// paid-for stock in no custody — the yard's lots were withdrawn, the
    /// buyer had paid, nothing reached the site stockpile, the package
    /// stayed Ready forever (no production retry caller exists), and a
    /// manual retry would buy the same lumber a second time. The
    /// procurement service now unwinds the sale on any post-sale failure
    /// (void the sale, drain the transient stockpile, refund the cash or
    /// cancel the booked payable), so a failed leg leaves the world
    /// exactly as it found it and a retry buys exactly once.
    /// </summary>
    [TestFixture]
    public sealed class ConstructionProcurementFailureUnwindTests
    {
        #region Fakes (runtime-adapter stand-ins, mirroring ConstructionFullChainTests)

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
            public readonly HashSet<string> VoidedSaleIds = new HashSet<string>();
            public bool Misprice;
            public string SaleDisplayNameOverride;
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
                        DisplayName = string.IsNullOrWhiteSpace(SaleDisplayNameOverride)
                            ? r.MaterialDisplayName
                            : SaleDisplayNameOverride,
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

        private static BuildingDesign LumberCabinDesign(string designId)
        {
            var design = new BuildingDesign
            {
                DesignId = designId,
                DisplayName = "Lumber cabin",
                WorksKind = ConstructionWorksKind.Unspecified,
                KindLabel = "cabin",
            };
            var framing = new BuildingDesignPhase
            {
                PhaseName = "Framing", Sequence = 0, LaborMinutes = 480, Notes = "lumber walls and roof",
            };
            framing.Materials.Add(Material("lumber", 600, "board feet", ConstructionMaterialKind.Lumber));
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
            public FakePricePort Prices = new FakePricePort();
            public FakeSalePort SalePort = new FakeSalePort();
            public SupplierDirectory Directory = new SupplierDirectory();
            public PersonScheduleTracker Tracker = new PersonScheduleTracker();
            public TaskAuthority Tasks = new TaskAuthority();
            public WorkTimeBudgetStore Budgets = new WorkTimeBudgetStore();
            public CreditCashBridge Cash = new CreditCashBridge();
            public FinancialObligationAuthority Obligations = new FinancialObligationAuthority();
            public EntityIdRegistry Ids = new EntityIdRegistry();

            public Chain()
            {
                Prices.Prices["lumber"] = 40;
                Prices.Prices["nails"] = 5;
                SalePort.Stock["lumber"] = 10000;
                SalePort.Stock["nails"] = 10000;
                var yard = new FakeGoodsSupplier { BusinessId = "lumber-yard" };
                yard.Categories.Add("construction-lumber");
                yard.Categories.Add("construction-hardware");
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
                Assert.IsNull(Catalog.RegisterDesign(LumberCabinDesign("cabin-a"), Diag));
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

        private string RunMaterialLeg(Chain c, ConstructionProject project, WorkPackage package,
            ConstructionSiteStockpile stockpile, ConstructionFreightPlan plan, int day,
            FinancialObligationAuthority obligations, EntityIdRegistry ids)
        {
            int priced = 0;
            foreach (ConstructionMaterialRequirement requirement in package.Materials)
            {
                int unitPrice = c.Prices.UnitPriceCents(requirement);
                Assert.GreaterOrEqual(unitPrice, 0);
                priced += unitPrice * requirement.RequiredUnits;
            }
            ConstructionMaterialSale sale;
            return c.Procurement.ProcureFreightAndStage(
                c.Executor, project, package, c.SalePort, c.Prices, c.Directory,
                c.FreightCoordinator, plan, stockpile,
                c.Cash, "household:7", obligations, ids,
                priced, day, c.Diag, out sale);
        }

        #endregion

        #region Service-level unwind

        [Test]
        public void FreightFailureAfterPaidSale_UnwindsSaleAndRefundsBuyer()
        {
            var c = new Chain();
            HouseholdLedger ledger = c.RegisterHousehold(7, 100000);
            PurseCashStore yard = c.RegisterPurse("business:lumber-yard", 0);
            NpcConstructionRecord record;
            ConstructionProject project = c.CommissionCabin(out record);
            WorkPackage framing = project.WorkPackages[0]; // 600 units of lumber
            var stockpile = new ConstructionSiteStockpile { ProjectId = project.ProjectId };

            // A toy wagon that cannot carry 600 units (600kg at 1kg/unit).
            var plan = c.FreightPlan(c.Wagon("wagon-tiny", 10f, 100f),
                "site:" + project.ProjectId, 41, 42, 43);

            string problem = RunMaterialLeg(c, project, framing, stockpile, plan, 11, c.Obligations, c.Ids);
            Assert.IsNotNull(problem);
            StringAssert.Contains("blocked pre-load", problem);
            StringAssert.Contains("unwound", problem);
            Assert.AreEqual(WorkPackageStatus.Ready, framing.Status,
                "the package stays Ready — nothing staged, nothing lost.");

            // Conservation: one sale recorded and voided, every unit back,
            // every cent back, nothing at the site.
            Assert.AreEqual(1, c.SalePort.Sales.Count);
            Assert.IsTrue(c.SalePort.VoidedSaleIds.Contains(c.SalePort.Sales[0].SaleId));
            Assert.AreEqual(10000, c.SalePort.Stock["lumber"]);
            Assert.AreEqual(100000, ledger.GetBalanceCents());
            Assert.AreEqual(0, yard.ReadBalanceCents());
            Assert.AreEqual(0, stockpile.AvailableUnits("lumber"));
        }

        [Test]
        public void RetryAfterUnwind_BuysExactlyOnce()
        {
            var c = new Chain();
            HouseholdLedger ledger = c.RegisterHousehold(7, 100000);
            PurseCashStore yard = c.RegisterPurse("business:lumber-yard", 0);
            NpcConstructionRecord record;
            ConstructionProject project = c.CommissionCabin(out record);
            WorkPackage framing = project.WorkPackages[0];
            var stockpile = new ConstructionSiteStockpile { ProjectId = project.ProjectId };

            var tinyPlan = c.FreightPlan(c.Wagon("wagon-tiny", 10f, 100f),
                "site:" + project.ProjectId, 41, 42, 43);
            Assert.IsNotNull(RunMaterialLeg(c, project, framing, stockpile, tinyPlan, 11, c.Obligations, c.Ids));
            Assert.AreEqual(1, c.SalePort.Sales.Count);
            Assert.AreEqual(1, c.SalePort.VoidedSaleIds.Count);

            // A clean retry with a wagon that can carry the load buys once.
            var bigPlan = c.FreightPlan(c.Wagon("wagon-1", 100000f, 100f),
                "site:" + project.ProjectId, 41, 42, 43);
            Assert.IsNull(RunMaterialLeg(c, project, framing, stockpile, bigPlan, 12, c.Obligations, c.Ids),
                string.Join("; ", c.Diag));
            Assert.AreEqual(WorkPackageStatus.MaterialsStaged, framing.Status);
            Assert.AreEqual(2, c.SalePort.Sales.Count);
            Assert.AreEqual(1, c.SalePort.VoidedSaleIds.Count,
                "only the failed sale is voided — the retry is a second sale, not a second charge for the first.");
            Assert.IsFalse(c.SalePort.VoidedSaleIds.Contains(c.SalePort.Sales[1].SaleId));
            Assert.AreEqual(9400, c.SalePort.Stock["lumber"],
                "the retry consumed the lumber exactly once.");
            Assert.AreEqual(100000 - 24000, ledger.GetBalanceCents());
            Assert.AreEqual(24000, yard.ReadBalanceCents());
            Assert.AreEqual(0, stockpile.AvailableUnits("lumber"),
                "staging consumed the stockpile — nothing left behind, nothing duplicated.");
        }

        [Test]
        public void SaleTotalMismatch_UnwindsUnpaidSale()
        {
            var c = new Chain();
            c.SalePort.Misprice = true;
            HouseholdLedger ledger = c.RegisterHousehold(7, 100000);
            PurseCashStore yard = c.RegisterPurse("business:lumber-yard", 0);
            NpcConstructionRecord record;
            ConstructionProject project = c.CommissionCabin(out record);
            WorkPackage framing = project.WorkPackages[0];
            var stockpile = new ConstructionSiteStockpile { ProjectId = project.ProjectId };
            var plan = c.FreightPlan(c.Wagon("wagon-1", 100000f, 100f),
                "site:" + project.ProjectId, 41, 42, 43);

            string problem = RunMaterialLeg(c, project, framing, stockpile, plan, 11, c.Obligations, c.Ids);
            Assert.IsNotNull(problem);
            StringAssert.Contains("totals", problem);
            StringAssert.Contains("unwound", problem);
            Assert.AreEqual(WorkPackageStatus.Ready, framing.Status);

            // The mispriced sale was voided before any cash moved.
            Assert.AreEqual(1, c.SalePort.Sales.Count);
            Assert.IsTrue(c.SalePort.VoidedSaleIds.Contains(c.SalePort.Sales[0].SaleId));
            Assert.AreEqual(10000, c.SalePort.Stock["lumber"]);
            Assert.AreEqual(100000, ledger.GetBalanceCents());
            Assert.AreEqual(0, yard.ReadBalanceCents());
            Assert.AreEqual(0, stockpile.AvailableUnits("lumber"));
        }

        [Test]
        public void StageFailure_UnwindsSaleAndDrainsStockpile()
        {
            var c = new Chain();
            // A misbehaving port labels the sale lines "timber": freight
            // delivers them, but the package requires "lumber", so staging
            // refuses — the C-9 strand shape. The unwind must still
            // conserve everything.
            c.SalePort.SaleDisplayNameOverride = "timber";
            HouseholdLedger ledger = c.RegisterHousehold(7, 100000);
            PurseCashStore yard = c.RegisterPurse("business:lumber-yard", 0);
            NpcConstructionRecord record;
            ConstructionProject project = c.CommissionCabin(out record);
            WorkPackage framing = project.WorkPackages[0];
            var stockpile = new ConstructionSiteStockpile { ProjectId = project.ProjectId };
            var plan = c.FreightPlan(c.Wagon("wagon-1", 100000f, 100f),
                "site:" + project.ProjectId, 41, 42, 43);

            string problem = RunMaterialLeg(c, project, framing, stockpile, plan, 11, c.Obligations, c.Ids);
            Assert.IsNotNull(problem);
            StringAssert.Contains("not fully covered", problem);
            StringAssert.Contains("unwound", problem);
            Assert.AreEqual(WorkPackageStatus.Ready, framing.Status);

            Assert.AreEqual(1, c.SalePort.Sales.Count);
            Assert.IsTrue(c.SalePort.VoidedSaleIds.Contains(c.SalePort.Sales[0].SaleId));
            // The fake keys stock by display name, so the void restores
            // the units under the line's name ("timber") while the sale
            // consumed them under the requirement's name ("lumber"); the
            // real yard restores by lot instead. Conservation here is
            // that every sold unit came back, under the line's name.
            Assert.AreEqual(9400, c.SalePort.Stock["lumber"]);
            Assert.AreEqual(600, c.SalePort.Stock["timber"]);
            Assert.AreEqual(100000, ledger.GetBalanceCents());
            Assert.AreEqual(0, yard.ReadBalanceCents());
            Assert.AreEqual(0, stockpile.AvailableUnits("timber"),
                "the stockpile was drained by the unwind — freighted units never strand in a transient stockpile.");
        }

        [Test]
        public void PaymentFailureWithoutObligationAuthority_UnwindsSale()
        {
            var c = new Chain();
            // Too poor to pay: the sale is made, the cash move fails, and
            // there is no obligation authority to book the debt — the sale
            // must be voided, not left standing unpaid.
            HouseholdLedger ledger = c.RegisterHousehold(7, 1000);
            PurseCashStore yard = c.RegisterPurse("business:lumber-yard", 0);
            NpcConstructionRecord record;
            ConstructionProject project = c.CommissionCabin(out record);
            WorkPackage framing = project.WorkPackages[0];
            var stockpile = new ConstructionSiteStockpile { ProjectId = project.ProjectId };
            var plan = c.FreightPlan(c.Wagon("wagon-1", 100000f, 100f),
                "site:" + project.ProjectId, 41, 42, 43);

            string problem = RunMaterialLeg(c, project, framing, stockpile, plan, 11,
                null, null);
            Assert.IsNotNull(problem);
            StringAssert.Contains("no obligation authority", problem);
            StringAssert.Contains("unwound", problem);
            Assert.AreEqual(WorkPackageStatus.Ready, framing.Status);

            Assert.AreEqual(1, c.SalePort.Sales.Count);
            Assert.IsTrue(c.SalePort.VoidedSaleIds.Contains(c.SalePort.Sales[0].SaleId));
            Assert.AreEqual(10000, c.SalePort.Stock["lumber"]);
            Assert.AreEqual(1000, ledger.GetBalanceCents());
            Assert.AreEqual(0, yard.ReadBalanceCents());
        }

        [Test]
        public void PaymentFailureWithPayable_FreightFailureCancelsPayable()
        {
            var c = new Chain();
            // Too poor to pay, but the obligation authority books the debt
            // as a real payable and freight proceeds — then freight fails,
            // so the payable must be cancelled in full with the sale.
            HouseholdLedger ledger = c.RegisterHousehold(7, 1000);
            PurseCashStore yard = c.RegisterPurse("business:lumber-yard", 0);
            NpcConstructionRecord record;
            ConstructionProject project = c.CommissionCabin(out record);
            WorkPackage framing = project.WorkPackages[0];
            var stockpile = new ConstructionSiteStockpile { ProjectId = project.ProjectId };
            var tinyPlan = c.FreightPlan(c.Wagon("wagon-tiny", 10f, 100f),
                "site:" + project.ProjectId, 41, 42, 43);

            string problem = RunMaterialLeg(c, project, framing, stockpile, tinyPlan, 11, c.Obligations, c.Ids);
            Assert.IsNotNull(problem);
            StringAssert.Contains("blocked pre-load", problem);
            StringAssert.Contains("unwound", problem);
            Assert.AreEqual(WorkPackageStatus.Ready, framing.Status);

            Assert.AreEqual(1, c.SalePort.Sales.Count);
            Assert.IsTrue(c.SalePort.VoidedSaleIds.Contains(c.SalePort.Sales[0].SaleId));
            Assert.AreEqual(10000, c.SalePort.Stock["lumber"]);
            Assert.AreEqual(1000, ledger.GetBalanceCents());
            Assert.AreEqual(0, yard.ReadBalanceCents());
            Assert.AreEqual(1, c.Obligations.Obligations.Count);
            foreach (FinancialObligation obligation in c.Obligations.Obligations)
            {
                Assert.AreEqual(FinancialObligationStatus.Cancelled, obligation.Status);
                Assert.AreEqual(0, obligation.OutstandingPrincipalCents);
            }
        }

        #endregion

        #region Yard-level void

        private sealed class YardFixture
        {
            public List<string> Diag = new List<string>();
            public EntityIdRegistry Ids = new EntityIdRegistry();
            public LumberYardShopRuntime Yard;
            public LumberYardConstructionSaleAdapter Adapter;
            public FakePricePort Prices = new FakePricePort();

            public YardFixture()
            {
                Yard = new LumberYardShopRuntime("lumber-yard", Ids);
                Adapter = new LumberYardConstructionSaleAdapter(Yard);
                Prices.Prices["lumber"] = 40;
                Prices.Prices["nails"] = 5;
            }

            public void StockLumber(int units, string species = "white pine")
            {
                Assert.IsNull(Yard.ReceiveLumberPurchase(units, species,
                    LumberYardGradeCatalog.DefaultGradeId, "mill-1", "test purchase",
                    1, 32, Diag), string.Join("; ", Diag));
            }

            public void StockNails(int units)
            {
                Assert.IsNull(Yard.ReceiveHardwareFromBlacksmith(LumberYardHardwareKind.Nails,
                    units, "smith-1", 1, string.Empty, 1, 2, Diag), string.Join("; ", Diag));
            }

            public ConstructionMaterialSale Sell(string projectId, string packageId,
                List<ConstructionMaterialRequirement> requirements, int day = 11)
            {
                ConstructionMaterialSale sale;
                Assert.IsNull(Adapter.TrySellToProject("lumber-yard", projectId, packageId,
                    requirements, Prices, day, Diag, out sale), string.Join("; ", Diag));
                Assert.IsNotNull(sale);
                return sale;
            }
        }

        [Test]
        public void YardVoid_RestoresFullyDrainedLot_WithProvenance()
        {
            var y = new YardFixture();
            y.StockLumber(10);
            EntityId lotId = y.Yard.LumberStock.Lots[0].LotId;
            Assert.AreEqual(10, y.Yard.LumberStock.TotalLumberUnits);

            ConstructionMaterialSale sale = y.Sell("proj-1", "pkg-1",
                new List<ConstructionMaterialRequirement>
                {
                    Material("lumber", 10, "units", ConstructionMaterialKind.Lumber, "lumber-yard"),
                });
            Assert.AreEqual(0, y.Yard.LumberStock.TotalLumberUnits);
            Assert.AreEqual(0, y.Yard.LumberStock.Lots.Count,
                "the drained lot was removed from stock by the sale.");

            int voidedCents;
            Assert.IsNull(y.Adapter.TryVoidSale(sale, 12, y.Diag, out voidedCents),
                string.Join("; ", y.Diag));
            Assert.AreEqual(400, voidedCents);
            Assert.AreEqual(1, y.Yard.LumberStock.Lots.Count);
            LumberYardLumberLot restored = y.Yard.LumberStock.FindLot(lotId);
            Assert.IsNotNull(restored, "the drained lot was recreated under its SAME yard lot id.");
            Assert.AreEqual(10, restored.LumberUnits);
            Assert.AreEqual("white pine", restored.Species);
            Assert.AreEqual(32, restored.UnitCostCents);
            Assert.AreEqual("mill-1 (test purchase)", restored.SellerNote);
            Assert.AreEqual(10, y.Yard.LumberStock.TotalLumberUnits);
            Assert.AreEqual(10, y.Yard.LumberStock.AvailableUnitsForSale(null, null));
            Assert.IsTrue(y.Yard.SalesHistory[0].Voided);
            Assert.AreEqual(12, y.Yard.SalesHistory[0].VoidedDayIndex);

            // A second void refuses — never voided twice, never restocked twice.
            Assert.IsNotNull(y.Adapter.TryVoidSale(sale, 13, y.Diag, out voidedCents));
            Assert.AreEqual(10, y.Yard.LumberStock.TotalLumberUnits);
        }

        [Test]
        public void YardVoid_RestoresPartialSale_AcrossTwoLots()
        {
            var y = new YardFixture();
            y.StockLumber(10);
            y.StockLumber(10);
            EntityId firstLot = y.Yard.LumberStock.Lots[0].LotId;
            EntityId secondLot = y.Yard.LumberStock.Lots[1].LotId;

            ConstructionMaterialSale sale = y.Sell("proj-1", "pkg-1",
                new List<ConstructionMaterialRequirement>
                {
                    Material("lumber", 15, "units", ConstructionMaterialKind.Lumber, "lumber-yard"),
                });
            Assert.AreEqual(5, y.Yard.LumberStock.TotalLumberUnits);

            int voidedCents;
            Assert.IsNull(y.Adapter.TryVoidSale(sale, 12, y.Diag, out voidedCents),
                string.Join("; ", y.Diag));
            Assert.AreEqual(600, voidedCents);
            Assert.AreEqual(20, y.Yard.LumberStock.TotalLumberUnits);
            Assert.AreEqual(10, y.Yard.LumberStock.FindLot(firstLot).LumberUnits);
            Assert.AreEqual(10, y.Yard.LumberStock.FindLot(secondLot).LumberUnits);
        }

        [Test]
        public void YardVoid_RestoresLumberAndNails()
        {
            var y = new YardFixture();
            y.StockLumber(10);
            y.StockNails(20);
            ConstructionMaterialSale sale = y.Sell("proj-1", "pkg-1",
                new List<ConstructionMaterialRequirement>
                {
                    Material("lumber", 10, "units", ConstructionMaterialKind.Lumber, "lumber-yard"),
                    Material("nails", 20, "units", ConstructionMaterialKind.Hardware, "lumber-yard"),
                });
            Assert.AreEqual(0, y.Yard.LumberStock.TotalLumberUnits);
            Assert.AreEqual(0, y.Yard.HardwareStock.TotalHardwareUnits);

            int voidedCents;
            Assert.IsNull(y.Adapter.TryVoidSale(sale, 12, y.Diag, out voidedCents),
                string.Join("; ", y.Diag));
            Assert.AreEqual(500, voidedCents);
            Assert.AreEqual(10, y.Yard.LumberStock.TotalLumberUnits);
            Assert.AreEqual(20, y.Yard.HardwareStock.TotalHardwareUnits);
            Assert.AreEqual(LumberYardHardwareKind.Nails,
                y.Yard.HardwareStock.Lots[0].HardwareKind);
            Assert.AreEqual("smith-1",
                y.Yard.HardwareStock.Lots[0].SourceBlacksmithBusinessId);
        }

        [Test]
        public void YardVoid_EarmarkedSaleReturnsAsOrdinaryStock()
        {
            var y = new YardFixture();
            y.StockLumber(10);
            EntityId lotId = y.Yard.LumberStock.Lots[0].LotId;
            Assert.IsNull(y.Yard.EarmarkLumberForProject(lotId, "proj-1", 10, 2, y.Diag),
                string.Join("; ", y.Diag));
            Assert.AreEqual(0, y.Yard.LumberStock.AvailableUnitsForSale(null, null),
                "the earmark leaves ordinary sale inventory.");

            ConstructionMaterialSale sale = y.Sell("proj-1", "pkg-1",
                new List<ConstructionMaterialRequirement>
                {
                    Material("lumber", 10, "units", ConstructionMaterialKind.Lumber, "lumber-yard"),
                });
            int voidedCents;
            Assert.IsNull(y.Adapter.TryVoidSale(sale, 12, y.Diag, out voidedCents),
                string.Join("; ", y.Diag));
            Assert.AreEqual(10, y.Yard.LumberStock.AvailableUnitsForSale(null, null),
                "voided units return as ordinary stock — the earmark was spent by the sale.");
            Assert.AreEqual(0, y.Yard.LumberStock.EarmarkedUnitsForProject("proj-1", null, null));
        }

        [Test]
        public void YardVoid_UnknownSale_Refused_NothingMoves()
        {
            var y = new YardFixture();
            y.StockLumber(10);
            var phantom = new ConstructionMaterialSale
            {
                SaleId = "sale-phantom",
                SupplierBusinessId = "lumber-yard",
                ProjectId = "proj-1",
                PackageId = "pkg-1",
            };
            int voidedCents = -1;
            Assert.IsNotNull(y.Adapter.TryVoidSale(phantom, 12, y.Diag, out voidedCents));
            Assert.AreEqual(0, voidedCents);
            Assert.AreEqual(10, y.Yard.LumberStock.TotalLumberUnits);
        }

        [Test]
        public void YardVoid_VoidedSaleCannotInvoiceOrDeliver()
        {
            var y = new YardFixture();
            y.StockLumber(10);
            ConstructionMaterialSale sale = y.Sell("proj-1", "pkg-1",
                new List<ConstructionMaterialRequirement>
                {
                    Material("lumber", 10, "units", ConstructionMaterialKind.Lumber, "lumber-yard"),
                });
            int voidedCents;
            Assert.IsNull(y.Adapter.TryVoidSale(sale, 12, y.Diag, out voidedCents),
                string.Join("; ", y.Diag));

            LumberYardSaleRecord record = y.Yard.SalesHistory[0];
            Assert.IsNull(y.Yard.IssueContractorInvoiceForSale("contractor-1", record, null, 13, y.Diag),
                "a voided sale is never invoiced.");
            Assert.IsNotNull(y.Yard.ScheduleDeliveryForSale(record, "site", 5, false,
                string.Empty, false, null, 13, y.Diag),
                "a voided sale is never delivered.");
            Assert.AreEqual(10, y.Yard.LumberStock.TotalLumberUnits);
        }

        [Test]
        public void YardVoid_SurvivesSaveLoad_WithoutDuplication()
        {
            var y = new YardFixture();
            y.StockLumber(10);
            EntityId lotId = y.Yard.LumberStock.Lots[0].LotId;
            ConstructionMaterialSale sale = y.Sell("proj-1", "pkg-1",
                new List<ConstructionMaterialRequirement>
                {
                    Material("lumber", 10, "units", ConstructionMaterialKind.Lumber, "lumber-yard"),
                });

            var dto = y.Yard.CaptureSaveDto();
            var restored = new LumberYardShopRuntime("lumber-yard", y.Ids);
            restored.LoadFromSaveDto(dto);
            Assert.AreEqual(0, restored.LumberStock.TotalLumberUnits);
            Assert.IsFalse(restored.SalesHistory[0].Voided);

            // The dispense-line snapshot survives the round trip, so the
            // void still recreates the drained lot exactly afterwards.
            var adapter = new LumberYardConstructionSaleAdapter(restored);
            int voidedCents;
            Assert.IsNull(adapter.TryVoidSale(sale, 12, y.Diag, out voidedCents),
                string.Join("; ", y.Diag));
            Assert.AreEqual(400, voidedCents);
            LumberYardLumberLot lot = restored.LumberStock.FindLot(lotId);
            Assert.IsNotNull(lot);
            Assert.AreEqual(10, lot.LumberUnits);
            Assert.AreEqual("white pine", lot.Species);
            Assert.AreEqual(32, lot.UnitCostCents);
        }

        #endregion
    }
}
