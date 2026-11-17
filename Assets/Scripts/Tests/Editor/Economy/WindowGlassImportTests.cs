using System.Collections.Generic;
using LandLedgers.Economy;
using LandLedgers.Economy.Businesses.GeneralStore;
using LandLedgers.Economy.Businesses.LumberYard;
using LandLedgers.Economy.Liabilities;
using LandLedgers.Economy.Trade;
using LandLedgers.Orchestration.Systems;
using LandLedgers.Primitives;
using NUnit.Framework;

namespace LandLedgers.EditorTests.Economy
{
    /// <summary>
    /// F1 (workstream F, glass leg): off-map manufactured window glass.
    /// §18 T — glass imports require a legitimate purchase and delivery:
    /// every pane traces to a paid import order (cash or honest credit)
    /// that spent 14 days in transit from Pittsburgh glassworks via the
    /// railhead, and no pane appears before its order arrives. §18 U —
    /// glass does not appear at a building automatically: an unglazed
    /// build carries no glass requirement at all, and a glazed build
    /// refuses outright when the yard holds no priced glass. No local
    /// glassmaking industry exists in this pass; no phantom merchant, no
    /// free glass, no universal mandatory glass.
    /// </summary>
    [TestFixture]
    public sealed class WindowGlassImportTests
    {
        private sealed class TestBusinessCashPort : IBusinessIdentityCashPort
        {
            public string BusinessId { get; }
            public int BalanceCents { get; private set; }
            public bool RefuseCredit { get; set; }
            public bool RefuseDebit { get; set; }

            public TestBusinessCashPort(string businessId, int openingBalanceCents)
            {
                BusinessId = businessId;
                BalanceCents = openingBalanceCents;
            }

            public void SetBalance(int balanceCents)
            {
                BalanceCents = balanceCents;
            }

            public string TryDebit(int amountCents, string reason)
            {
                if (RefuseDebit) return "TestBusinessCashPort: debit refused by the test double.";
                if (amountCents < 0) return "TestBusinessCashPort: negative debit.";
                if (BalanceCents < amountCents) return $"TestBusinessCashPort: insufficient funds ({BalanceCents}c for {amountCents}c).";
                BalanceCents -= amountCents;
                return null;
            }

            public string Credit(int amountCents, string reason)
            {
                if (RefuseCredit) return "TestBusinessCashPort: credit refused by the test double.";
                if (amountCents < 0) return "TestBusinessCashPort: negative credit.";
                BalanceCents += amountCents;
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

        private static LumberYardGlassImportDriver NewImportDriver(
            LumberYardShopRuntime yard, TestBusinessCashPort yardCash,
            BusinessLiabilityLedger liabilities, EntityIdRegistry ids)
        {
            var diagnostics = new List<string>();
            var driver = new LumberYardGlassImportDriver(yard, diagnostics);
            driver.YardCash = yardCash;
            driver.LiabilityLedger = liabilities;
            driver.IdRegistry = ids;
            return driver;
        }

        private static LumberYardShopRuntime NewYard()
        {
            return new LumberYardShopRuntime("lumber-yard", new EntityIdRegistry());
        }

        private static void StockGlass(LumberYardShopRuntime yard, int panes, string orderId, int unitCostCents = 25)
        {
            var diagnostics = new List<string>();
            Assert.IsNull(yard.ReceiveGlassImportArrival(
                panes, orderId, "Pittsburgh glassworks, via railhead", 0, unitCostCents, diagnostics),
                string.Join("; ", diagnostics));
        }

        private static BuildingDesignCatalog PresetCatalog()
        {
            var catalog = new BuildingDesignCatalog();
            var diag = new List<string>();
            foreach (BuildingDesign design in ProductionDesignPresets.All())
                Assert.IsNull(catalog.RegisterDesign(design, diag),
                    $"preset '{design.DesignId}' registers: " + string.Join("; ", diag));
            return catalog;
        }

        // ---- Reorder policy ----

        [Test]
        public void Policy_EmptyYard_FiresOrderFor12Panes()
        {
            var yard = NewYard();
            var policy = new LumberYardGlassReorderPolicy();
            LumberYardGlassReorderSignal signal = policy.Evaluate(yard, 0);
            Assert.IsTrue(signal.ShouldOrder);
            Assert.AreEqual(12, signal.OrderUnits, "the fixed import lot is 12 panes");
            Assert.AreEqual(0, signal.CoveredUnits);
            Assert.AreEqual(LumberYardSupply.GlassImportMaterialId, signal.MaterialId);
        }

        [Test]
        public void Policy_CoveredStockAboveThreshold_NoOrder()
        {
            var yard = NewYard();
            StockGlass(yard, 5, "IMP-ORD-SEED");
            var policy = new LumberYardGlassReorderPolicy();
            Assert.IsFalse(policy.Evaluate(yard, 0).ShouldOrder, "5 panes on hand is above the threshold of 4");
            Assert.IsFalse(policy.Evaluate(yard, 12).ShouldOrder, "outstanding panes count toward covered stock");
            Assert.AreEqual(17, policy.Evaluate(yard, 12).CoveredUnits);
        }

        // ---- §18 T: the import driver ----

        // An empty yard orders 12 panes for cash at 25c, arriving day 14.
        [Test]
        public void Glass_DriveBelowThreshold_PlacesCashOrderAt25c()
        {
            var yard = NewYard();
            var yardCash = new TestBusinessCashPort("lumber-yard", 10000);
            var driver = NewImportDriver(yard, yardCash, new BusinessLiabilityLedger(), new EntityIdRegistry());

            Assert.IsNotNull(ImportCatalog.Get(LumberYardSupply.GlassImportMaterialId),
                "the driver registers the glass import material at construction");

            string summary = driver.Drive(0);
            Assert.AreEqual(1, driver.Orders.Count, summary);
            ImportOrder order = driver.Orders[0];
            Assert.AreEqual(LumberYardSupply.GlassImportMaterialId, order.MaterialId);
            Assert.AreEqual(12, order.Units);
            Assert.AreEqual(25, order.PricePerUnitCents, "glass import at 25c per pane (provisional)");
            Assert.AreEqual(14, order.ExpectedArrivalDayIndex, "14 days transit from Pittsburgh via the railhead");
            Assert.AreEqual("Pittsburgh glassworks, via railhead", order.OriginName);
            Assert.IsFalse(order.PaidOnCredit, "cash order");
            Assert.AreEqual(10000 - 12 * 25, yardCash.BalanceCents, "the yard pays 300c from its own business cash");
            Assert.AreEqual(0, yard.GlassStock.TotalGlassPanes, "nothing appears before arrival");
        }

        // The order is in transit for 14 days: nothing arrives early, and
        // no second order is placed while the first is outstanding.
        [Test]
        public void Glass_OrderInTransit_NoEarlyArrivalNoDoubleOrder()
        {
            var yard = NewYard();
            var yardCash = new TestBusinessCashPort("lumber-yard", 10000);
            var driver = NewImportDriver(yard, yardCash, new BusinessLiabilityLedger(), new EntityIdRegistry());

            driver.Drive(0);
            Assert.AreEqual(1, driver.Orders.Count);
            Assert.AreEqual(12, driver.OutstandingGlassUnits);

            driver.Drive(5);
            Assert.AreEqual(0, yard.GlassStock.TotalGlassPanes, "nothing arrives before day 14");
            Assert.AreEqual(1, driver.Orders.Count, "no second order while the first is outstanding");

            driver.Drive(13);
            Assert.AreEqual(0, yard.GlassStock.TotalGlassPanes, "day 13 is still too early");
            Assert.AreEqual(1, driver.Orders.Count);
        }

        // On day 14 the 12 panes land as one lot with the order id and
        // origin on it, and the covered stock (12) needs no new order.
        [Test]
        public void Glass_ArrivalDay_LandsAsProvenanceLot()
        {
            var yard = NewYard();
            var yardCash = new TestBusinessCashPort("lumber-yard", 10000);
            var driver = NewImportDriver(yard, yardCash, new BusinessLiabilityLedger(), new EntityIdRegistry());

            driver.Drive(0);
            ImportOrder order = driver.Orders[0];

            driver.Drive(14);
            Assert.AreEqual(12, yard.GlassStock.TotalGlassPanes);
            Assert.AreEqual(1, yard.GlassStock.Lots.Count);
            LumberYardGlassLot lot = yard.GlassStock.Lots[0];
            Assert.AreEqual(12, lot.GlassPanes);
            Assert.AreEqual(order.OrderId, lot.ImportOrderId);
            Assert.AreEqual("Pittsburgh glassworks, via railhead", lot.OriginName);
            Assert.AreEqual(25, lot.UnitCostCents);
            Assert.AreEqual(ImportOrderStatus.Delivered, order.Status);
            Assert.AreEqual(0, driver.OutstandingGlassUnits);
            Assert.AreEqual(1, driver.Orders.Count, "12 on hand is above the threshold — no new order");
        }

        // A yard already stocked above the threshold orders nothing.
        [Test]
        public void Glass_StockAboveThreshold_NoOrder()
        {
            var yard = NewYard();
            StockGlass(yard, 5, "IMP-ORD-SEED");
            var yardCash = new TestBusinessCashPort("lumber-yard", 10000);
            var driver = NewImportDriver(yard, yardCash, new BusinessLiabilityLedger(), new EntityIdRegistry());

            driver.Drive(0);
            Assert.AreEqual(0, driver.Orders.Count);
            Assert.AreEqual(10000, yardCash.BalanceCents, "no cash moves when no order is placed");
        }

        // No cash and no credit: the order is refused and nothing is
        // created — no order, no lot, no payable.
        [Test]
        public void Glass_NoCashNoCredit_OrderRefusedNothingCreated()
        {
            var yard = NewYard();
            var yardCash = new TestBusinessCashPort("lumber-yard", 0);
            var driver = NewImportDriver(yard, yardCash, null, new EntityIdRegistry());

            string summary = driver.Drive(0);
            Assert.AreEqual(0, driver.Orders.Count, summary);
            Assert.AreEqual(0, yard.GlassStock.TotalGlassPanes);
            Assert.AreEqual(0, yardCash.BalanceCents);
        }

        // No cash but a liability ledger: the order is placed on honest
        // credit and the payable is recorded.
        [Test]
        public void Glass_NoCashWithCredit_OrderPlacedOnCreditPayableRecorded()
        {
            var yard = NewYard();
            var yardCash = new TestBusinessCashPort("lumber-yard", 0);
            var liabilities = new BusinessLiabilityLedger();
            var ids = new EntityIdRegistry();
            var driver = NewImportDriver(yard, yardCash, liabilities, ids);

            driver.Drive(0);
            Assert.AreEqual(1, driver.Orders.Count);
            ImportOrder order = driver.Orders[0];
            Assert.IsTrue(order.PaidOnCredit);
            Assert.IsFalse(string.IsNullOrWhiteSpace(order.LiabilityId), "the credit order names its payable");
            Assert.AreEqual(1, liabilities.All.Count, "one payable on the liability ledger");
            Assert.AreEqual(12 * 25, liabilities.All[0].PrincipalCents, "the payable is the full order price");
            Assert.AreEqual(0, yardCash.BalanceCents, "no cash moves on a credit order");
        }

        // Save round trip: the order book persists, the next order number
        // is not reissued, and a due order still arrives after restore.
        [Test]
        public void GlassImport_SaveLoad_OrderBookPersistsNoReissueDueOrderArrives()
        {
            var yard = NewYard();
            var yardCash = new TestBusinessCashPort("lumber-yard", 10000);
            var driver = NewImportDriver(yard, yardCash, new BusinessLiabilityLedger(), new EntityIdRegistry());

            driver.Drive(0);
            Assert.AreEqual(1, driver.Orders.Count);
            string firstOrderId = driver.Orders[0].OrderId;

            var restored = new LumberYardGlassImportDriver(yard, new List<string>());
            restored.YardCash = new TestBusinessCashPort("lumber-yard", 10000);
            restored.LiabilityLedger = new BusinessLiabilityLedger();
            restored.IdRegistry = new EntityIdRegistry();
            restored.LoadFromSaveDto(driver.ToSaveDto());
            Assert.AreEqual(1, restored.Orders.Count);
            Assert.AreEqual(firstOrderId, restored.Orders[0].OrderId);
            Assert.AreEqual(ImportOrderStatus.Ordered, restored.Orders[0].Status);

            ImportOrder second = restored.PlaceGlassOrder(12, 1);
            Assert.IsNotNull(second);
            Assert.AreNotEqual(firstOrderId, second.OrderId, "the restored driver never reissues an order id");

            Assert.AreEqual(12, restored.ReceiveDueImports(14), "the restored order still arrives on day 14");
            Assert.AreEqual(12, yard.GlassStock.TotalGlassPanes);
            Assert.AreEqual(ImportOrderStatus.Delivered, restored.Orders[0].Status);
        }

        // ---- Yard glass stock and sales ----

        [Test]
        public void GlassStock_ReceiveImportArrival_RequiresNamedOrderAndOrigin()
        {
            var yard = NewYard();
            var diagnostics = new List<string>();
            Assert.IsNotNull(yard.ReceiveGlassImportArrival(
                12, string.Empty, "Pittsburgh glassworks, via railhead", 0, 25, diagnostics),
                "an anonymous import order is refused");
            Assert.IsNotNull(yard.ReceiveGlassImportArrival(
                12, "IMP-ORD-1", string.Empty, 0, 25, diagnostics),
                "an anonymous origin is refused");
            Assert.AreEqual(0, yard.GlassStock.TotalGlassPanes, "refused intakes create no stock");
        }

        [Test]
        public void YardSale_GlassSellsFromRealLots_WithProvenance()
        {
            var yard = NewYard();
            StockGlass(yard, 12, "IMP-ORD-9");
            var diag = new List<string>();
            LumberYardSaleRecord record = yard.TrySellToConstructionProject(
                "cproj-1", 0, 0, 0, 0, 2, 25, 0, diag);
            Assert.IsNotNull(record, string.Join("; ", diag));
            Assert.AreEqual(2, record.UnitsSoldFor(ConstructionResourceKind.Glass));
            Assert.AreEqual(50, record.TotalCents);
            Assert.AreEqual(10, yard.GlassStock.TotalGlassPanes);
            Assert.AreEqual(1, record.Lines.Count);
            Assert.AreEqual(1, record.Lines[0].GlassLines.Count);
            Assert.AreEqual("IMP-ORD-9", record.Lines[0].GlassLines[0].ImportOrderId);
            Assert.IsTrue(record.Lines[0].ProvenanceLines()[0].Contains("IMP-ORD-9"),
                "the provenance names the import order");
        }

        // A shortfall in ANY bucket refuses the whole sale atomically —
        // the lumber is not consumed when the glass runs out.
        [Test]
        public void YardSale_GlassShortfall_RefusesAtomicallyNothingConsumed()
        {
            var yard = NewYard();
            StockGlass(yard, 1, "IMP-ORD-9");
            var diag = new List<string>();
            LumberYardSaleRecord record = yard.TrySellToConstructionProject(
                "cproj-1", 10, 40, 0, 0, 2, 25, 0, diag);
            Assert.IsNull(record, string.Join("; ", diag));
            Assert.AreEqual(1, yard.GlassStock.TotalGlassPanes, "the short pane stays on the shelf");
            Assert.AreEqual(0, yard.SalesHistory.Count, "no sale is recorded");
        }

        [Test]
        public void YardSale_Void_RestoresGlassPanes()
        {
            var yard = NewYard();
            StockGlass(yard, 12, "IMP-ORD-9");
            var diag = new List<string>();
            LumberYardSaleRecord record = yard.TrySellToConstructionProject(
                "cproj-1", 0, 0, 0, 0, 2, 25, 0, diag);
            Assert.IsNotNull(record);
            Assert.AreEqual(10, yard.GlassStock.TotalGlassPanes);
            int voidedCents;
            Assert.IsNull(yard.VoidSale(record.SaleId.ToString(), 1, diag, out voidedCents), string.Join("; ", diag));
            Assert.AreEqual(50, voidedCents);
            Assert.AreEqual(12, yard.GlassStock.TotalGlassPanes, "the voided panes return to stock");
        }

        [Test]
        public void YardSaveLoad_GlassStockPersists()
        {
            var yard = NewYard();
            StockGlass(yard, 12, "IMP-ORD-9");
            var restored = new LumberYardShopRuntime("lumber-yard", new EntityIdRegistry());
            restored.LoadFromSaveDto(yard.CaptureSaveDto());
            Assert.AreEqual(12, restored.GlassStock.TotalGlassPanes);
            Assert.AreEqual(1, restored.GlassStock.Lots.Count);
            Assert.AreEqual("IMP-ORD-9", restored.GlassStock.Lots[0].ImportOrderId);
            Assert.AreEqual("Pittsburgh glassworks, via railhead", restored.GlassStock.Lots[0].OriginName);
            Assert.AreEqual(25, restored.GlassStock.Lots[0].UnitCostCents);
        }

        // ---- Price port: glass is priced only from a real stocked lot ----

        [Test]
        public void PricePort_NoGlassStock_NoPrice()
        {
            var yard = NewYard();
            var port = new LumberYardConstructionPricePort(yard);
            ConstructionMaterialRequirement requirement = WindowGlassRequirements.GlassRequirementFor("settler-cabin");
            Assert.AreEqual(-1, port.UnitPriceCents(requirement), "no glass in the yard means no glass price");
        }

        [Test]
        public void PricePort_StockedGlassLot_PricesFromRealCost()
        {
            var yard = NewYard();
            StockGlass(yard, 12, "IMP-ORD-9");
            var port = new LumberYardConstructionPricePort(yard);
            ConstructionMaterialRequirement requirement = WindowGlassRequirements.GlassRequirementFor("settler-cabin");
            Assert.AreEqual(25, port.UnitPriceCents(requirement), "the price is the real lot cost, never invented");
        }

        // ---- Sale adapter: the negative test (§18 U) ----

        // A window package without ordered glass cannot complete: the
        // yard has no panes, the adapter refuses, and nothing is sold.
        [Test]
        public void Adapter_GlassRequirementWithoutStock_RefusesNothingSold()
        {
            var yard = NewYard();
            var port = new LumberYardConstructionPricePort(yard);
            var adapter = new LumberYardConstructionSaleAdapter(yard);
            var diag = new List<string>();
            var requirements = new List<ConstructionMaterialRequirement>
            {
                WindowGlassRequirements.GlassRequirementFor("settler-cabin"),
            };
            ConstructionMaterialSale sale;
            string refusal = adapter.TrySellToProject(
                "lumber-yard", "cproj-1", "wpkg-1", requirements, port, 0, diag, out sale);
            Assert.IsNotNull(refusal, "the sale must refuse — the glass was never ordered or delivered");
            Assert.IsNull(sale);
            Assert.AreEqual(0, yard.GlassStock.TotalGlassPanes);
            Assert.AreEqual(0, yard.SalesHistory.Count);
        }

        [Test]
        public void Adapter_GlassRequirementWithStock_SellsGlassLine()
        {
            var yard = NewYard();
            StockGlass(yard, 12, "IMP-ORD-9");
            var port = new LumberYardConstructionPricePort(yard);
            var adapter = new LumberYardConstructionSaleAdapter(yard);
            var diag = new List<string>();
            var requirements = new List<ConstructionMaterialRequirement>
            {
                WindowGlassRequirements.GlassRequirementFor("settler-cabin"),
            };
            ConstructionMaterialSale sale;
            string refusal = adapter.TrySellToProject(
                "lumber-yard", "cproj-1", "wpkg-1", requirements, port, 0, diag, out sale);
            Assert.IsNull(refusal, string.Join("; ", diag));
            Assert.IsNotNull(sale);
            Assert.AreEqual(1, sale.Lines.Count);
            Assert.AreEqual("glass", sale.Lines[0].DisplayName);
            Assert.AreEqual(1, sale.Lines[0].Units);
            Assert.AreEqual(25, sale.Lines[0].UnitPriceCents);
            Assert.AreEqual(25, sale.TotalCents);
            Assert.IsTrue(sale.Lines[0].Provenance.Contains("IMP-ORD-9"),
                "the sale line carries the import order provenance");
            Assert.AreEqual(11, yard.GlassStock.TotalGlassPanes);
        }

        // ---- Registry and executor (§18 U) ----

        [Test]
        public void Registry_PanesForDesign()
        {
            Assert.AreEqual(1, WindowGlassRequirements.PanesForDesign("settler-cabin"));
            Assert.AreEqual(2, WindowGlassRequirements.PanesForDesign("family-cabin"));
            Assert.AreEqual(2, WindowGlassRequirements.PanesForDesign("carpenters-workshop"));
            Assert.AreEqual(-1, WindowGlassRequirements.PanesForDesign("stone-cabin"),
                "an unrecorded design has no window count — never guessed");
            Assert.AreEqual(-1, WindowGlassRequirements.PanesForDesign(null));
        }

        [Test]
        public void Executor_UnglazedBuild_HasNoGlassRequirement()
        {
            BuildingDesignCatalog catalog = PresetCatalog();
            var executor = new ConstructionProjectExecutor();
            var diag = new List<string>();
            BuildingDesign design = catalog.FindDesign("settler-cabin");
            ConstructionProject project = executor.CreateProject(catalog, 7, "settler-cabin", "parcel-9", 0, diag);
            Assert.IsNotNull(project, string.Join("; ", diag));
            Assert.IsNull(executor.CreateWorkPackages(project, design, 0, diag), string.Join("; ", diag));
            foreach (WorkPackage package in project.WorkPackages)
            {
                foreach (ConstructionMaterialRequirement requirement in package.Materials)
                    Assert.AreNotEqual(ConstructionMaterialKind.Glass, requirement.MaterialKind,
                        "an unglazed build carries no glass requirement");
            }
        }

        [Test]
        public void Executor_GlazedBuild_GlassOnOpeningsPackageOnly()
        {
            BuildingDesignCatalog catalog = PresetCatalog();
            var executor = new ConstructionProjectExecutor();
            var diag = new List<string>();
            BuildingDesign design = catalog.FindDesign("settler-cabin");
            ConstructionProject project = executor.CreateProject(catalog, 7, "settler-cabin", "parcel-9", 0, diag);
            Assert.IsNotNull(project, string.Join("; ", diag));
            Assert.IsNull(executor.CreateWorkPackages(project, design, true, 0, diag), string.Join("; ", diag));
            int glassLines = 0;
            foreach (WorkPackage package in project.WorkPackages)
            {
                foreach (ConstructionMaterialRequirement requirement in package.Materials)
                {
                    if (requirement.MaterialKind != ConstructionMaterialKind.Glass) continue;
                    glassLines++;
                    Assert.AreEqual(WindowGlassRequirements.OpeningsPhaseName, package.PhaseName,
                        "the glass requirement joins the openings package only");
                    Assert.AreEqual(1, requirement.RequiredUnits, "a settler cabin has one window, one pane");
                    Assert.AreEqual("glass", requirement.MaterialDisplayName);
                }
            }
            Assert.AreEqual(1, glassLines, "exactly one glass requirement per glazed build");
        }

        [Test]
        public void Executor_GlazedBuild_FamilyCabinNeedsTwoPanes()
        {
            BuildingDesignCatalog catalog = PresetCatalog();
            var executor = new ConstructionProjectExecutor();
            var diag = new List<string>();
            BuildingDesign design = catalog.FindDesign("family-cabin");
            ConstructionProject project = executor.CreateProject(catalog, 7, "family-cabin", "parcel-9", 0, diag);
            Assert.IsNotNull(project, string.Join("; ", diag));
            Assert.IsNull(executor.CreateWorkPackages(project, design, true, 0, diag), string.Join("; ", diag));
            foreach (WorkPackage package in project.WorkPackages)
            {
                foreach (ConstructionMaterialRequirement requirement in package.Materials)
                {
                    if (requirement.MaterialKind == ConstructionMaterialKind.Glass)
                        Assert.AreEqual(2, requirement.RequiredUnits, "a family cabin has two windows, two panes");
                }
            }
        }

        [Test]
        public void Executor_GlazedBuild_UnknownDesign_Refuses()
        {
            var catalog = new BuildingDesignCatalog();
            var diag = new List<string>();
            var design = new BuildingDesign { DesignId = "stone-cabin" };
            design.SpaceSpecs.Add(new BuildingDesignSpaceSpec { Label = "main room", SleepingCapacity = 4 });
            design.Phases.Add(new BuildingDesignPhase
            {
                PhaseName = WindowGlassRequirements.OpeningsPhaseName, Sequence = 0, LaborMinutes = 100,
                Materials = new List<ConstructionMaterialRequirement>(),
            });
            Assert.IsNull(catalog.RegisterDesign(design, diag), string.Join("; ", diag));
            var executor = new ConstructionProjectExecutor();
            ConstructionProject project = executor.CreateProject(catalog, 7, "stone-cabin", "parcel-9", 0, diag);
            Assert.IsNotNull(project, string.Join("; ", diag));
            Assert.IsNotNull(executor.CreateWorkPackages(project, design, true, 0, diag),
                "a glazed build on a design with no recorded window count refuses rather than guessing");
        }

        [Test]
        public void Executor_GlazedBuild_NoOpeningsPhase_Refuses()
        {
            var catalog = new BuildingDesignCatalog();
            var diag = new List<string>();
            var design = new BuildingDesign { DesignId = "settler-cabin" };
            design.SpaceSpecs.Add(new BuildingDesignSpaceSpec { Label = "main room", SleepingCapacity = 4 });
            design.Phases.Add(new BuildingDesignPhase
            {
                PhaseName = "Sills", Sequence = 0, LaborMinutes = 100,
                Materials = new List<ConstructionMaterialRequirement>(),
            });
            Assert.IsNull(catalog.RegisterDesign(design, diag), string.Join("; ", diag));
            var executor = new ConstructionProjectExecutor();
            ConstructionProject project = executor.CreateProject(catalog, 7, "settler-cabin", "parcel-9", 0, diag);
            Assert.IsNotNull(project, string.Join("; ", diag));
            Assert.IsNotNull(executor.CreateWorkPackages(project, design, true, 0, diag),
                "a glazed build with no openings phase refuses — the glass has nowhere to go");
        }

        // ---- Commissioner (§18 T/U): the glazed estimate ----

        [Test]
        public void Commission_GlazedWithPricedGlass_ViableEstimateIncludesGlass()
        {
            BuildingDesignCatalog catalog = PresetCatalog();
            var commissioner = new NpcConstructionCommissioner();
            var prices = new FakePricePort();
            prices.Prices["lumber"] = 40;
            prices.Prices["door hardware"] = 45;
            prices.Prices["glass"] = 25;
            var diag = new List<string>();
            NpcConstructionEvaluation unglazed = commissioner.EvaluateCommission(
                catalog, "settler-cabin", 1, prices, 100000, 28,
                NpcConstructionReason.Overcrowding, "test", 10, diag);
            Assert.IsTrue(unglazed.IsViable, string.Join("; ", diag));
            NpcConstructionEvaluation glazed = commissioner.EvaluateCommission(
                catalog, "settler-cabin", 1, prices, 100000, 28,
                NpcConstructionReason.Overcrowding, "test", 10, diag, true);
            Assert.IsTrue(glazed.IsViable, string.Join("; ", diag));
            Assert.AreEqual(unglazed.EstimateCents + 25, glazed.EstimateCents,
                "one settler window adds exactly one 25c pane to the estimate");
            Assert.IsTrue(glazed.Summary.Contains("glazed windows"));
        }

        [Test]
        public void Commission_GlazedWithUnpricedGlass_Refused()
        {
            BuildingDesignCatalog catalog = PresetCatalog();
            var commissioner = new NpcConstructionCommissioner();
            var prices = new FakePricePort();
            prices.Prices["lumber"] = 40;
            prices.Prices["door hardware"] = 45;
            var diag = new List<string>();
            NpcConstructionEvaluation glazed = commissioner.EvaluateCommission(
                catalog, "settler-cabin", 1, prices, 100000, 28,
                NpcConstructionReason.Overcrowding, "test", 10, diag, true);
            Assert.IsFalse(glazed.IsViable, "a glazed build with no priced glass is not viable");
            Assert.IsTrue(glazed.RefusalReason.Contains("no price"),
                "the refusal names the unpriced material: " + glazed.RefusalReason);
            Assert.IsTrue(glazed.RefusalReason.Contains("glass"),
                "the refusal names glass: " + glazed.RefusalReason);
        }

        [Test]
        public void Commission_Unglazed_ExcludesGlass()
        {
            BuildingDesignCatalog catalog = PresetCatalog();
            var commissioner = new NpcConstructionCommissioner();
            var prices = new FakePricePort();
            prices.Prices["lumber"] = 40;
            prices.Prices["door hardware"] = 45;
            var diag = new List<string>();
            NpcConstructionEvaluation evaluation = commissioner.EvaluateCommission(
                catalog, "settler-cabin", 1, prices, 100000, 28,
                NpcConstructionReason.Overcrowding, "test", 10, diag);
            Assert.IsTrue(evaluation.IsViable, string.Join("; ", diag));
            Assert.IsFalse(evaluation.Summary.Contains("glazed windows"));
        }

        [Test]
        public void Commission_GlazedProject_RecordPricesGlass()
        {
            BuildingDesignCatalog catalog = PresetCatalog();
            var commissioner = new NpcConstructionCommissioner();
            var executor = new ConstructionProjectExecutor();
            var prices = new FakePricePort();
            prices.Prices["lumber"] = 40;
            prices.Prices["door hardware"] = 45;
            prices.Prices["glass"] = 25;
            var diag = new List<string>();
            NpcConstructionEvaluation evaluation = commissioner.EvaluateCommission(
                catalog, "settler-cabin", 1, prices, 100000, 28,
                NpcConstructionReason.Overcrowding, "test", 10, diag, true);
            Assert.IsTrue(evaluation.IsViable, string.Join("; ", diag));
            NpcConstructionRecord record;
            ConstructionProject project = commissioner.CommissionProject(
                executor, catalog, 7, "settler-cabin", "parcel-9", prices, 10, diag, out record, true);
            Assert.IsNotNull(project, string.Join("; ", diag));
            Assert.IsNotNull(record);
            Assert.AreEqual(evaluation.EstimateCents, record.TotalEstimatedCostCents,
                "the record's estimate matches the glazed evaluation — the glass is in the books");
            Assert.IsTrue(record.Decisions[0].Contains("glazed windows"));
        }
    }
}
