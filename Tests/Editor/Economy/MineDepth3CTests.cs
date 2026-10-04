using EntityId = LandLedgers.Primitives.EntityId;

using System.Collections.Generic;
using LandLedgers.Economy;
using LandLedgers.Economy.Businesses.LumberYard;
using LandLedgers.Economy.Businesses.Mine;
using LandLedgers.Population;
using LandLedgers.Primitives;
using LandLedgers.World;
using NUnit.Framework;

namespace LandLedgers.Editor.Economy
{
    /// <summary>
    /// D3C: mine depth — grade-aware smelter settlement (Canon 21.7H), ore
    /// classes (21.7E), assay office desk (21.7F), cost-cause ledger (21.7Q),
    /// operating posture (21.7L), work-front allocation (21.7D), hoist
    /// equipment-asset bridge, and timber requisition wiring. Existing W8
    /// flat-price behavior is preserved: null terms keep the legacy path.
    /// </summary>
    [TestFixture]
    public sealed class MineDepth3CTests
    {
        private static MineOreStock StockWithLots(EntityIdRegistry registry, bool assaySecond)
        {
            var stock = new MineOreStock();
            var diag = new List<string>();
            var lot1 = MineOreLot.Create(registry, MineralResourceKind.Silver, 30, 3.0f,
                "shaft-1", "level-1", "vein-1", 400, "silver_ore");
            stock.ReceiveLot(lot1, diag);
            var lot2 = MineOreLot.Create(registry, MineralResourceKind.Silver, 20, 1.0f,
                "shaft-1", "level-2", "vein-1", 401, "silver_ore");
            stock.ReceiveLot(lot2, diag);
            if (assaySecond)
            {
                EntityId assayer = registry.Allocate(EntityKind.Person);
                string rejection = lot2.RecordAssay("assay-1", assayer, 402, 1.5f);
                Assert.IsNull(rejection);
            }
            return stock;
        }

        private static MineSmelterLink GradedLink()
        {
            var terms = new MineSmelterSettlementTerms(
                treatmentChargePerTonCents: 200,
                freightPerTonCents: 100,
                recoveryRate01: 0.9f,
                metalPricePerGradeUnitCents: 1000,
                unassayedDiscount01: 0.5f,
                termsNote: "test terms");
            return new MineSmelterLink("smelter-test-ag", "Test Smelting Works",
                MineralResourceKind.Silver, 1900, 21, terms);
        }

        // ---------- grade-aware settlement ----------

        [Test]
        public void Settlement_GradedTermsPayNetOfTreatmentAndFreight()
        {
            // The graded path: terms negotiated on the link and snapshotted at placement.
            var gradedOrder = new MineOreShipmentOrder("T1", "biz-mine-1", "Silver King", MineralResourceKind.Silver,
                40, GradedLink(), "shaft-1", 400);
            var summary = new MineShipmentGradeSummary(tonsAssayed: 20, weightedAssayedGrade: 1.5f,
                tonsUnassayed: 20, weightedEstimateGrade: 3.0f);
            gradedOrder.SetPlacementSummary(summary, GradedLink().SettlementTerms);

            var diag = new List<string>();
            MineSettlementBreakdown breakdown = GradedLink().SettlementTerms.ComputeSettlement(summary, 40, diag);
            // gross = 20*1.5*1000*0.9 + 20*3.0*1000*0.9*0.5 = 27000 + 27000 = 54000
            Assert.AreEqual(54000, breakdown.GrossCents);
            Assert.AreEqual(8000, breakdown.TreatmentCents);
            Assert.AreEqual(4000, breakdown.FreightCents);
            Assert.AreEqual(42000, breakdown.NetCents);

            var cash = new HouseholdLedger(70);
            var ledger = new MineCostLedger();
            var svc2 = new MineOreShipmentService(new List<MineOreShipmentOrder> { gradedOrder });
            string rejection = svc2.SettleDelivery("T1", cash, 421, ledger);
            Assert.IsNull(rejection);
            Assert.AreEqual(42000, cash.GetBalanceCents(), "smelter paid graded net");
            Assert.AreEqual(8000, ledger.TotalCentsByCause(MineCostCause.Treatment));
            Assert.AreEqual(4000, ledger.TotalCentsByCause(MineCostCause.Freight));
            Assert.IsTrue(gradedOrder.IsDelivered);
        }

        [Test]
        public void Settlement_LegacyFlatPathUnchangedWhenTermsNull()
        {
            var registry = new EntityIdRegistry();
            MineOreStock stock = StockWithLots(registry, assaySecond: false);
            var service = new MineOreShipmentService();
            MineOreShipmentOrder order = service.PlaceShipment(stock, "biz-mine-1", "Silver King",
                "smelter-omaha-ag", 40, "shaft-1", 400);
            Assert.AreEqual(40 * 1900, order.TotalCents);

            var cash = new HouseholdLedger(70);
            Assert.IsNull(service.SettleDelivery(order.OrderId, cash, 421));
            Assert.AreEqual(40 * 1900, cash.GetBalanceCents());
        }

        [Test]
        public void Settlement_NetFlooredAtZeroWhenChargesExceedValue()
        {
            var terms = new MineSmelterSettlementTerms(5000, 5000, 0.9f, 1000, 1f, "punitive");
            var summary = new MineShipmentGradeSummary(10, 1.0f, 0, 0f);
            var diag = new List<string>();
            MineSettlementBreakdown breakdown = terms.ComputeSettlement(summary, 10, diag);
            Assert.AreEqual(0, breakdown.NetCents, "net floored, never invoiced");
            Assert.IsNotEmpty(diag, "floor is diagnosed loudly");
        }

        [Test]
        public void GradeSummary_CapturedAtPlacementFromDispenseLines()
        {
            var registry = new EntityIdRegistry();
            MineOreStock stock = StockWithLots(registry, assaySecond: true);
            var service = new MineOreShipmentService();
            MineOreShipmentOrder order = service.PlaceShipment(stock, "biz-mine-1", "Silver King",
                "smelter-omaha-ag", 40, "shaft-1", 400);
            Assert.IsNotNull(order);
            // FIFO: 30 tons of lot1 (unassayed estimate 3.0) + 10 of lot2 (assayed 1.5)
            Assert.AreEqual(10, order.GradeSummary.TonsAssayed);
            Assert.AreEqual(1.5f, order.GradeSummary.WeightedAssayedGrade, 0.001f);
            Assert.AreEqual(30, order.GradeSummary.TonsUnassayed);
            Assert.AreEqual(3.0f, order.GradeSummary.WeightedEstimateGrade, 0.001f);
            Assert.AreEqual(0.25f, order.GradeSummary.AssayCoverage01, 0.001f);
        }

        // ---------- ore classes ----------

        [Test]
        public void OreClassifier_AssignsClassesByCutoffs()
        {
            var registry = new EntityIdRegistry();
            var cutoffs = new MineOreClassCutoffs(highGradeThreshold: 5f, millFeedThreshold: 1f, lowGradeThreshold: 0.2f);
            var high = MineOreLot.Create(registry, MineralResourceKind.Gold, 10, 8f, "s", "l", "v", 1, "gold_ore");
            var feed = MineOreLot.Create(registry, MineralResourceKind.Gold, 10, 2f, "s", "l", "v", 1, "gold_ore");
            var low = MineOreLot.Create(registry, MineralResourceKind.Gold, 10, 0.5f, "s", "l", "v", 1, "gold_ore");
            var waste = MineOreLot.Create(registry, MineralResourceKind.Gold, 10, 0.05f, "s", "l", "v", 1, "gold_ore");

            Assert.AreEqual(MineOreClass.HighGradeSpecial, MineOreClassifier.Classify(high, cutoffs));
            Assert.AreEqual(MineOreClass.NormalMillFeed, MineOreClassifier.Classify(feed, cutoffs));
            Assert.AreEqual(MineOreClass.LowGradeStockpile, MineOreClassifier.Classify(low, cutoffs));
            Assert.AreEqual(MineOreClass.Waste, MineOreClassifier.Classify(waste, cutoffs));
            Assert.IsFalse(MineOreClassifier.IsMarketable(MineOreClass.Waste));
            Assert.IsTrue(MineOreClassifier.IsMarketable(MineOreClass.LowGradeStockpile));
            Assert.AreEqual(MineOreClass.TreatmentSpecific,
                MineOreClassifier.Classify(feed, cutoffs, declaredTreatmentSpecific: true),
                "treatment-specific is declared, never inferred from grade");
        }

        [Test]
        public void ParcelAggregation_CombinesCompatibleLotsOnly()
        {
            var registry = new EntityIdRegistry();
            var cutoffs = new MineOreClassCutoffs(5f, 1f, 0.2f);
            var diag = new List<string>();
            var lot1 = MineOreLot.Create(registry, MineralResourceKind.Silver, 30, 2f, "s", "l", "v", 1, "silver_ore");
            var lot2 = MineOreLot.Create(registry, MineralResourceKind.Silver, 20, 4f, "s", "l", "v", 1, "silver_ore");

            MineShipmentParcel parcel = MineShipmentParcel.TryBuild("parcel-1",
                new List<MineOreLot> { lot1, lot2 }, cutoffs, diag);
            Assert.IsNotNull(parcel);
            Assert.AreEqual(50, parcel.TotalTons);
            Assert.AreEqual(2.8f, parcel.WeightedGrade, 0.001f);
            Assert.AreEqual(MineOreClass.NormalMillFeed, parcel.OreClass);

            var otherKind = MineOreLot.Create(registry, MineralResourceKind.Gold, 10, 2f, "s", "l", "v", 1, "gold_ore");
            Assert.IsNull(MineShipmentParcel.TryBuild("parcel-2",
                new List<MineOreLot> { lot1, otherKind }, cutoffs, diag), "mixed minerals refused");
            var wasteLot = MineOreLot.Create(registry, MineralResourceKind.Silver, 10, 0.01f, "s", "l", "v", 1, "silver_ore");
            Assert.IsNull(MineShipmentParcel.TryBuild("parcel-3",
                new List<MineOreLot> { wasteLot }, cutoffs, diag), "waste not parcelable");
        }

        // ---------- cost ledger ----------

        [Test]
        public void CostLedger_RecordsByCauseAndReports()
        {
            var ledger = new MineCostLedger();
            Assert.IsNull(ledger.RecordCost(10, MineCostCause.ExtractionLabor, 5000, "crew day shift", "wages-d10"));
            Assert.IsNull(ledger.RecordCost(10, MineCostCause.SupportTimber, 1200, "timber sets", "timber-s1-d10"));
            Assert.IsNotNull(ledger.RecordCost(10, MineCostCause.Unspecified, 100, "x", "y"), "unclassified refused");
            Assert.IsNotNull(ledger.RecordCost(10, MineCostCause.Freight, 0, "x", "y"), "zero refused");

            Assert.AreEqual(5000, ledger.TotalCentsByCause(MineCostCause.ExtractionLabor));
            Assert.AreEqual(1200, ledger.TotalCentsByCause(MineCostCause.SupportTimber));
            Assert.AreEqual(6200, ledger.TotalCents);
            string report = ledger.BuildCostCauseReport("Test Mine");
            StringAssert.Contains("Extraction labor", report);
            StringAssert.Contains("Support / timber", report);
        }

        [Test]
        public void CostFeed_BooksWagesAndTimberingFromRealRecords()
        {
            var registry = new EntityIdRegistry();
            var register = new MineLaborRegister();
            var diag = new List<string>();
            register.AssignMiner(new EntityId { Kind = EntityKind.EmploymentRelationship, Id = 1 },
                registry.Allocate(EntityKind.Person), MineRole.HardRockMiner, MineShift.Day, 300, 100, diag);
            var ledger = new MineCostLedger();

            Assert.IsNull(MineCostFeed.FeedShiftWages(register, ledger, 101, "day-101", diag));
            Assert.AreEqual(300, ledger.TotalCentsByCause(MineCostCause.ExtractionLabor));

            var record = new MineTimberingRecord("shaft-1", 10, 20, 101, new List<string> { "yard lot 5 | mill m1" });
            Assert.IsNull(MineCostFeed.FeedTimbering(record, ledger, 2500, diag));
            Assert.AreEqual(2500, ledger.TotalCentsByCause(MineCostCause.SupportTimber));

            var hoist = new MineHoist("hoist-1", HoistKind.SteamHoist, "shaft-1", 90);
            Assert.IsNull(MineCostFeed.FeedHoistMaintenance(hoist, ledger, 102, 800, "rope replacement", diag));
            Assert.AreEqual(800, ledger.TotalCentsByCause(MineCostCause.Maintenance));
        }

        // ---------- assay office ----------

        [Test]
        public void AssayOffice_QueuesRushFirstAndGatesCapacity()
        {
            var registry = new EntityIdRegistry();
            var office = new MineAssayOffice("Deadwood Assay Office", samplesPerDayCapacity: 1,
                standardFeeCents: 250, rushFeeCents: 500);
            var diag = new List<string>();
            EntityId lot = registry.Allocate(EntityKind.Lot);
            EntityId sampler = registry.Allocate(EntityKind.Person);

            MineAssaySample normal = office.RegisterSample(lot, sampler, "grab sample at face", 500, false, diag);
            MineAssaySample rush = office.RegisterSample(lot, sampler, "channel sample", 500, true, diag);
            Assert.IsNotNull(normal);
            Assert.IsNotNull(rush);

            Assert.AreEqual(rush.SampleId, office.NextSampleForAnalysis().SampleId, "rush jumps the queue");
            Assert.IsNull(office.BeginAnalysis(rush.SampleId, 501));
            Assert.IsNotNull(office.BeginAnalysis(normal.SampleId, 501), "bench capacity is real");
            Assert.IsNull(office.BeginAnalysis(normal.SampleId, 502), "next day has capacity again");
        }

        [Test]
        public void AssayOffice_CompleteAnalysisIssuesReportCollectsFee()
        {
            var registry = new EntityIdRegistry();
            var stock = new MineOreStock();
            var diag = new List<string>();
            var lot = MineOreLot.Create(registry, MineralResourceKind.Gold, 25, 0.4f,
                "shaft-1", "level-1", "vein-1", 500, "gold_ore");
            stock.ReceiveLot(lot, diag);

            var office = new MineAssayOffice("Deadwood Assay Office", 2, 250, 500);
            EntityId sampler = registry.Allocate(EntityKind.Person);
            EntityId assayer = registry.Allocate(EntityKind.Person);
            MineAssaySample sample = office.RegisterSample(lot.LotId, sampler, "representative channel sample", 500, false, diag);
            Assert.IsNull(office.BeginAnalysis(sample.SampleId, 501));

            var assayService = new MineAssayService();
            var payerCash = new HouseholdLedger(80);
            payerCash.RecordInflow(499, 10000, HouseholdIncomeSource.SaleProceeds, "seed", "seed", "seed");
            MineAssayReport report = office.CompleteAnalysis(sample.SampleId, stock, assayService,
                assayer, 0.62f, "fire assay", 501, payerCash, diag);

            Assert.IsNotNull(report);
            Assert.IsTrue(report.FeePaid);
            Assert.AreEqual(250, report.FeeCents);
            Assert.IsFalse(string.IsNullOrWhiteSpace(report.AssayId));
            Assert.AreEqual(9750, payerCash.GetBalanceCents());
            Assert.AreEqual(0.62f, stock.FindLot(lot.LotId).GradeValue, 0.001f, "assay service ran the shared path");
            StringAssert.Contains("0 disputed", office.ReputationLine());

            Assert.IsNull(office.DisputeReport(report.ReportId, "buyer questions the sample"));
            Assert.AreEqual(1, office.ReportsDisputed);
        }

        [Test]
        public void AssayOffice_RefusesAnonymousSamplesAndUnknownLots()
        {
            var registry = new EntityIdRegistry();
            var office = new MineAssayOffice("Office", 1, 100, 200);
            var diag = new List<string>();
            EntityId sampler = registry.Allocate(EntityKind.Person);
            Assert.IsNull(office.RegisterSample(EntityId.Invalid, sampler, "note", 1, false, diag),
                "anonymous samples refused");
            Assert.IsNull(office.RegisterSample(registry.Allocate(EntityKind.Lot), EntityId.Invalid, "note", 1, false, diag),
                "anonymous samplers refused");
            Assert.IsNotNull(office.BeginAnalysis("sample-999", 1), "unknown sample refused");
        }

        // ---------- posture, preservation, allocation ----------

        [Test]
        public void OperatingPosture_SetAndEffectiveDefault()
        {
            var state = MineRuntimeState.CreateDefault(MineralResourceKind.Coal);
            Assert.AreEqual(MineOperatingPosture.NormalProduction, state.OperatingPosture);
            state.SetOperatingPosture(MineOperatingPosture.TemporarilyIdle);
            Assert.AreEqual(MineOperatingPosture.TemporarilyIdle, state.OperatingPosture);
            state.SetOperatingPosture(MineOperatingPosture.Unspecified);
            Assert.AreEqual(MineOperatingPosture.TemporarilyIdle, state.OperatingPosture,
                "Unspecified never clears a real decision");

            state.SetPreservationBurden(new MinePreservationBurden(7, 14, 7, "wet shaft — keep pumping"));
            StringAssert.Contains("7 pump shifts/week", state.PreservationBurden.Describe());

            state.SetRestartReadinessPenalty(1.5f);
            Assert.AreEqual(1f, state.RestartReadinessPenalty01, "penalty clamped, caller-set");
        }

        [Test]
        public void WorkFrontAllocation_ValidatesProducingFrontsOnly()
        {
            var state = MineRuntimeState.CreateDefault(MineralResourceKind.Gold);
            var diag = new List<string>();
            MineShaft shaft = state.ShaftPlan.PlanShaft("Main", MineralResourceKind.Gold, 200, "collar");
            MineLevel producing = state.ShaftPlan.PlanLevel(shaft.ShaftId, 1, 100, string.Empty);
            producing.SetWorkingStatus(MineWorkingStatus.InOre);
            MineLevel driving = state.ShaftPlan.PlanLevel(shaft.ShaftId, 2, 150, string.Empty);

            Assert.IsNull(state.WorkFrontAllocation.SetAllocation(producing.LevelId, 0.6f, state.ShaftPlan, diag));
            Assert.IsNotNull(state.WorkFrontAllocation.SetAllocation(driving.LevelId, 0.4f, state.ShaftPlan, diag),
                "non-producing front refused");
            Assert.IsNotNull(state.WorkFrontAllocation.SetAllocation(producing.LevelId, 0.5f, state.ShaftPlan, diag),
                "over-allocation refused (0.6 + 0.5 > 1)");
            Assert.AreEqual(0.6f, state.WorkFrontAllocation.ShareFor(producing.LevelId), 0.001f);
        }

        // ---------- equipment bridge ----------

        [Test]
        public void EquipmentBridge_ShadowAssetsMatchTaskGateKinds()
        {
            var diag = new List<string>();
            Assert.IsNull(MineEquipmentBridge.VerifyKindWiring(diag), string.Join("; ", diag.ToArray()));

            var hoist = new MineHoist("hoist-1", HoistKind.SteamHoist, "shaft-1", 90);
            var shadow = MineEquipmentBridge.ShadowHoistAsset(hoist, "biz-mine-1");
            Assert.AreEqual("hoist", shadow.Kind);
            Assert.AreEqual("biz-mine-1", shadow.OwnerId);
            Assert.AreEqual("shaft-1", shadow.LocationId);

            hoist.RecordWear(0.5f);
            Assert.IsNull(MineEquipmentBridge.SyncHoistCondition(shadow, hoist, 100));
            Assert.AreEqual(0.5f, shadow.Condition01, 0.001f, "shadow synced from the hoist authority");
            Assert.IsNotEmpty(shadow.MaintenanceLog);

            var windlass = MineEquipmentBridge.ShadowWindlassAsset("shaft-2", "biz-mine-1", 0.8f);
            Assert.AreEqual("windlass", windlass.Kind);
            var pump = MineEquipmentBridge.ShadowPumpAsset("pump-1", "shaft-1", "biz-mine-1", 0.9f);
            Assert.AreEqual("pump", pump.Kind);
        }

        // ---------- timber requisition ----------

        [Test]
        public void TimberRequisition_WithdrawsRealLumberWithProvenance()
        {
            var registry = new EntityIdRegistry();
            var yard = new LumberYardLumberStock("yard-1");
            var diag = new List<string>();
            var lot = new LumberYardLumberLot
            {
                LotId = registry.Allocate(EntityKind.Lot),
                LumberUnits = 100,
                IsBootstrapEndowment = true,
                Species = "Ponderosa pine",
                SourceMillBusinessId = "mill-1",
            };
            Assert.IsNull(yard.ReceiveBootstrapEndowment(lot, diag));

            MineTimberWithdrawal withdrawal = MineSupplyRequisition.WithdrawTimberSets(
                yard, timberSetsNeeded: 10, lumberUnitsPerTimberSet: 8, callerDiagnostics: diag);
            Assert.IsNotNull(withdrawal);
            Assert.IsTrue(withdrawal.FullyCovered);
            Assert.AreEqual(10, withdrawal.TimberSetsCovered);
            Assert.AreEqual(80, withdrawal.LumberUnitsWithdrawn);
            Assert.IsNotEmpty(withdrawal.LumberProvenanceChains);
            Assert.AreEqual(20, yard.TotalLumberUnits, "withdrawal really left the yard");
        }

        [Test]
        public void TimberRequisition_ShortfallRefusedLoudlyNeverAutoOrdered()
        {
            var registry = new EntityIdRegistry();
            var yard = new LumberYardLumberStock("yard-1");
            var diag = new List<string>();
            var lot = new LumberYardLumberLot
            {
                LotId = registry.Allocate(EntityKind.Lot),
                LumberUnits = 10,
                IsBootstrapEndowment = true,
                Species = "Ponderosa pine",
            };
            Assert.IsNull(yard.ReceiveBootstrapEndowment(lot, diag));

            MineTimberWithdrawal withdrawal = MineSupplyRequisition.WithdrawTimberSets(
                yard, timberSetsNeeded: 10, lumberUnitsPerTimberSet: 8, callerDiagnostics: diag);
            Assert.IsNotNull(withdrawal);
            Assert.IsFalse(withdrawal.FullyCovered);
            Assert.AreEqual(9, withdrawal.ShortfallSets, "10 units cover 1 of 10 sets");
            Assert.IsTrue(diag.Count > 0, "shortfall diagnosed loudly");

            Assert.IsNull(MineSupplyRequisition.WithdrawTimberSets(
                yard, 5, 0, diag), "conversion factor required — never invented");
            Assert.IsNull(MineSupplyRequisition.WithdrawTimberSets(
                null, 5, 8, diag), "no phantom stock");
        }

        // ---------- save round trip ----------

        [Test]
        public void SaveRoundTrip_PreservesDepthState()
        {
            var registry = new EntityIdRegistry();
            var state = MineRuntimeState.CreateDefault(MineralResourceKind.Silver);
            var diag = new List<string>();
            var lot = MineOreLot.Create(registry, MineralResourceKind.Silver, 60, 2.0f,
                "shaft-1", "level-1", "vein-1", 400, "silver_ore");
            state.OreStock.ReceiveLot(lot, diag);

            state.SetOperatingPosture(MineOperatingPosture.ReducedOperation);
            state.SetPreservationBurden(new MinePreservationBurden(3, 30, 0, "dry shaft"));
            state.SetRestartReadinessPenalty(0.25f);
            Assert.IsNull(state.CostLedger.RecordCost(400, MineCostCause.Administration, 250,
                "assay office fee", "report-1"));

            MineOreShipmentService service = state.CreateShipmentService();
            MineOreShipmentOrder order = service.PlaceShipment(state.OreStock, "biz-mine-1",
                "Silver King Mine", "smelter-omaha-ag", 40, "shaft-1", 400);
            Assert.IsNotNull(order);

            MineRuntimeState restored = MineRuntimeState.FromSaveDto(state.CaptureSaveDto());
            Assert.AreEqual(MineOperatingPosture.ReducedOperation, restored.OperatingPosture);
            Assert.AreEqual(3, restored.PreservationBurden.PumpShiftsPerWeek);
            Assert.AreEqual(0.25f, restored.RestartReadinessPenalty01, 0.001f);
            Assert.AreEqual(250, restored.CostLedger.TotalCentsByCause(MineCostCause.Administration));
            Assert.AreEqual(1, restored.ShipmentOrders.Count);
            Assert.AreEqual(40, restored.ShipmentOrders[0].GradeSummary.TotalTons);
            Assert.IsNull(restored.ShipmentOrders[0].SettlementTerms, "flat links stay flat through save/load");
        }
    }
}
