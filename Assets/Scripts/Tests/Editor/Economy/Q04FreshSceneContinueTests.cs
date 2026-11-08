using System;
using System.Collections.Generic;
using System.Linq;
using LandLedgers.Economy;
using LandLedgers.Economy.Blacksmith;
using LandLedgers.Economy.BreadDrivers;
using LandLedgers.Economy.Businesses.Bakery;
using LandLedgers.Economy.Businesses.GeneralStore;
using LandLedgers.Economy.Core;
using LandLedgers.Economy.Equipment;
using LandLedgers.Economy.Financing;
using LandLedgers.Economy.Freight;
using LandLedgers.Economy.FreightFuel;
using LandLedgers.Economy.Logistics;
using LandLedgers.Economy.Ordering;
using LandLedgers.Economy.Trade;
using LandLedgers.Persistence;
using LandLedgers.Population;
using LandLedgers.Primitives;
using LandLedgers.Tasks;
using LandLedgers.Time;
using NUnit.Framework;

namespace LandLedgers.Tests.Economy
{
    /// <summary>
    /// Priority 5 Slice 0 — Q04 fresh-scene Continue pins (tests only).
    /// On a fresh-scene Continue the bootstrapper returns early before
    /// ConfigureLiveBakerySaveOwner, ConfigureMillbrookAidTable, and
    /// RegisterNotionsProcurement ever run, so SaveLoadManager's Pass 9
    /// state, Millbrook runtime, and procurement service are all null
    /// when LoadDefaultSlot restores. The coordinator then takes its
    /// null-state branches and drops the saved sections — with a
    /// diagnostic line each, and no crash. These pins record the drop
    /// exactly as the coordinator emits it, so Slices 1-3 have something
    /// to close. Roslyn harness only. No Unity compilation, Test Runner,
    /// PlayMode, or scene wiring is claimed by this file.
    ///
    /// NOTE on the "exact diagnostic" wording: the plan quotes the
    /// diagnostic text without the coordinator's "PassXlSliceSaveCoordinator: "
    /// prefix or the trailing Codex sentence. The emitted line is the
    /// plan's quoted text embedded verbatim in the longer line, so each
    /// test asserts BOTH: the joined diagnostics contain the plan's exact
    /// quoted substring, and the single diagnostic line mentioning the
    /// section equals the full line the source actually emits. Nothing
    /// is weakened — the full-line assertion is strictly stronger.
    /// </summary>
    [TestFixture]
    public sealed class Q04FreshSceneContinueTests
    {
        // The 9-argument Restore is the production call shape
        // (SaveLoadManager.LoadDefaultSlot). All holders null is the
        // fresh-scene Continue shape: no Pass 9 state, no Millbrook
        // runtime, no procurement service, no D3/Pass 10 state.
        private static List<string> RestoreWithNoHolders(PassXlSliceSaveDto dto)
        {
            return PassXlSliceSaveCoordinator.Restore(
                dto, null, null, null, null, null, null, null, null);
        }

        [Test]
        public void ContinueDrop_Pin_Pass9SectionPresent_NullState_LogsExactDiagnostic()
        {
            var dto = new PassXlSliceSaveDto
            {
                series2Pass9 = new Series2Pass9SaveDto(),
            };

            List<string> diagnostics = RestoreWithNoHolders(dto);

            string joined = string.Join("; ", diagnostics);
            Assert.IsTrue(joined.Contains(
                    "Pass 9 section present in save but no Series2Pass9SaveState supplied; Pass 9 state NOT restored."),
                "the Pass 9 drop diagnostic must appear verbatim; diag: " + joined);
            List<string> pass9Lines = diagnostics.Where(d => d.Contains("Pass 9")).ToList();
            Assert.AreEqual(1, pass9Lines.Count,
                "exactly one diagnostic mentions Pass 9, and it is the drop line; diag: " + joined);
            Assert.AreEqual(
                "PassXlSliceSaveCoordinator: Pass 9 section present in save but no Series2Pass9SaveState supplied; Pass 9 state NOT restored. Codex must set SaveLoadManager.Series2Pass9State before loading, exactly as for procurement.",
                pass9Lines[0],
                "the full emitted line, prefix and trailing sentence included");
            Assert.IsTrue(!diagnostics.Any(d => d.Contains("Series2Pass9Save: restore complete")),
                "no Pass 9 restore ran at all — the section was dropped, not partially restored; diag: " + joined);
            Assert.IsNotNull(dto.series2Pass9,
                "restore must not consume or clear the dropped section from the DTO");
        }

        [Test]
        public void ContinueDrop_Pin_MillbrookSectionPresent_NullRuntime_LogsExactDiagnostic()
        {
            var dto = new PassXlSliceSaveDto
            {
                millbrookAid = new OffMapAidBusinessSaveDto { BusinessId = "millbrook-aid-1" },
            };

            List<string> diagnostics = RestoreWithNoHolders(dto);

            string joined = string.Join("; ", diagnostics);
            Assert.IsTrue(joined.Contains(
                    "Millbrook aid section is present but no live aid runtime was supplied; state was not restored."),
                "the Millbrook drop diagnostic must appear verbatim; diag: " + joined);
            List<string> millbrookLines = diagnostics.Where(d => d.Contains("Millbrook")).ToList();
            Assert.AreEqual(1, millbrookLines.Count,
                "exactly one diagnostic mentions Millbrook, and it is the drop line; diag: " + joined);
            Assert.AreEqual(
                "PassXlSliceSaveCoordinator: Millbrook aid section is present but no live aid runtime was supplied; state was not restored.",
                millbrookLines[0],
                "the full emitted line, prefix included");
            Assert.IsTrue(!diagnostics.Any(d => d.Contains("restored Millbrook aid table")),
                "no Millbrook restore ran at all — the section was dropped, not partially restored; diag: " + joined);
            Assert.IsNotNull(dto.millbrookAid,
                "restore must not consume or clear the dropped section from the DTO");
        }

        [Test]
        public void ContinueDrop_Pin_ProcurementSectionPresent_NullService_LogsExactDiagnostic()
        {
            var dto = new PassXlSliceSaveDto
            {
                procurement = new NamedSkuProcurementSaveDto { BusinessId = "bakery-notions" },
            };

            List<string> diagnostics = RestoreWithNoHolders(dto);

            string joined = string.Join("; ", diagnostics);
            Assert.IsTrue(joined.Contains(
                    "no procurement service supplied; procurement state not restored."),
                "the procurement drop diagnostic must appear verbatim; diag: " + joined);
            List<string> procurementLines = diagnostics.Where(d => d.Contains("procurement")).ToList();
            Assert.AreEqual(1, procurementLines.Count,
                "exactly one diagnostic mentions procurement, and it is the drop line; diag: " + joined);
            Assert.AreEqual(
                "PassXlSliceSaveCoordinator: no procurement service supplied; procurement state not restored. Codex must register the production procurement service with SaveLoadManager before saving, or procurement will not persist.",
                procurementLines[0],
                "the full emitted line, prefix and trailing sentence included");
            Assert.IsTrue(!diagnostics.Any(d => d.Contains("restored off-map procurement")),
                "no procurement restore ran at all — the section was dropped, not partially restored; diag: " + joined);
            Assert.IsNotNull(dto.procurement,
                "restore must not consume or clear the dropped section from the DTO");
        }

        [Test]
        public void ContinueDrop_Pin_CoordinatorCapture_NullHolders_WritesNullSections()
        {
            // The 7-argument Capture is the production call shape
            // (SaveLoadManager.BuildSaveDto): procurement service,
            // Pass 9 state, Pass 10 state, D3 state, Millbrook runtime.
            // All null is the fresh-scene Continue shape — a player
            // who Continues and then saves overwrites the good save
            // with null sections (the save-after-Continue amplifier).
            PassXlSliceSaveDto dto = PassXlSliceSaveCoordinator.Capture(
                null, null, null, null, null, null, null);

            Assert.IsNotNull(dto, "capture always returns a DTO, never null");
            Assert.IsNull(dto.procurement,
                "a null procurement service captures a null procurement section");
            Assert.IsNull(dto.series2Pass9,
                "a null Pass 9 state captures a null Pass 9 section");
            Assert.IsNull(dto.millbrookAid,
                "a null Millbrook runtime captures a null Millbrook section");
        }

        // ---- Slice 1 (AUD-274): coordinator half -------------------
        //
        // PIN, NOT A REPRODUCING FAILURE — read before changing it.
        // This test passes before the Slice 1 wiring fix too: it drives
        // the 6-argument coordinator Restore with a live
        // Series2Pass9SaveState already supplied, and the coordinator
        // half is already sound (it restores when the holder exists).
        // What it pins is the half the production path must supply:
        // after the fix, SaveLoadManager.LoadDefaultSlot calls
        // FirstLedgerSliceBootstrapper.EnsurePass9SaveOwnerForRestore
        // between the shared-business restore and the Pass XL restore,
        // so the production restore runs this same coordinator path
        // with a live holder instead of taking the null-state drop
        // branch pinned by
        // ContinueDrop_Pin_Pass9SectionPresent_NullState. The in-window
        // call itself is harness-excluded
        // (FirstLedgerSliceBootstrapper.cs, SaveLoadManager.cs) and is
        // source-reviewed only; no Unity verification is claimed by
        // this test.

        [Test]
        public void ContinueWiring_Pass9State_RestoreWithSuppliedState_RestoresBakeryAndFreight()
        {
            var diag = new List<string>();

            // Source: a live Pass 9 state mid-work. The bin holds
            // 20 lb of flour AFTER the batch's custody was dispensed
            // (the custody lines below are the batch's own record of
            // what it already holds, not a second dispense from the
            // bin — no flour is created or double-counted here).
            var sourceBakery = new BakeryShopRuntime("bakery-1");
            Assert.IsNull(sourceBakery.FlourStock.ReceiveLot(new BakeryFlourLot
            {
                LotId = EntityId.For(EntityKind.Lot, 901),
                FlourName = BakeryBreadCatalog.FlourItemId,
                Units = 20,
                AcquiredDayIndex = 0,
                ImportOrderId = "test-flour-order",
                OriginName = "Test mill",
            }, diag), "the 20 lb flour lot must be received; diag: " + string.Join("; ", diag));
            var sourceDriver = new Series2BakeryProductionDriver(
                sourceBakery, new Series2InStockBakeryFuelSource(sourceBakery));
            List<string> plannedIds = sourceDriver.PlanBatches(
                BakeryBreadCatalog.BreadLoafId, 1, 2, diag);
            Assert.IsNotNull(plannedIds, "planning must succeed; diag: " + string.Join("; ", diag));
            Assert.AreEqual(1, plannedIds.Count);
            string batchId = plannedIds[0];
            BakeryDoughBatch sourceBatch = sourceBakery.Batches.First(b => b.BatchId == batchId);
            sourceBatch.Stage = BakeryBatchStage.Prepped;
            sourceBatch.FlourInCustody.Add(new BakeryFlourDispenseLine
            {
                LotId = EntityId.For(EntityKind.Lot, 901),
                FlourName = BakeryBreadCatalog.FlourItemId,
                UnitsTaken = 12,
                ProvenanceChain = "Test mill",
            });
            sourceBatch.NotionsUsed.Add(new BakeryFlourDispenseLine
            {
                LotId = EntityId.For(EntityKind.Lot, 902),
                FlourName = BakeryBreadCatalog.NotionsItemId,
                UnitsTaken = 1,
                ProvenanceChain = "Test notions",
            });

            var sourceFreight = new FreightProductionDriver();
            var freightDto = new FreightShipmentService.FreightServiceSaveDto();
            freightDto.Shipments.Add(new LogisticsShipmentState
            {
                shipmentId = "ship-q04-1",
                state = LogisticsShipmentStatus.InTransit,
                carrierBusinessInstanceId = "carrier-1",
                freightPayerBusinessInstanceId = "bakery-1",
                freightChargeCents = 125,
                loadApplied = true,
                deliveryApplied = false,
                freightRevenueCollected = false,
            }.CaptureSaveDto());
            sourceFreight.RestoreSaveDto(freightDto, null, diag);

            var source = new Series2Pass9SaveState
            {
                BakeryRuntime = sourceBakery,
                BakeryDriver = sourceDriver,
                FreightDriver = sourceFreight,
            };
            source.WholesaleSettlements.Add(new Pass3WholesaleSettlement
            {
                BakeryLotId = "lot-1",
                ProductId = "bread",
                Units = 10,
                UnitWholesalePriceCents = 50,
                PayerBusinessId = "bakery-1",
                PayeeBusinessId = "general-store-1",
                AmountCents = 500,
                Settled = true,
            });

            // Capture through the real coordinator, as BuildSaveDto does.
            PassXlSliceSaveDto dto = PassXlSliceSaveCoordinator.Capture(null, null, null, source);
            Assert.IsNotNull(dto.series2Pass9, "the source state must capture a Pass 9 section");
            Assert.IsNotNull(dto.series2Pass9.BakeryRuntime, "the Bakery runtime must be captured");
            Assert.IsNotNull(dto.series2Pass9.Freight, "the freight section must be captured");
            Assert.AreEqual(1, dto.series2Pass9.WholesaleSettlements.Count);

            // Target: fresh live holders with the SAME Bakery business
            // id (the restore refuses to overwrite a mismatched id),
            // as the Continue branch supplies after the Slice 1 fix.
            var targetBakery = new BakeryShopRuntime("bakery-1");
            var targetDriver = new Series2BakeryProductionDriver(
                targetBakery, new Series2InStockBakeryFuelSource(targetBakery));
            var targetFreight = new FreightProductionDriver();
            var target = new Series2Pass9SaveState
            {
                BakeryRuntime = targetBakery,
                BakeryDriver = targetDriver,
                FreightDriver = targetFreight,
            };

            List<string> restoreDiag = PassXlSliceSaveCoordinator.Restore(
                dto, null, null, null, target, null);
            string joined = string.Join("; ", restoreDiag);
            Assert.IsTrue(!restoreDiag.Any(d => d.Contains("Pass 9 section present in save but no Series2Pass9SaveState supplied")),
                "with a live state supplied the drop branch must not run; diag: " + joined);
            Assert.IsTrue(restoreDiag.Any(d => d.Contains("Series2Pass9Save: restore complete")),
                "the Pass 9 restore must run to completion; diag: " + joined);

            // Bakery: same batch id, bin 20 lb, custody 12 lb + 1 notion.
            Assert.AreEqual(2, target.BakeryDriver.PlannedDayIndex);
            Assert.AreEqual(1, target.BakeryDriver.PlannedBatchIds.Count);
            Assert.AreEqual(batchId, target.BakeryDriver.PlannedBatchIds[0],
                "the planned batch id must survive the restore verbatim");
            Assert.AreEqual(20, target.BakeryRuntime.FlourStock.UnitsOnHand(BakeryBreadCatalog.FlourItemId),
                "the flour bin must restore at 20 lb, not re-charged for the batch's custody");
            Assert.AreEqual(1, target.BakeryRuntime.Batches.Count, "exactly one active batch must restore");
            BakeryDoughBatch restoredBatch = target.BakeryRuntime.Batches[0];
            Assert.AreEqual(batchId, restoredBatch.BatchId);
            Assert.AreEqual(BakeryBatchStage.Prepped, restoredBatch.Stage);
            Assert.AreEqual(12, restoredBatch.FlourInCustody.Sum(l => l.UnitsTaken),
                "the batch must keep its 12 lb of flour in custody");
            Assert.AreEqual(1, restoredBatch.NotionsUsed.Sum(l => l.UnitsTaken),
                "the batch must keep its 1 notion in custody");

            // Freight: the mid-journey shipment, still undelivered.
            List<LogisticsShipmentState> shipments = target.FreightDriver.AllShipments();
            Assert.AreEqual(1, shipments.Count, "exactly one shipment must restore, never duplicated");
            Assert.AreEqual("ship-q04-1", shipments[0].ShipmentId);
            Assert.AreEqual(LogisticsShipmentStatus.InTransit, shipments[0].Status);
            Assert.IsTrue(shipments[0].loadApplied, "the load must stay applied");
            Assert.IsTrue(!shipments[0].deliveryApplied, "the shipment must stay undelivered");
            Assert.IsTrue(!shipments[0].freightRevenueCollected, "no revenue may be collected by the restore");
            Assert.AreEqual(125, shipments[0].freightChargeCents);

            // Settlement: the record survives with its settled flag.
            Assert.AreEqual(1, target.WholesaleSettlements.Count);
            Assert.AreEqual("lot-1", target.WholesaleSettlements[0].BakeryLotId);
            Assert.IsTrue(target.WholesaleSettlements[0].Settled);
            Assert.AreEqual(500, target.WholesaleSettlements[0].AmountCents);
        }

        // Builds the same source DTO the pin test above captures: a
        // live Pass 9 state mid-work (20 lb flour bin, one Prepped
        // batch holding 12 lb + 1 notion in custody, one InTransit
        // freight shipment, one settled wholesale record). Shared by
        // the amplifier and degraded-path pins so the DTO they restore
        // is the pin's DTO, not a second hand-built shape.
        private static PassXlSliceSaveDto BuildPinSourceDto(out string batchId)
        {
            var diag = new List<string>();
            var sourceBakery = new BakeryShopRuntime("bakery-1");
            Assert.IsNull(sourceBakery.FlourStock.ReceiveLot(new BakeryFlourLot
            {
                LotId = EntityId.For(EntityKind.Lot, 901),
                FlourName = BakeryBreadCatalog.FlourItemId,
                Units = 20,
                AcquiredDayIndex = 0,
                ImportOrderId = "test-flour-order",
                OriginName = "Test mill",
            }, diag), "the 20 lb flour lot must be received; diag: " + string.Join("; ", diag));
            var sourceDriver = new Series2BakeryProductionDriver(
                sourceBakery, new Series2InStockBakeryFuelSource(sourceBakery));
            List<string> plannedIds = sourceDriver.PlanBatches(
                BakeryBreadCatalog.BreadLoafId, 1, 2, diag);
            Assert.IsNotNull(plannedIds, "planning must succeed; diag: " + string.Join("; ", diag));
            Assert.AreEqual(1, plannedIds.Count);
            batchId = plannedIds[0];
            string plannedBatchId = batchId;
            BakeryDoughBatch sourceBatch = sourceBakery.Batches.First(b => b.BatchId == plannedBatchId);
            sourceBatch.Stage = BakeryBatchStage.Prepped;
            sourceBatch.FlourInCustody.Add(new BakeryFlourDispenseLine
            {
                LotId = EntityId.For(EntityKind.Lot, 901),
                FlourName = BakeryBreadCatalog.FlourItemId,
                UnitsTaken = 12,
                ProvenanceChain = "Test mill",
            });
            sourceBatch.NotionsUsed.Add(new BakeryFlourDispenseLine
            {
                LotId = EntityId.For(EntityKind.Lot, 902),
                FlourName = BakeryBreadCatalog.NotionsItemId,
                UnitsTaken = 1,
                ProvenanceChain = "Test notions",
            });

            var sourceFreight = new FreightProductionDriver();
            var freightDto = new FreightShipmentService.FreightServiceSaveDto();
            freightDto.Shipments.Add(new LogisticsShipmentState
            {
                shipmentId = "ship-q04-1",
                state = LogisticsShipmentStatus.InTransit,
                carrierBusinessInstanceId = "carrier-1",
                freightPayerBusinessInstanceId = "bakery-1",
                freightChargeCents = 125,
                loadApplied = true,
                deliveryApplied = false,
                freightRevenueCollected = false,
            }.CaptureSaveDto());
            sourceFreight.RestoreSaveDto(freightDto, null, diag);

            var source = new Series2Pass9SaveState
            {
                BakeryRuntime = sourceBakery,
                BakeryDriver = sourceDriver,
                FreightDriver = sourceFreight,
            };
            source.WholesaleSettlements.Add(new Pass3WholesaleSettlement
            {
                BakeryLotId = "lot-1",
                ProductId = "bread",
                Units = 10,
                UnitWholesalePriceCents = 50,
                PayerBusinessId = "bakery-1",
                PayeeBusinessId = "general-store-1",
                AmountCents = 500,
                Settled = true,
            });

            PassXlSliceSaveDto dto = PassXlSliceSaveCoordinator.Capture(null, null, null, source);
            Assert.IsNotNull(dto.series2Pass9, "the source state must capture a Pass 9 section");
            return dto;
        }

        [Test]
        public void ContinueWiring_Pass9State_RestoreThenCapture_SectionSurvivesSaveAfterContinue()
        {
            // Amplifier pin (original plan §0): a save after Continue
            // must no longer overwrite the good save with null Pass 9
            // sections. Restore the pin DTO into fresh live holders,
            // then capture through the real coordinator, as
            // BuildSaveDto does, and assert the section survives with
            // the same batch id and the same shipment id. Passes
            // before the wiring fix too — it pins the coordinator half;
            // the production half is the in-window holder creation.
            PassXlSliceSaveDto dto = BuildPinSourceDto(out string batchId);

            var targetBakery = new BakeryShopRuntime("bakery-1");
            var targetDriver = new Series2BakeryProductionDriver(
                targetBakery, new Series2InStockBakeryFuelSource(targetBakery));
            var target = new Series2Pass9SaveState
            {
                BakeryRuntime = targetBakery,
                BakeryDriver = targetDriver,
                FreightDriver = new FreightProductionDriver(),
            };
            List<string> restoreDiag = PassXlSliceSaveCoordinator.Restore(
                dto, null, null, null, target, null);
            string joined = string.Join("; ", restoreDiag);
            Assert.IsTrue(restoreDiag.Any(d => d.Contains("Series2Pass9Save: restore complete")),
                "the Pass 9 restore must run to completion; diag: " + joined);

            PassXlSliceSaveDto recaptured = PassXlSliceSaveCoordinator.Capture(null, null, null, target);
            Assert.IsNotNull(recaptured.series2Pass9,
                "a save after Continue must write a Pass 9 section, not null");
            Assert.IsNotNull(recaptured.series2Pass9.BakeryRuntime,
                "the re-saved section must carry the Bakery runtime");
            Assert.IsNotNull(recaptured.series2Pass9.Freight,
                "the re-saved section must carry the freight section");
            Assert.AreEqual(1, recaptured.series2Pass9.BakeryPlannedBatchIds.Count);
            Assert.AreEqual(batchId, recaptured.series2Pass9.BakeryPlannedBatchIds[0],
                "the planned batch id must survive the restore-then-save round trip verbatim");
            Assert.AreEqual(1, recaptured.series2Pass9.Freight.Shipments.Count);
            Assert.AreEqual("ship-q04-1", recaptured.series2Pass9.Freight.Shipments[0].shipmentId,
                "the freight shipment id must survive the restore-then-save round trip verbatim");
        }

        [Test]
        public void ContinueWiring_Pass9State_NoBakeryBusiness_RestoresFreightOnly_DiagnosesBakeryLegs()
        {
            // Degraded path (re-plan §2.4): a save carrying a Pass 9
            // section but no Bakery business instance (sold / never
            // formed) must restore the driverless legs and the freight
            // leg, diagnose the Bakery legs, and never crash or invent
            // a Bakery. The wrapper relies on this contract: it creates
            // the state + freight driver and lets
            // ConfigureLiveBakerySaveOwner warn-return. Passes before
            // and after the wiring fix — it pins the degraded contract.
            PassXlSliceSaveDto dto = BuildPinSourceDto(out string batchId);
            Assert.IsNotNull(dto.series2Pass9.BakeryRuntime,
                "the DTO must actually carry a Bakery runtime section for this test to mean anything");
            Assert.AreEqual(1, dto.series2Pass9.BakeryPlannedBatchIds.Count);
            Assert.AreEqual(batchId, dto.series2Pass9.BakeryPlannedBatchIds[0]);

            var target = new Series2Pass9SaveState
            {
                FreightDriver = new FreightProductionDriver(),
            };
            List<string> restoreDiag = PassXlSliceSaveCoordinator.Restore(
                dto, null, null, null, target, null);
            string joined = string.Join("; ", restoreDiag);
            Assert.IsTrue(restoreDiag.Any(d => d.Contains("Series2Pass9Save: restore complete")),
                "the Pass 9 restore must run to completion even without a Bakery; diag: " + joined);
            Assert.IsTrue(joined.Contains("no live runtime owner was supplied"),
                "the Bakery runtime leg must be diagnosed as not restored; diag: " + joined);
            Assert.IsTrue(joined.Contains("no bakery driver supplied"),
                "the planned-batch leg must be diagnosed as not restored; diag: " + joined);

            List<LogisticsShipmentState> shipments = target.FreightDriver.AllShipments();
            Assert.AreEqual(1, shipments.Count, "exactly one shipment must restore, never duplicated");
            Assert.AreEqual("ship-q04-1", shipments[0].ShipmentId);
            Assert.AreEqual(LogisticsShipmentStatus.InTransit, shipments[0].Status);
            Assert.IsTrue(!shipments[0].deliveryApplied, "the shipment must stay undelivered");
            Assert.IsNull(target.BakeryRuntime, "no Bakery runtime may be invented on the degraded path");
            Assert.IsNull(target.BakeryDriver, "no Bakery driver may be invented on the degraded path");
        }

        [Test]
        public void ContinueWiring_Pass9State_WholesaleIntakePresent_DeliveredLotGuardRestores()
        {
            // Guard pin (re-plan §1.4): the delivered-lot guard
            // restores only when the intake instance exists, so the
            // wrapper creates it (mirroring
            // SimulationDrivers.cs:7173). Assertion surface verified
            // in source first: Pass3WholesaleIntake exposes the public
            // DeliveredLotIds() read surface; no accessor was invented.
            // A Capture round trip is asserted alongside it.
            var dto = new PassXlSliceSaveDto
            {
                series2Pass9 = new Series2Pass9SaveDto
                {
                    WholesaleDeliveredLotIds = new List<string> { "lot-delivered-1" },
                },
            };

            var withIntake = new Series2Pass9SaveState
            {
                WholesaleIntake = new Pass3WholesaleIntake(),
            };
            List<string> restoreDiag = PassXlSliceSaveCoordinator.Restore(
                dto, null, null, null, withIntake, null);
            string joined = string.Join("; ", restoreDiag);
            Assert.IsTrue(restoreDiag.Any(d => d.Contains("Series2Pass9Save: restore complete")),
                "the Pass 9 restore must run to completion; diag: " + joined);
            List<string> delivered = withIntake.WholesaleIntake.DeliveredLotIds();
            Assert.AreEqual(1, delivered.Count, "exactly the one delivered lot id must restore");
            Assert.AreEqual("lot-delivered-1", delivered[0]);
            PassXlSliceSaveDto recaptured = PassXlSliceSaveCoordinator.Capture(null, null, null, withIntake);
            Assert.IsNotNull(recaptured.series2Pass9, "the state must capture a Pass 9 section");
            Assert.AreEqual(1, recaptured.series2Pass9.WholesaleDeliveredLotIds.Count);
            Assert.AreEqual("lot-delivered-1", recaptured.series2Pass9.WholesaleDeliveredLotIds[0],
                "the delivered-lot guard must survive the restore-then-save round trip");

            // Companion: without the intake the guard is NOT restored,
            // which is why the wrapper creates it.
            var withoutIntake = new Series2Pass9SaveState();
            List<string> noIntakeDiag = PassXlSliceSaveCoordinator.Restore(
                dto, null, null, null, withoutIntake, null);
            string noIntakeJoined = string.Join("; ", noIntakeDiag);
            Assert.IsTrue(noIntakeJoined.Contains("delivered-lot guard NOT restored"),
                "without an intake the guard loss must be diagnosed; diag: " + noIntakeJoined);
        }

        // ---- Slice 2 (AUD-275): coordinator half -------------------
        //
        // PIN, NOT A REPRODUCING FAILURE — read before changing it.
        // These tests pass before the Slice 2 wiring fix too: they drive
        // the 9-argument coordinator Restore with a live
        // OffMapAidBusinessRuntime already supplied, and the coordinator
        // half is already sound (it imports when the runtime exists).
        // What they pin is the half the production path must supply:
        // after the fix, the Continue branch calls
        // ConfigureMillbrookAidTable(continueDrivers) before
        // LoadDefaultSlot, so the production restore runs this same
        // coordinator path with a live runtime instead of taking the
        // null-runtime drop branch pinned by
        // ContinueDrop_Pin_MillbrookSectionPresent_NullRuntime. The
        // Continue-branch call itself is harness-excluded
        // (FirstLedgerSliceBootstrapper.cs) and is source-reviewed
        // only; no Unity verification is claimed by these tests.

        // The Millbrook definition, copied from the
        // MillbrookRuntimeResolutionTests fixture (values copied from
        // MillbrookGrainCo.asset: 230 lb starting, 48c/sack, 10 lb per
        // sack, 18 bakery units per sack), never re-derived.
        private static OffMapAidBusinessRuntime NewMillbrookSupplier()
        {
            var definition = new OffMapAidBusinessDefinition
            {
                BusinessId = "millbrook_grain_co",
                DisplayName = "Millbrook Grain Co.",
                PickupRouteMilesFromTown = 50f,
                Inventory = new List<OffMapAidInventoryRow>
                {
                    new OffMapAidInventoryRow
                    {
                        ItemId = "bakery_flour_sack",
                        DisplayName = "Flour",
                        InventoryUnit = "lb",
                        StartingUnits = 230,
                        OrderUnit = "10 lb sack",
                        InventoryUnitsPerOrderUnit = 10,
                        BakeryUnitsPerOrderUnit = 18,
                        OrderPriceCents = 48,
                    },
                },
            };
            return new OffMapAidBusinessRuntime(definition);
        }

        private sealed class FakeMillbrookBuyerCashPort : IBusinessCashPort
        {
            public int BalanceCents { get; private set; }

            public FakeMillbrookBuyerCashPort(int balanceCents) => BalanceCents = balanceCents;

            public string TryDebit(int amountCents, string reason)
            {
                if (amountCents <= 0 || amountCents > BalanceCents)
                    return "test cash port refused debit";
                BalanceCents -= amountCents;
                return null;
            }

            public string Credit(int amountCents, string reason)
            {
                if (amountCents <= 0) return "test cash port refused credit";
                BalanceCents += amountCents;
                return null;
            }
        }

        // Places the fixture's 1-sack opening order through the real
        // production path. CORRECTION to the plan's parenthetical
        // ("authored definition, 230 lb flour, 0c cash, one uncommitted
        // 1-sack opening lot"): 230 lb / 0c are the definition's
        // STARTING values before the order. Placing the opening order
        // debits 10 lb and credits 48c immediately (pinned by
        // MillbrookRuntimeResolutionTests.OpeningOrder_SucceedsOnce),
        // so the live runtime holding the opening lot reads 220 lb /
        // 48c, not 230 lb / 0c. The post-restore assertions below are
        // exactly as the plan specifies; only the live pre-restore
        // reading is stated as production actually gives it.
        private static OffMapAidBusinessRuntime PlaceMillbrookOpeningOrder(out FakeMillbrookBuyerCashPort bakeryCash)
        {
            var supplier = NewMillbrookSupplier();
            bakeryCash = new FakeMillbrookBuyerCashPort(20000);
            Assert.IsNull(supplier.TryPlaceOrder(
                "opening-flour-b1", "bakery_001", "Glen Pierce", 1, bakeryCash, 0, out OffMapAidCargoLot lot),
                "the opening 1-sack Millbrook order should place");
            Assert.IsNotNull(lot, "the opening order must produce a lot");
            Assert.AreEqual(220, supplier.GetInventoryUnits("bakery_flour_sack"), "230 lb - 10 lb");
            Assert.AreEqual(48, supplier.CashCents, "supplier cash 0 -> 48c");
            Assert.AreEqual(1, supplier.Lots.Count, "exactly the one opening lot");
            return supplier;
        }

        // The saved Millbrook section for the restore pin: 160 lb flour
        // (16 sacks x 10 lb), 768c retained cash (16 x the authored
        // 48c/sack), one 16-sack clamped lot, uncommitted and
        // unreceived — the Slice 4b clamp figures (supplier 160 lb,
        // request 18 -> 16-sack lot, 160 lb, 768c), arithmetic on
        // authored values, nothing invented.
        private static OffMapAidBusinessSaveDto BuildClampedLotMillbrookDto()
        {
            return new OffMapAidBusinessSaveDto
            {
                BusinessId = "millbrook_grain_co",
                CashCents = 768,
                InventoryItemIds = new List<string> { "bakery_flour_sack" },
                InventoryUnits = new List<int> { 160 },
                Lots = new List<OffMapAidCargoLot>
                {
                    new OffMapAidCargoLot
                    {
                        LotId = "aidlot-millbrook_grain_co:flour-reorder-d57-n16",
                        OrderId = "millbrook_grain_co:flour-reorder-d57-n16",
                        SkuId = "bakery_flour_sack",
                        SupplierBusinessId = "millbrook_grain_co",
                        BuyerBusinessId = "bakery_001",
                        OrderUnit = "10 lb sack",
                        PackageUnits = 16,
                        InventoryUnits = 160,
                        InventoryUnit = "lb",
                        BakeryUnits = 288,
                        TotalCostCents = 768,
                        CreatedDayIndex = 57,
                        SourceCommitted = false,
                        Received = false,
                    },
                },
            };
        }

        [Test]
        public void ContinueWiring_Millbrook_RestoreWithSuppliedRuntime_RestoresStockCashLots()
        {
            // Coordinator pin (re-plan §3.3 test 1): with a live
            // runtime supplied, the coordinator restores the saved
            // Millbrook section. The live runtime holds the opening
            // lot; the saved section holds the clamped lot — after
            // restore exactly the saved lot must remain (clear-then-
            // load), never both. Passes before the wiring fix too.
            OffMapAidBusinessRuntime runtime = PlaceMillbrookOpeningOrder(out _);
            var dto = new PassXlSliceSaveDto { millbrookAid = BuildClampedLotMillbrookDto() };

            List<string> restoreDiag = PassXlSliceSaveCoordinator.Restore(
                dto, null, null, null, null, null, null, null, runtime);
            string joined = string.Join("; ", restoreDiag);
            Assert.IsTrue(!restoreDiag.Any(d => d.Contains("Millbrook aid section is present but no live aid runtime was supplied")),
                "with a live runtime supplied the drop branch must not run; diag: " + joined);
            Assert.IsTrue(joined.Contains("restored Millbrook aid table (160 lb flour, 768c retained cash, 1 cargo lots)"),
                "the restore must report the saved values; diag: " + joined);

            Assert.AreEqual(160, runtime.GetInventoryUnits("bakery_flour_sack"),
                "stock must restore at the saved 160 lb, not the live 220 lb");
            Assert.AreEqual(768, runtime.CashCents,
                "cash must restore at the saved 768c, not the live 48c");
            Assert.AreEqual(1, runtime.Lots.Count, "exactly the one saved lot must remain, never duplicated");
            OffMapAidCargoLot lot = runtime.Lots[0];
            Assert.AreEqual("aidlot-millbrook_grain_co:flour-reorder-d57-n16", lot.LotId,
                "the saved clamped lot must restore with its id");
            Assert.AreEqual(16, lot.PackageUnits, "the clamped lot holds 16 sacks");
            Assert.AreEqual(160, lot.InventoryUnits, "the clamped lot holds 160 lb");
            Assert.AreEqual(768, lot.TotalCostCents, "the clamped lot costs 16 x 48c");
            Assert.IsTrue(!lot.SourceCommitted, "the clamped lot must stay uncommitted");
            Assert.IsTrue(!lot.Received, "the clamped lot must stay unreceived");
            Assert.IsTrue(!runtime.Lots.Any(l => l.LotId == "aidlot-millbrook_grain_co:opening-flour-b1"),
                "the live opening lot must be cleared by the restore, not kept alongside the saved lot");
        }

        [Test]
        public void ContinueWiring_Millbrook_RestoreThenCapture_SectionSurvivesSaveAfterContinue()
        {
            // Amplifier pin (re-plan §3.3 test 2, original plan §0): a
            // save after Continue must no longer overwrite the good
            // save with a null Millbrook section. Restore the pin DTO
            // into a fresh live runtime, then capture through the real
            // coordinator, as BuildSaveDto does. Passes before the
            // wiring fix too — it pins the coordinator half.
            OffMapAidBusinessRuntime runtime = PlaceMillbrookOpeningOrder(out _);
            var dto = new PassXlSliceSaveDto { millbrookAid = BuildClampedLotMillbrookDto() };

            List<string> restoreDiag = PassXlSliceSaveCoordinator.Restore(
                dto, null, null, null, null, null, null, null, runtime);
            string joined = string.Join("; ", restoreDiag);
            Assert.IsTrue(joined.Contains("restored Millbrook aid table"),
                "the Millbrook restore must run; diag: " + joined);

            PassXlSliceSaveDto recaptured = PassXlSliceSaveCoordinator.Capture(
                null, null, null, null, null, null, runtime);
            Assert.IsNotNull(recaptured.millbrookAid,
                "a save after Continue must write a Millbrook section, not null");
            Assert.AreEqual("millbrook_grain_co", recaptured.millbrookAid.BusinessId);
            Assert.AreEqual(160, recaptured.millbrookAid.InventoryUnits[recaptured.millbrookAid.InventoryItemIds.IndexOf("bakery_flour_sack")],
                "the re-saved section must carry the restored 160 lb stock");
            Assert.AreEqual(768, recaptured.millbrookAid.CashCents,
                "the re-saved section must carry the restored 768c cash");
            Assert.AreEqual(1, recaptured.millbrookAid.Lots.Count,
                "the re-saved section must carry exactly the one restored lot");
            Assert.AreEqual("aidlot-millbrook_grain_co:flour-reorder-d57-n16", recaptured.millbrookAid.Lots[0].LotId,
                "the clamped lot id must survive the restore-then-save round trip verbatim");
        }

        [Test]
        public void ContinueWiring_Millbrook_OldSaveNoSection_RestoresAsStartingStock()
        {
            // Old-save contract (re-plan §3.3 test 3, §1.9): a save
            // with no Millbrook section restores as the definition's
            // starting stock with cash 0 and no lots — and the next
            // save writes a Millbrook section at starting units. This
            // mirrors the D3 control case and the New Game default; it
            // is not a defect. The live runtime starts with the opening
            // lot placed, so the reset is visible, not vacuous.
            OffMapAidBusinessRuntime runtime = PlaceMillbrookOpeningOrder(out _);
            var dto = new PassXlSliceSaveDto { millbrookAid = null };

            List<string> restoreDiag = PassXlSliceSaveCoordinator.Restore(
                dto, null, null, null, null, null, null, null, runtime);
            string joined = string.Join("; ", restoreDiag);
            Assert.IsTrue(joined.Contains("restored Millbrook aid table"),
                "the restore diagnostic must name the Millbrook table; diag: " + joined);
            Assert.IsTrue(!restoreDiag.Any(d => d.Contains("Millbrook aid section is present but no live aid runtime was supplied")),
                "no drop diagnostic may fire when a live runtime was supplied; diag: " + joined);

            Assert.AreEqual(230, runtime.GetInventoryUnits("bakery_flour_sack"),
                "an old save restores as the definition's 230 lb starting stock");
            Assert.AreEqual(0, runtime.CashCents, "an old save restores with cash 0");
            Assert.AreEqual(0, runtime.Lots.Count, "an old save restores with no lots");

            PassXlSliceSaveDto recaptured = PassXlSliceSaveCoordinator.Capture(
                null, null, null, null, null, null, runtime);
            Assert.IsNotNull(recaptured.millbrookAid,
                "the next save writes a Millbrook section at starting units (re-plan §1.9)");
            Assert.AreEqual(230, recaptured.millbrookAid.InventoryUnits[recaptured.millbrookAid.InventoryItemIds.IndexOf("bakery_flour_sack")]);
            Assert.AreEqual(0, recaptured.millbrookAid.CashCents);
            Assert.AreEqual(0, recaptured.millbrookAid.Lots.Count);
        }

        [Test]
        public void ContinueWiring_Millbrook_MismatchedBusinessId_RefusesAndKeepsLiveState()
        {
            // Refusal visibility (re-plan §3.3 test 4, original plan
            // §2 item 6): a saved section for a different supplier
            // must refuse with the "does not match" diagnostic and
            // leave the live stock, cash, and lots untouched.
            OffMapAidBusinessRuntime runtime = PlaceMillbrookOpeningOrder(out _);
            var dto = new PassXlSliceSaveDto
            {
                millbrookAid = new OffMapAidBusinessSaveDto
                {
                    BusinessId = "other_supplier",
                    CashCents = 999,
                    InventoryItemIds = new List<string> { "bakery_flour_sack" },
                    InventoryUnits = new List<int> { 5 },
                },
            };

            List<string> restoreDiag = PassXlSliceSaveCoordinator.Restore(
                dto, null, null, null, null, null, null, null, runtime);
            string joined = string.Join("; ", restoreDiag);
            Assert.IsTrue(joined.Contains("does not match"),
                "the refusal diagnostic must name the BusinessId mismatch; diag: " + joined);
            Assert.IsTrue(joined.Contains("Off-map aid save refused: saved supplier 'other_supplier' does not match 'millbrook_grain_co'."),
                "the full refusal line must be the coordinator's diagnostic; diag: " + joined);

            Assert.AreEqual(220, runtime.GetInventoryUnits("bakery_flour_sack"),
                "live stock must stay at the pre-restore 220 lb after a refused import");
            Assert.AreEqual(48, runtime.CashCents,
                "live cash must stay at the pre-restore 48c after a refused import");
            Assert.AreEqual(1, runtime.Lots.Count, "live lots must stay at exactly the opening lot");
            Assert.AreEqual("aidlot-millbrook_grain_co:opening-flour-b1", runtime.Lots[0].LotId,
                "the live opening lot must be untouched by the refused import");
        }

        // ---- Slice 3 (procurement on Continue): coordinator half ----
        //
        // PIN, NOT A REPRODUCING FAILURE — read before changing it.
        // These tests pass before the Slice 3 wiring fix too: they drive
        // the 9-argument coordinator Restore with a live
        // OffMapNamedSkuProcurementService already supplied, and the
        // coordinator half is already sound (it clears then imports when
        // the service exists). What they pin is the half the production
        // path must supply: after the fix, SaveLoadManager.LoadDefaultSlot
        // calls FirstLedgerSliceBootstrapper.EnsureNotionsProcurementForRestore
        // between the shared-business restore and the Pass XL restore,
        // immediately after EnsurePass9SaveOwnerForRestore, so the
        // production restore runs this same coordinator path with a live
        // notions-only service instead of taking the null-service drop
        // branch pinned by
        // ContinueDrop_Pin_ProcurementSectionPresent_NullService. The
        // in-window call itself is harness-excluded
        // (FirstLedgerSliceBootstrapper.cs, SaveLoadManager.cs) and is
        // source-reviewed only; no Unity verification is claimed by
        // these tests.

        // Builds the fresh notions-only service exactly as
        // RegisterNotionsProcurement does: a fresh ExternalMarketAuthority
        // carrying only the notions counterparty, with terms read from
        // the catalog's authored registration
        // (BakeryFlourSupply.EnsureBakeryImportables: 10c/unit, 14
        // transit days, finite supply FiniteExternalOrderUnits) — never
        // hardcoded here — and the real Series2BusinessRuntimeCashPort
        // over a live BusinessRuntimeState, as production passes the
        // store's RuntimeState. The horse-feed counterparty is NOT on
        // this market: in editor/development builds the dev feed
        // registration owns a shared market, but on the release path
        // (and on this in-window Continue path) the service is
        // notions-only, and saved horse-feed orders restore as data
        // anyway (test 3 pins that ImportState does not validate saved
        // orders against the market's counterparties).
        private static OffMapNamedSkuProcurementService NewNotionsOnlyService(
            out ExternalMarketAuthority market, out ImportMaterial notionsMaterial)
        {
            market = new ExternalMarketAuthority();
            var importDiag = new List<string>();
            BakeryFlourSupply.EnsureBakeryImportables(importDiag);
            notionsMaterial = ImportCatalog.Get(BakeryFlourSupply.NotionsMaterialId);
            Assert.IsNotNull(notionsMaterial,
                "the notions import material must be registered by the catalog fixture; diag: "
                + string.Join("; ", importDiag));
            var notionsCounterparty = new ExternalCounterparty
            {
                CounterpartyId = BakeryNotionsDailySupply.NotionsSupplierId,
                DisplayName = BakeryNotionsDailySupply.NotionsSupplierName,
                TransitDays = notionsMaterial.TransitDays,
                InitialBudgetCents = 0,
                SupplyUnits = new Dictionary<string, int>
                {
                    [BakeryFlourSupply.NotionsMaterialId] =
                        BakeryNotionsDailySupply.FiniteExternalOrderUnits,
                },
            };
            Assert.IsNull(market.RegisterCounterparty(notionsCounterparty),
                "the notions counterparty must register on the fresh market");
            return new OffMapNamedSkuProcurementService(
                "general-store-1", market,
                new Series2BusinessRuntimeCashPort(new BusinessRuntimeState()));
        }

        // The saved procurement section for the restore pin: one notions
        // order (order day 1, arrival day 15 = order day + the catalog's
        // 14 transit days), one lot, and one delivered sale id. Order
        // price is the catalog's 10c/unit; no freight surcharge (no
        // freight rate is authored for the railhead route).
        private static NamedSkuProcurementSaveDto BuildNotionsProcurementDto()
        {
            return new NamedSkuProcurementSaveDto
            {
                BusinessId = "general-store-1",
                Orders = new List<NamedSkuOrder>
                {
                    new NamedSkuOrder
                    {
                        OrderId = "notions-order-1",
                        BusinessId = "general-store-1",
                        SkuId = BakeryFlourSupply.NotionsMaterialId,
                        UnitsOrdered = 1,
                        UnitGoodsCostCents = 10,
                        FreightSurchargeCents = 0,
                        TotalCostCents = 10,
                        SupplierId = BakeryNotionsDailySupply.NotionsSupplierId,
                        SupplierName = BakeryNotionsDailySupply.NotionsSupplierName,
                        OrderDayIndex = 1,
                        ExpectedArrivalDayIndex = 15,
                        ReceiptId = "receipt-notions-1",
                        Stage = NamedSkuOrderStage.Ordered,
                        PaymentMode = NamedSkuPaymentMode.CashOnArrival,
                    },
                },
                Lots = new List<NamedSkuLot>
                {
                    new NamedSkuLot
                    {
                        LotId = "notions-lot-1",
                        SkuId = BakeryFlourSupply.NotionsMaterialId,
                        BusinessId = "general-store-1",
                        OrderId = "notions-order-1",
                        ReceiptId = "receipt-notions-1",
                        QuantityUnits = 1,
                        RemainingUnits = 1,
                        TotalCostCents = 10,
                        UnitCostCents = 10,
                        CreatedDayIndex = 15,
                    },
                },
                DeliveredSaleIds = new List<string> { "sale-notions-1" },
            };
        }

        [Test]
        public void ContinueWiring_Procurement_RestoreWithSuppliedService_RestoresOrdersAndLots()
        {
            // Coordinator pin (re-plan §4.3 test 1): with a live
            // notions-only service supplied, the coordinator restores
            // the saved procurement section, and a second restore of
            // the same DTO changes nothing (idempotent). Passes before
            // the wiring fix too.
            OffMapNamedSkuProcurementService service =
                NewNotionsOnlyService(out _, out _);
            var dto = new PassXlSliceSaveDto { procurement = BuildNotionsProcurementDto() };

            List<string> restoreDiag = PassXlSliceSaveCoordinator.Restore(
                dto, null, null, service, null, null, null, null, null);
            string joined = string.Join("; ", restoreDiag);
            Assert.IsTrue(!restoreDiag.Any(d => d.Contains("no procurement service supplied")),
                "with a live service supplied the drop branch must not run; diag: " + joined);
            Assert.IsTrue(joined.Contains(
                    "restored off-map procurement for business 'general-store-1' (1 orders, 1 lots, 0 sales, 1 delivered)"),
                "the restore must report the saved counts; diag: " + joined);

            Assert.AreEqual(1, service.Orders.Count, "exactly the one saved notions order must restore");
            NamedSkuOrder order = service.Orders[0];
            Assert.AreEqual("notions-order-1", order.OrderId);
            Assert.AreEqual(BakeryFlourSupply.NotionsMaterialId, order.SkuId);
            Assert.AreEqual(1, order.OrderDayIndex, "the order day must restore verbatim");
            Assert.AreEqual(15, order.ExpectedArrivalDayIndex,
                "the arrival day must restore verbatim (order day 1 + 14 transit days)");
            Assert.AreEqual(1, service.Lots.Count, "exactly the one saved lot must restore");
            Assert.AreEqual("notions-lot-1", service.Lots[0].LotId);
            Assert.AreEqual(1, service.Lots[0].RemainingUnits, "the lot must stay unconsumed");
            Assert.AreEqual(1, service.DeliveredSaleIds.Count, "exactly the one delivered mark must restore");
            Assert.AreEqual("sale-notions-1", service.DeliveredSaleIds[0]);
            Assert.IsTrue(service.IsSaleDelivered("sale-notions-1"),
                "the delivered mark must answer as delivered");

            // Idempotent: restoring the same DTO again changes nothing.
            List<string> secondDiag = PassXlSliceSaveCoordinator.Restore(
                dto, null, null, service, null, null, null, null, null);
            string secondJoined = string.Join("; ", secondDiag);
            Assert.IsTrue(secondJoined.Contains(
                    "restored off-map procurement for business 'general-store-1' (1 orders, 1 lots, 0 sales, 1 delivered)"),
                "the second restore must report the same counts; diag: " + secondJoined);
            Assert.AreEqual(1, service.Orders.Count, "a second restore must not duplicate the order");
            Assert.AreEqual(1, service.Lots.Count, "a second restore must not duplicate the lot");
            Assert.AreEqual(1, service.DeliveredSaleIds.Count, "a second restore must not duplicate the delivered mark");
        }

        [Test]
        public void ContinueWiring_Procurement_RestoreThenCapture_SectionSurvivesSaveAfterContinue()
        {
            // Amplifier pin (re-plan §4.3 test 2, original plan §0): a
            // save after Continue must no longer overwrite the good
            // save with a null procurement section. Restore the pin DTO
            // into a fresh live service, then capture through the real
            // coordinator, as BuildSaveDto does. Passes before the
            // wiring fix too — it pins the coordinator half.
            OffMapNamedSkuProcurementService service =
                NewNotionsOnlyService(out _, out _);
            var dto = new PassXlSliceSaveDto { procurement = BuildNotionsProcurementDto() };

            List<string> restoreDiag = PassXlSliceSaveCoordinator.Restore(
                dto, null, null, service, null, null, null, null, null);
            string joined = string.Join("; ", restoreDiag);
            Assert.IsTrue(joined.Contains("restored off-map procurement"),
                "the procurement restore must run; diag: " + joined);

            PassXlSliceSaveDto recaptured = PassXlSliceSaveCoordinator.Capture(
                null, null, service);
            Assert.IsNotNull(recaptured.procurement,
                "a save after Continue must write a procurement section, not null");
            Assert.AreEqual("general-store-1", recaptured.procurement.BusinessId);
            Assert.AreEqual(1, recaptured.procurement.Orders.Count,
                "the re-saved section must carry the restored order");
            Assert.AreEqual("notions-order-1", recaptured.procurement.Orders[0].OrderId,
                "the order id must survive the restore-then-save round trip verbatim");
            Assert.AreEqual(15, recaptured.procurement.Orders[0].ExpectedArrivalDayIndex,
                "the arrival day must survive the round trip verbatim");
            Assert.AreEqual(1, recaptured.procurement.Lots.Count,
                "the re-saved section must carry the restored lot");
            Assert.AreEqual("notions-lot-1", recaptured.procurement.Lots[0].LotId,
                "the lot id must survive the restore-then-save round trip verbatim");
            Assert.AreEqual(1, recaptured.procurement.DeliveredSaleIds.Count,
                "the re-saved section must carry the delivered mark");
            Assert.AreEqual("sale-notions-1", recaptured.procurement.DeliveredSaleIds[0],
                "the delivered sale id must survive the restore-then-save round trip verbatim");
        }

        [Test]
        public void ContinueWiring_Procurement_FreshNotionsOnlyService_RestoresSavedOrdersAsData()
        {
            // In-window precondition pin (re-plan §4.3 test 3, §1.6):
            // the fresh market carries only the notions counterparty,
            // yet a saved horse-feed order (a different supplier /
            // counterparty, from the dev feed registration's shared
            // market) restores as data with no refusal — ImportState is
            // a merge with no market-counterparty validation. This is
            // the property the in-window registration relies on: the
            // fresh notions-only service can hold a saved section that
            // was written against the dev shared service. Passes before
            // the wiring fix too.
            //
            // Horse-feed figures are production's, from
            // ConfigurePlaytestHorseFeed: supplier "millbrook_grain_co",
            // item "store_horse_feed_sack", 15c per sack, 1 transit day
            // — nothing invented.
            OffMapNamedSkuProcurementService service =
                NewNotionsOnlyService(out ExternalMarketAuthority market, out ImportMaterial notionsMaterial);
            ExternalCounterparty notionsCounterparty =
                market.GetCounterparty(BakeryNotionsDailySupply.NotionsSupplierId);
            Assert.IsNotNull(notionsCounterparty, "the fresh market must carry the notions counterparty");
            Assert.AreEqual(notionsMaterial.TransitDays, notionsCounterparty.TransitDays,
                "the counterparty transit must be the catalog's transit, never hardcoded");
            Assert.AreEqual(notionsMaterial.PricePerUnitCents, 10,
                "the catalog's notions price is 10c/unit (read, not hardcoded)");
            Assert.AreEqual(BakeryNotionsDailySupply.FiniteExternalOrderUnits,
                notionsCounterparty.SupplyUnits[BakeryFlourSupply.NotionsMaterialId],
                "the counterparty supply must be the authored finite external order size");
            Assert.IsNull(market.GetCounterparty("millbrook_grain_co"),
                "the fresh market must NOT carry the horse-feed counterparty — it is notions-only");

            NamedSkuProcurementSaveDto procurementDto = BuildNotionsProcurementDto();
            procurementDto.Orders.Add(new NamedSkuOrder
            {
                OrderId = "horsefeed-order-1",
                BusinessId = "general-store-1",
                SkuId = "store_horse_feed_sack",
                UnitsOrdered = 1,
                UnitGoodsCostCents = 15,
                FreightSurchargeCents = 0,
                TotalCostCents = 15,
                SupplierId = "millbrook_grain_co",
                SupplierName = "Millbrook Grain Co.",
                OrderDayIndex = 2,
                ExpectedArrivalDayIndex = 3,
                ReceiptId = "receipt-horsefeed-1",
                Stage = NamedSkuOrderStage.Ordered,
                PaymentMode = NamedSkuPaymentMode.CashOnArrival,
            });
            var dto = new PassXlSliceSaveDto { procurement = procurementDto };

            List<string> restoreDiag = PassXlSliceSaveCoordinator.Restore(
                dto, null, null, service, null, null, null, null, null);
            string joined = string.Join("; ", restoreDiag);
            Assert.IsTrue(joined.Contains(
                    "restored off-map procurement for business 'general-store-1' (2 orders, 1 lots, 0 sales, 1 delivered)"),
                "the restore must report both orders; diag: " + joined);
            Assert.IsTrue(!joined.Contains("refus"),
                "no refusal may fire for the saved horse-feed order; diag: " + joined);
            Assert.IsTrue(!joined.Contains("does not match"),
                "no mismatch diagnostic may fire for the saved horse-feed order; diag: " + joined);

            Assert.AreEqual(2, service.Orders.Count, "both saved orders must restore as data");
            NamedSkuOrder horseFeedOrder = service.Orders.First(o => o.OrderId == "horsefeed-order-1");
            Assert.AreEqual("store_horse_feed_sack", horseFeedOrder.SkuId);
            Assert.AreEqual("millbrook_grain_co", horseFeedOrder.SupplierId,
                "the horse-feed supplier must restore verbatim even though the fresh market has no such counterparty");
            Assert.AreEqual(2, horseFeedOrder.OrderDayIndex);
            Assert.AreEqual(3, horseFeedOrder.ExpectedArrivalDayIndex);
            Assert.IsTrue(service.Orders.Any(o => o.OrderId == "notions-order-1"),
                "the notions order must restore alongside the horse-feed order");
            Assert.AreEqual(1, service.Lots.Count, "the saved notions lot must restore");
            Assert.AreEqual(1, service.DeliveredSaleIds.Count, "the delivered mark must restore");
        }

        // Priority 5 Slice 4 — HouseholdInventoryRegistry.ImportState:
        // clear-then-load + deep copy (original plan §4 Slice 4). No
        // dedicated inventory save-test file exists, so the four
        // plan-named tests live here, as the plan permits. All four
        // are reproducing tests: they FAIL before the fix (ImportState
        // never clears and skips any household id it already holds,
        // and wraps the caller's state object by reference) and PASS
        // after it. The repair copies the ledger registry precedent
        // (HouseholdLedger.cs ImportState: clear first, deep-copy each
        // state, null means "no section", not "empty").
        private static HouseholdItemLot NewBreadLot(int lotId, int units, params int[] ancestry)
        {
            return new HouseholdItemLot
            {
                LotId = lotId,
                ItemId = HouseholdItemCatalog.BreadId,
                QuantityUnits = units,
                UnitLabel = "loaf",
                AcquiredDayIndex = 0,
                SourceLabel = "general-store purchase",
                HeldUnits = 0,
                QualityBand = string.Empty,
                SourceKind = "purchase",
                OwnerPersonId = -1,
                AncestryLotIds = new List<int>(ancestry),
            };
        }

        private static HouseholdInventoryState NewInventoryState(
            int householdId, int nextLotSequence, params HouseholdItemLot[] lots)
        {
            return new HouseholdInventoryState
            {
                HouseholdId = householdId,
                NextLotSequence = nextLotSequence,
                SeededFromReserves = false,
                Lots = new List<HouseholdItemLot>(lots),
            };
        }

        [Test]
        public void HouseholdInventoryImport_SecondLoad_ReplacesFirstLoadLots()
        {
            var registry = new HouseholdInventoryRegistry();
            registry.ImportState(new[]
            {
                NewInventoryState(1, 2, NewBreadLot(1, 14)),
            });
            Assert.AreEqual(14, registry.Get(1).GetTotalUnits(HouseholdItemCatalog.BreadId),
                "setup: first load holds lot 1, 14 bread.");

            registry.ImportState(new[]
            {
                NewInventoryState(1, 3, NewBreadLot(2, 6)),
            });

            HouseholdInventory inventory = registry.Get(1);
            Assert.IsNotNull(inventory, "H1 must still exist after the second load.");
            Assert.AreEqual(1, inventory.Lots.Count, "the second load must replace the first load's lots.");
            Assert.AreEqual(2, inventory.Lots[0].LotId, "only lot 2 may survive the second load.");
            Assert.AreEqual(6, inventory.Lots[0].QuantityUnits, "lot 2 must hold its saved 6 units.");
            Assert.AreEqual(6, inventory.GetTotalUnits(HouseholdItemCatalog.BreadId));
            Assert.AreEqual(3, inventory.State.NextLotSequence,
                "NextLotSequence must come from the second load, not the first.");
            Assert.IsTrue(!inventory.Lots.Any(l => l.LotId == 1), "lot 1 must be gone after the second load.");
        }

        [Test]
        public void HouseholdInventoryImport_SmallerSecondLoad_DropsAbsentHousehold()
        {
            // Mirrors Pass12DoubleRestoreTests.LedgerImport_SmallerLoad_DropsStaleLedgers.
            var registry = new HouseholdInventoryRegistry();
            registry.ImportState(new[]
            {
                NewInventoryState(1, 2, NewBreadLot(1, 14)),
                NewInventoryState(2, 2, NewBreadLot(1, 10)),
            });
            Assert.IsNotNull(registry.Get(1), "setup: household 1 loaded.");
            Assert.IsNotNull(registry.Get(2), "setup: household 2 loaded.");

            registry.ImportState(new[]
            {
                NewInventoryState(1, 3, NewBreadLot(2, 6)),
            });

            Assert.IsNotNull(registry.Get(1), "The kept household survives the smaller load.");
            Assert.AreEqual(6, registry.Get(1).GetTotalUnits(HouseholdItemCatalog.BreadId));
            Assert.IsNull(registry.Get(2),
                "A smaller save must not leave the dropped household's inventory behind.");
        }

        [Test]
        public void HouseholdInventoryImport_SameDtoRestoredTwice_LoadsPristineLots()
        {
            var registry = new HouseholdInventoryRegistry();
            var dto = NewInventoryState(1, 2, NewBreadLot(1, 14, 7, 8));
            dto.SeededFromReserves = true;
            registry.ImportState(new[] { dto });
            Assert.AreEqual(14, registry.Get(1).GetTotalUnits(HouseholdItemCatalog.BreadId),
                "setup: first restore loads 14 bread.");

            // Run the live lots: consume 2 units and dirty the ancestry
            // on the live copy. Copy-on-import means the DTO object must
            // not have been mutated by the run.
            string consumeError = registry.Get(1).Consume(
                HouseholdItemCatalog.BreadId, 2, 0, "meal", "test", null);
            Assert.IsNull(consumeError, "setup: consuming 2 of 14 bread must succeed.");
            registry.Get(1).Lots[0].AncestryLotIds.Add(99);
            Assert.AreEqual(12, registry.Get(1).GetTotalUnits(HouseholdItemCatalog.BreadId),
                "setup: live lots now hold 12 bread after the meal.");
            Assert.AreEqual(14, dto.Lots[0].QuantityUnits,
                "the DTO object must not have been mutated by running the live lots (copy-on-import).");
            Assert.AreEqual(2, dto.Lots[0].AncestryLotIds.Count,
                "the DTO lot's ancestry must not have gained the live 99 entry.");

            registry.ImportState(new[] { dto });

            HouseholdInventory inventory = registry.Get(1);
            Assert.AreEqual(1, inventory.Lots.Count);
            Assert.AreEqual(1, inventory.Lots[0].LotId);
            Assert.AreEqual(14, inventory.Lots[0].QuantityUnits,
                "restoring the same DTO object again must load its pristine 14 units, not the already-run 12.");
            Assert.AreEqual(2, inventory.Lots[0].AncestryLotIds.Count,
                "the restored lot's ancestry must be the DTO's [7, 8], not the dirtied live list.");
            Assert.IsTrue(inventory.Lots[0].AncestryLotIds.Contains(7));
            Assert.IsTrue(inventory.Lots[0].AncestryLotIds.Contains(8));
            Assert.IsTrue(!inventory.Lots[0].AncestryLotIds.Contains(99));
            Assert.AreEqual(2, inventory.State.NextLotSequence);
            Assert.IsTrue(inventory.State.SeededFromReserves);
        }

        [Test]
        public void HouseholdInventoryImport_NullStates_IsNoOp()
        {
            // Complementary pin: a null import leaves live state
            // untouched (guards the repair against over-clearing; the
            // ledger registry keeps the same null-means-no-section rule).
            var registry = new HouseholdInventoryRegistry();
            registry.ImportState(new[]
            {
                NewInventoryState(1, 2, NewBreadLot(1, 14)),
            });
            Assert.AreEqual(14, registry.Get(1).GetTotalUnits(HouseholdItemCatalog.BreadId),
                "setup: household 1 loaded.");

            registry.ImportState(null);

            Assert.IsNotNull(registry.Get(1), "a null import must not clear live inventories.");
            Assert.AreEqual(14, registry.Get(1).GetTotalUnits(HouseholdItemCatalog.BreadId));
            Assert.AreEqual(1, registry.Get(1).Lots.Count);
            Assert.AreEqual(1, registry.Get(1).Lots[0].LotId);
        }

        // ---- Slice 7: mid-work round-trip acceptance ----------------
        //
        // Original plan §4 Slice 7: no test drives a save/load round
        // trip across ALL thirteen directive items at once, mid economic
        // work, through the real coordinator + authority seams, with
        // exact conservation and a repeated-load no-duplicate proof.
        // These three tests compose the already-sound seams plus
        // Slices 4-6. They are expected to PASS: a failure rewrites
        // plan §2 for the item that fails, and the slice stops and
        // reports instead of repairing inside the acceptance.
        //
        // BOUNDARY, stated explicitly (plan Slice 7): no
        // SaveLoadManager, no hub — the test composes the same calls
        // LoadDefaultSlot makes, in the same order, against fresh
        // instances. The manager wiring itself (that LoadDefaultSlot
        // calls these seams in this order with these arguments) is
        // source-reviewed only; Codex check 18 / §5 items 1-5 owe the
        // same scenario live, fresh-scene Continue. Roslyn harness
        // only; no Unity compilation, Test Runner, PlayMode, or scene
        // wiring is claimed by these tests.
        //
        // DEVIATIONS FROM THE PLAN'S FIXTURE SHAPE, verified in source
        // before writing the tests (recorded here, not silently
        // weakened):
        // (a) The plan's item 12 names one open "fr.load-freight" task
        //     AND item 7 a dispatched mid-journey shipment. A dispatched
        //     shipment cannot hold an open freight:load task (Slice 6:
        //     WorkLoading completes the load task and drops the binding
        //     before Dispatch can run), so the composed scenario keeps
        //     the two apart: the mid-journey shipment's own load task
        //     is Complete (as production leaves it), and the open
        //     fr.load-freight task is a separate pending task — it is
        //     also the agreement's registered dispatch task — whose
        //     equipment hold is the loading dock, not the wagon: the
        //     wagon is already held by the dispatched shipment under
        //     the shipment id, and the equipment authority refuses a
        //     second exclusive hold on the same asset (capacity 1).
        //     The cross-check the plan pins is unchanged: the task's
        //     handle resolves to a live reservation, orphan count zero.
        // (b) The plan's item 11 names an operating-ledger record.
        //     BusinessOperatingLedger lives in BusinessCreation.cs,
        //     which is harness-excluded (its test file is excluded
        //     from the harness sources), so no harness test can
        //     construct it. The settlement leg is pinned here through
        //     the harness-compiled authorities — the obligation with
        //     its one payment, the household ledger entries, the Pass 9
        //     wholesale settlement record, and the carrier-settlement
        //     rebuild flag — and the operating-ledger record itself is
        //     owed to Codex §5 item 5 (live before/after readout).
        // (c) The plan's "two stores must agree" line for item 7
        //     names LogisticsRuntimeManager as the second shipment
        //     store. That manager is a MonoBehaviour in a
        //     harness-excluded file, so it cannot be constructed in
        //     Roslyn. The seam-level agreement is pinned instead:
        //     the Pass 9 freight DTO and the service's own capture
        //     carry the same shipment id with the same flags, and the
        //     live Unity agreement is owed to Codex §5 item 5.
        // (d) The plan's item 4 "bin 20 lb flour + 1 notion" is read
        //     as bin stock: the notions bin lot (lot 904, 1u) is a
        //     DIFFERENT lot from the batch's notions custody line
        //     (lot 902, 1u, as in the Slice 1 pin, which carries no
        //     bin lot for it). Bin notions 1u + custody notions 1u
        //     are two distinct lots, never the same unit counted
        //     twice; the test asserts both lots by id.
        //
        // TEST-AUTHORING CORRECTION (no production defect, no
        // stop-rule trigger): the first version created the shipment
        // through FreightShipmentService.CreateShipment directly, so
        // it never entered a carrier company's ActiveShipments and
        // FreightProductionDriver.CaptureSaveDto(AllShipments())
        // captured zero shipments. The production seam is
        // FreightProductionDriver.CreateShipment, which ensures the
        // company, binds the source lot, sets charge/payer/agreement,
        // and accepts the shipment into the company (TryAcceptShipment
        // stamps carrierBusinessInstanceId) — the fixture now uses it.

        private const int Q04DayIndex = 2;
        private const string Q04ReceiptId = "rcpt-q04-7";
        private const string Q04ShipmentId = "ship-q04-7";
        private const string Q04MillbrookLotId = "aidlot-millbrook_grain_co:flour-reorder-d57-n16";
        private const string Q04AgreementRequestId = "freight-req-q04-7";
        private const string Q04AgreementId = "freight-agreement:freight-req-q04-7";
        private const string Q04LoadingDockKey = "workstation:loading-dock-1";

        // One live mid-work world: every authority the composed
        // round trip touches, plus the test-side models of stock the
        // save DTOs do not themselves carry (the source-lot stock
        // the custody debit draws from, and the recipient stock a
        // delivery would land in — neither is persisted; the
        // persisted custody evidence is the shipment's cargoLotId +
        // sourceLotDebitApplied flags).
        private sealed class Q04Scenario
        {
            public HouseholdErrandPlanRegistry Errands = new HouseholdErrandPlanRegistry();
            public HouseholdInventoryRegistry Inventories = new HouseholdInventoryRegistry();
            public HouseholdLedgerRegistry Ledgers = new HouseholdLedgerRegistry();
            public TaskAuthority Tasks = new TaskAuthority();
            public PersonScheduleTracker Schedules = new PersonScheduleTracker();
            public EquipmentReservationService Equipment = new EquipmentReservationService();
            public DueEventScheduler DueEvents = new DueEventScheduler();
            public WorkTimeBudgetStore Budgets = new WorkTimeBudgetStore();
            public FinancialObligationAuthority Obligations = new FinancialObligationAuthority();
            public BusinessRuntimeState Store = new BusinessRuntimeState();
            public OffMapNamedSkuProcurementService Procurement;
            public Series2Pass9SaveState Pass9;
            public OffMapAidBusinessRuntime Millbrook;
            public FreightProductionDriver FreightDriver;
            public Dictionary<string, int> SourceLotStock = new Dictionary<string, int>(StringComparer.Ordinal);
            public Dictionary<string, int> RecipientStock = new Dictionary<string, int>(StringComparer.Ordinal);
            public HashSet<string> SourceLotDebitedShipments = new HashSet<string>(StringComparer.Ordinal);
            public string PlanId;
            public string BatchId;
            public string ObligationId;
            public EntityId TaskId;
            public string HandleId;
            public string DueEventId;
        }

        // Every DTO the composed capture produces, in one bundle so
        // the repeated-load test restores the SAME DTO object.
        private sealed class Q04Captured
        {
            public List<HouseholdLedgerState> Ledgers = new List<HouseholdLedgerState>();
            public List<HouseholdInventoryState> Inventories = new List<HouseholdInventoryState>();
            public TaskAuthoritySaveDto Tasks;
            public PersonScheduleTrackerSaveDto Schedules;
            public WorkTimeBudgetSaveDto Budgets;
            public HouseholdErrandPlanRegistrySaveDto Errands;
            public DueEventSchedulerSaveDto DueEvents;
            public EquipmentReservationAuthoritySaveDto Equipment;
            public FinancialObligationSaveDto Obligations;
            public BusinessRuntimeSaveDto Store;
            public PassXlSliceSaveDto PassXl;
        }

        private static Q04Scenario NewQ04Scenario()
        {
            var scenario = new Q04Scenario();
            scenario.Procurement = NewNotionsOnlyService(out _, out _);
            var bakery = new BakeryShopRuntime("bakery-1");
            var driver = new Series2BakeryProductionDriver(
                bakery, new Series2InStockBakeryFuelSource(bakery));
            scenario.FreightDriver = new FreightProductionDriver();
            scenario.Pass9 = new Series2Pass9SaveState
            {
                BakeryRuntime = bakery,
                BakeryDriver = driver,
                FreightDriver = scenario.FreightDriver,
                CarrierSettlement = new Series2CarrierSettlement(),
            };
            scenario.Millbrook = NewMillbrookSupplier();
            scenario.SourceLotStock[Q04MillbrookLotId] = 16;
            return scenario;
        }

        // Real FreightShipmentHooks over a scenario's own authorities,
        // exactly the shape Slice 6's production restore builds
        // (Tasks, Budgets, Reservations, DayIndex, NowGameSeconds),
        // plus the custody callbacks the load flow needs. The source
        // hooks and the restore hooks are built the same way over
        // their own scenario; only the dicts differ.
        private static FreightShipmentHooks BuildQ04FreightHooks(Q04Scenario scenario)
        {
            var hooks = new FreightShipmentHooks
            {
                Tasks = scenario.Tasks,
                Budgets = scenario.Budgets,
                DayIndex = Q04DayIndex,
                Diagnostics = new List<string>(),
                Reservations = scenario.Equipment,
                NowGameSeconds = 0f,
            };
            hooks.ReceiveShipment = (LogisticsShipmentState shipment, int quantityUnits, out string reason) =>
            {
                reason = string.Empty;
                if (quantityUnits <= 0) return 0;
                string key = string.IsNullOrWhiteSpace(shipment.destinationBusinessInstanceId)
                    ? "nowhere" : shipment.destinationBusinessInstanceId;
                scenario.RecipientStock[key] = scenario.RecipientStock.TryGetValue(key, out int have)
                    ? have + quantityUnits : quantityUnits;
                return quantityUnits;
            };
            hooks.CommitSourceLot = (LogisticsShipmentState shipment, int quantity, out string reason) =>
            {
                reason = string.Empty;
                if (scenario.SourceLotDebitedShipments.Contains(shipment.ShipmentId)) return true;
                if (!scenario.SourceLotStock.TryGetValue(shipment.CargoLotId, out int onHand) || onHand < quantity)
                {
                    reason = "source lot shortfall";
                    return false;
                }
                scenario.SourceLotStock[shipment.CargoLotId] = onHand - quantity;
                scenario.SourceLotDebitedShipments.Add(shipment.ShipmentId);
                return true;
            };
            return hooks;
        }

        private static EquipmentAsset NewQ04Wagon(string assetId)
        {
            return new EquipmentAsset
            {
                AssetId = assetId,
                Kind = "farm-wagon",
                DisplayName = "Farm wagon",
                Condition01 = 0.9f,
                PayloadMassCapacityKg = 1000f,
                CargoVolumeCapacityM3 = 4f,
            };
        }

        // Builds the source world through the real production seams:
        // Millbrook import, Bakery planning, procurement import, store
        // lot receipt, inventory/ledger/errand/schedule/budget/task/
        // equipment/due-event/obligation writes, then the agreement
        // request/quote/accept flow and the freight load/dispatch flow.
        // Every step asserts its own success, so a build failure names
        // the seam that refused instead of surfacing as a restore
        // failure later.
        private static Q04Scenario BuildQ04SourceScenario()
        {
            var s = NewQ04Scenario();
            var diag = new List<string>();

            // (6)/(9) Millbrook: the saved clamped-lot section (160 lb,
            // 768c, one 16-sack lot uncommitted/unreceived) imported into
            // the live runtime — the Slice 2 fixture, reused verbatim.
            Assert.IsNull(s.Millbrook.ImportState(BuildClampedLotMillbrookDto()),
                "the Millbrook section must import into the live runtime");
            Assert.AreEqual(160, s.Millbrook.GetInventoryUnits("bakery_flour_sack"));
            Assert.AreEqual(768, s.Millbrook.CashCents);
            Assert.AreEqual(1, s.Millbrook.Lots.Count);

            // (4) Bakery: bin 20 lb flour + 1 notion (lot 904), fuel
            // 30u, one Prepped batch holding 12 lb flour + 1 notion in
            // custody (notion custody lot 902, distinct from the bin
            // lot — deviation (d) above), one settled wholesale record.
            BakeryShopRuntime bakery = s.Pass9.BakeryRuntime;
            Assert.IsNull(bakery.FlourStock.ReceiveLot(new BakeryFlourLot
            {
                LotId = EntityId.For(EntityKind.Lot, 901),
                FlourName = BakeryBreadCatalog.FlourItemId,
                Units = 20,
                AcquiredDayIndex = 0,
                ImportOrderId = "test-flour-order",
                OriginName = "Test mill",
            }, diag), "the 20 lb flour lot must be received; diag: " + string.Join("; ", diag));
            Assert.IsNull(bakery.FlourStock.ReceiveLot(new BakeryFlourLot
            {
                LotId = EntityId.For(EntityKind.Lot, 904),
                FlourName = BakeryBreadCatalog.NotionsItemId,
                Units = 1,
                AcquiredDayIndex = 0,
                ImportOrderId = "notions-order-1",
                OriginName = "Test notions",
            }, diag), "the 1u notions bin lot must be received; diag: " + string.Join("; ", diag));
            Assert.IsNull(bakery.FuelStock.ReceiveLot(new BakeryFuelLot
            {
                LotId = EntityId.For(EntityKind.Lot, 903),
                FuelName = BakeryBreadCatalog.FuelItemId,
                Units = 30,
                AcquiredDayIndex = 0,
                IsBootstrapEndowment = true,
            }, diag), "the 30u fuel lot must be received; diag: " + string.Join("; ", diag));
            List<string> plannedIds = s.Pass9.BakeryDriver.PlanBatches(
                BakeryBreadCatalog.BreadLoafId, 1, Q04DayIndex, diag);
            Assert.IsNotNull(plannedIds, "planning must succeed; diag: " + string.Join("; ", diag));
            Assert.AreEqual(1, plannedIds.Count);
            s.BatchId = plannedIds[0];
            BakeryDoughBatch batch = bakery.Batches.First(b => b.BatchId == s.BatchId);
            batch.Stage = BakeryBatchStage.Prepped;
            batch.FlourInCustody.Add(new BakeryFlourDispenseLine
            {
                LotId = EntityId.For(EntityKind.Lot, 901),
                FlourName = BakeryBreadCatalog.FlourItemId,
                UnitsTaken = 12,
                ProvenanceChain = "Test mill",
            });
            batch.NotionsUsed.Add(new BakeryFlourDispenseLine
            {
                LotId = EntityId.For(EntityKind.Lot, 902),
                FlourName = BakeryBreadCatalog.NotionsItemId,
                UnitsTaken = 1,
                ProvenanceChain = "Test notions",
            });
            s.Pass9.WholesaleSettlements.Add(new Pass3WholesaleSettlement
            {
                BakeryLotId = "lot-1",
                ProductId = "bread",
                Units = 10,
                UnitWholesalePriceCents = 50,
                PayerBusinessId = "bakery-1",
                PayeeBusinessId = "general-store-1",
                AmountCents = 500,
                Settled = true,
            });

            // (5) Procurement: one notions order in flight (day 1,
            // arrival day 15) + one lot + one delivered sale id.
            s.Procurement.ClearState();
            s.Procurement.ImportState(BuildNotionsProcurementDto());
            Assert.AreEqual(1, s.Procurement.Orders.Count, "setup: one notions order");
            Assert.AreEqual(1, s.Procurement.Lots.Count, "setup: one notions lot");

            // (2, store side) Store: one identified retail bread lot,
            // 24 loaves, with the opening-stock endowment recorded
            // once — the endowment must not re-apply on restore.
            s.Store.BindBusinessIdentity("general-store-1");
            Assert.IsNotNull(s.Store.EnsureCategoryStock("staple_food", 0, 1000),
                "the store must hold a staple_food category");
            Assert.IsNull(s.Store.TryAddRetailItemLot("store-lot-bread-1",
                HouseholdItemCatalog.BreadId, "staple_food", "loaf", 24,
                "general-store-1", "general-store-1", "bakery wholesale transfer", Q04DayIndex),
                "the store retail lot must land");
            Assert.IsTrue(s.Store.RecordAppliedScenarioEndowment("dev-opening-stock"),
                "the opening-stock endowment must record once");

            // (2)/(3) Household inventory: purchased bread lot (6u,
            // 2 held, SourceLabel carrying the receipt id) + a second
            // flour lot (10 lb).
            s.Inventories.ImportState(new[]
            {
                new HouseholdInventoryState
                {
                    HouseholdId = 1,
                    NextLotSequence = 3,
                    SeededFromReserves = false,
                    Lots = new List<HouseholdItemLot>
                    {
                        new HouseholdItemLot
                        {
                            LotId = 1,
                            ItemId = HouseholdItemCatalog.BreadId,
                            QuantityUnits = 6,
                            UnitLabel = "loaf",
                            AcquiredDayIndex = Q04DayIndex,
                            SourceLabel = "receipt " + Q04ReceiptId + "; general-store purchase",
                            HeldUnits = 2,
                            SourceKind = "purchase",
                            OwnerPersonId = 1,
                        },
                        new HouseholdItemLot
                        {
                            LotId = 2,
                            ItemId = HouseholdItemCatalog.FlourId,
                            QuantityUnits = 10,
                            UnitLabel = "lb",
                            AcquiredDayIndex = Q04DayIndex,
                            SourceLabel = "millbrook flour purchase",
                            HeldUnits = 0,
                            SourceKind = "purchase",
                            OwnerPersonId = 1,
                        },
                    },
                },
            });
            Assert.AreEqual(6, s.Inventories.Get(1).GetTotalUnits(HouseholdItemCatalog.BreadId),
                "setup: household bread lot holds 6");

            // (11, household leg) Ledger: wages in, bread out —
            // 10000c - 30c (6 loaves x 5c) = 9970c.
            HouseholdLedger ledger = s.Ledgers.GetOrCreate(1);
            Assert.IsNull(ledger.RecordInflow(Q04DayIndex, 10000,
                HouseholdIncomeSource.WageEmployment, "emp-q04-7", "wages day 2", "employer"),
                "the wage inflow must record");
            Assert.IsNull(ledger.RecordOutflow(Q04DayIndex, 30, "bread purchase 6 loaves", "general-store-1"),
                "the bread outflow must record");
            Assert.AreEqual(9970, ledger.GetBalanceCents(), "setup: ledger balance 9970c");

            // (1) Errand plan Proposed, bread need 7 claimed, shopper
            // window 720 + 95 min.
            HouseholdErrandPlan plan = s.Errands.Create(1, Q04DayIndex);
            s.PlanId = plan.PlanId;
            Assert.AreEqual("errand-1-2-0", s.PlanId, "the plan id is deterministic from household/day/sequence");
            Assert.IsTrue(s.Errands.Claim(s.PlanId, 1, 7), "the bread need must claim");
            s.Errands.NoteEstimate(s.PlanId, 95, 720);

            // (13, person leg) Schedule: Work [480,720] + Shopping
            // [720,815] for P1 on day 2 — person time exclusive, no
            // overlap.
            Assert.IsNull(s.Schedules.TryReserve(1, PersonActivityKind.Work,
                Q04DayIndex, 480, 240, "day job"), "the Work reservation must hold");
            Assert.IsNull(s.Schedules.TryReserve(1, PersonActivityKind.Shopping,
                Q04DayIndex, 720, 95, "bread errand"), "the Shopping reservation must hold");
            Assert.AreEqual(2, s.Schedules.ActiveFor(1, Q04DayIndex).Count, "setup: two live reservations for P1");

            // (12)/(13) Pending task: fr.load-freight, 15 planned
            // minutes, 0 worked, assigned to P701, holding the
            // loading dock through the equipment authority.
            s.Budgets.EnsureDay(Q04DayIndex);
            Assert.IsTrue(s.Tasks.RegisterDefinition(
                new TaskDefinition("fr.load-freight", "Load freight", 15), out string defReason),
                "the load-freight definition must register: " + defReason);
            WorkTask task = s.Tasks.CreateTaskWithPlannedMinutes("fr.load-freight",
                EntityId.For(EntityKind.Business, 3), Q04DayIndex, 15);
            Assert.IsNotNull(task, "the pending task must be created");
            s.TaskId = task.TaskId;
            Assert.IsTrue(s.Tasks.AssignTask(s.TaskId, EntityId.For(EntityKind.Person, 701),
                s.Budgets, out string assignReason), "the task must assign: " + assignReason);
            Assert.AreEqual(15, task.PlannedMinutes, "setup: 15 planned minutes");
            Assert.AreEqual(0, task.MinutesWorked, "setup: 0 minutes worked");
            var holdRequest = new ReservationRequest(s.TaskId.ToString(), "load freight " + Q04ShipmentId);
            holdRequest.Items.Add(new ReservationItem(Q04LoadingDockKey, 1f, 0f, 3600f));
            Assert.IsNull(s.Equipment.TryReserveAll(holdRequest, out ReservationHandle handle),
                "the loading-dock hold must grant");
            Assert.IsNotNull(handle, "the hold must return a handle");
            s.HandleId = handle.ReservationId;
            Assert.IsTrue(s.Tasks.MarkResourcesReserved(s.TaskId, s.HandleId,
                "loading dock held", out string markReason),
                "the task must record its handle: " + markReason);
            Assert.AreEqual(s.HandleId, task.ReservationHandleId, "setup: task holds its handle id");
            Assert.AreEqual(TaskStatus.ResourcesReserved, task.Status, "setup: task is ResourcesReserved (open)");

            // (13, due-event leg) Due event for the shipment.
            Assert.IsNull(s.DueEvents.Schedule(DueEventKind.TaskCompletionDue,
                3600f, Q04ShipmentId, out string dueEventId), "the due event must schedule");
            s.DueEventId = dueEventId;
            Assert.AreEqual("due-TaskCompletionDue-" + Q04ShipmentId, s.DueEventId,
                "the due event id is deterministic from kind + payload");

            // (11, obligation leg) Obligation open: 125c freight
            // charge, one 25c payment recorded, 100c outstanding.
            var ids = new EntityIdRegistry();
            FinancialObligation obligation = s.Obligations.Create(ids,
                FinancialObligationKind.Payable, "bakery-1", "millbrook_grain_co",
                125, Q04DayIndex, string.Empty, "freight charge " + Q04ShipmentId, Q04AgreementId);
            Assert.IsNotNull(obligation, "the obligation must be created");
            s.ObligationId = obligation.ObligationId;
            FinancialPaymentRecord payment = s.Obligations.ApplyPayment(
                s.ObligationId, 25, Q04DayIndex, "bakery-1", "millbrook_grain_co", "pay-q04-7");
            Assert.IsNotNull(payment, "the 25c payment must apply");
            Assert.AreEqual(100, obligation.OutstandingPrincipalCents, "setup: 100c outstanding after one payment");
            Assert.AreEqual(1, obligation.Payments.Count, "setup: exactly one payment recorded");

            // (7)/(8)/(10) Freight: agreement request -> quote (125c)
            // -> accept -> link shipment -> prepare -> register the
            // pending task as the dispatch task; then the real load /
            // dispatch flow drives the shipment mid-journey.
            FreightProductionDriver driver = s.FreightDriver;
            FreightShipmentService service = driver.Service;
            service.RegisterWagon(NewQ04Wagon("wagon-1"));
            service.RegisterWagon(NewQ04Wagon("wagon-2"));
            FreightShipmentHooks hooks = BuildQ04FreightHooks(s);
            var book = driver.ServiceAgreements;
            var freightRequest = new FreightServiceRequest
            {
                RequestId = Q04AgreementRequestId,
                RequestingBusinessId = "bakery-1",
                RequestingPersonId = 1,
                CarrierBusinessId = "carrier-1",
                CarrierOperatorPersonId = 702,
                PickupBusinessId = "millbrook_grain_co",
                PickupLocationId = "millbrook",
                DestinationBusinessId = "bakery-1",
                DestinationLocationId = "bakery-1",
                CargoLotId = Q04MillbrookLotId,
                CargoSkuId = "bakery_flour_sack",
                Quantity = 16,
                QuantityUnit = "sack",
            };
            Assert.IsNull(book.CreateRequest(freightRequest, out _), "the freight request must be created");
            var quote = new FreightServiceQuote
            {
                QuoteId = "quote-q04-7",
                RateCardBusinessId = "carrier-1",
                OneWayDistanceMiles = 50f,
                ChargeableMiles = 50,
                BaseServiceFeeCents = 125,
                RatePerMileCents = 0,
                MileageChargeCents = 0,
                TotalPriceCents = 125,
                PayerBusinessId = "bakery-1",
                PaymentTiming = "After successful delivery",
            };
            Assert.IsNull(book.SetQuote(Q04AgreementRequestId, quote), "the 125c quote must set");
            Assert.IsNull(book.Accept(Q04AgreementRequestId, 702, Q04DayIndex, 0f,
                out FreightServiceAgreement agreement), "the agreement must be accepted");
            Assert.IsNotNull(agreement, "acceptance must return the agreement");
            Assert.AreEqual(Q04AgreementId, agreement.AgreementId, "the agreement id is deterministic from the request");

            LogisticsShipmentState shipment = driver.CreateShipment("carrier-1",
                Q04ShipmentId, "millbrook_grain_co", "bakery-1", 16,
                new FreightCargoSpec { MassKgPerUnit = 2f, VolumeM3PerUnit = 0.01f },
                "wagon-1", hooks, EntityId.For(EntityKind.Person, 702), null,
                125, "bakery-1", diag, out string createReason,
                Q04AgreementId, Q04MillbrookLotId, "bakery_flour_sack");
            Assert.IsNotNull(shipment, "the shipment must be created: " + createReason);
            Assert.AreEqual("carrier-1", shipment.CarrierBusinessInstanceId,
                "the driver seam accepts the shipment into the carrier company");
            Assert.AreEqual(125, shipment.freightChargeCents, "the driver seam sets the 125c charge");
            Assert.AreEqual("bakery-1", shipment.FreightPayerBusinessInstanceId,
                "the driver seam sets the payer");
            Assert.AreEqual(Q04AgreementId, shipment.FreightServiceAgreementId,
                "the driver seam sets the agreement link");
            Assert.AreEqual(Q04MillbrookLotId, shipment.CargoLotId,
                "the driver seam binds the Millbrook source lot");
            Assert.IsTrue(shipment.requiresSourceLotCustody, "the driver seam marks lot custody");
            Assert.IsNull(book.LinkShipment(Q04AgreementId, Q04ShipmentId),
                "the agreement must link its shipment");
            Assert.IsNull(book.MarkPreparedForDispatch(Q04AgreementId, Q04ShipmentId, 3600f),
                "the shipment must be marked prepared");
            Assert.IsNull(book.RegisterDispatchTask(Q04AgreementId, Q04ShipmentId, s.TaskId.ToString()),
                "the pending task must register as the dispatch task");
            Assert.IsTrue(service.BeginLoading(shipment, EntityId.For(EntityKind.Person, 701),
                hooks, out string loadReason), "loading must begin: " + loadReason);
            int guard = 0;
            while (!shipment.loadApplied && guard++ < 10)
                Assert.IsTrue(service.WorkLoading(shipment, 60, hooks, out string workReason),
                    "loading must work: " + workReason);
            Assert.IsTrue(shipment.loadApplied, "setup: shipment loaded");
            Assert.IsTrue(shipment.sourceLotDebitApplied, "setup: source lot debit applied");
            Assert.AreEqual(0, s.SourceLotStock[Q04MillbrookLotId],
                "setup: the 16-sack source lot is debited exactly once to 0");
            Assert.IsTrue(service.Dispatch(shipment, EntityId.For(EntityKind.Person, 702),
                null, hooks, out string dispatchReason), "dispatch must succeed: " + dispatchReason);
            Assert.IsTrue(shipment.physicalTransportBound, "setup: transport bound");
            shipment.transitDurationGameSeconds = 200f;
            service.AdvanceTransit(shipment, 80f, hooks);
            Assert.AreEqual(LogisticsShipmentStatus.InTransit, shipment.Status, "setup: shipment mid-journey");
            Assert.Greater(shipment.physicalProgress01, 0f, "setup: transit progressing");
            Assert.IsTrue(shipment.physicalProgress01 < 1f, "setup: transit not complete");
            return s;
        }

        // Capture through the real seams, in BuildSaveDto order: hub
        // authorities first, then the coordinator (procurement, Pass 9,
        // Millbrook) — the same capture LoadDefaultSlot's counterpart
        // performs.
        private static Q04Captured CaptureQ04Scenario(Q04Scenario s)
        {
            var captured = new Q04Captured();
            s.Ledgers.ExportState(captured.Ledgers);
            s.Inventories.ExportState(captured.Inventories);
            captured.Tasks = s.Tasks.CaptureSaveDto();
            captured.Schedules = s.Schedules.CaptureSaveDto();
            captured.Budgets = s.Budgets.CaptureSaveDto();
            captured.Errands = s.Errands.CaptureSaveDto();
            captured.DueEvents = s.DueEvents.CaptureSaveDto();
            captured.Equipment = s.Equipment.CaptureSaveDto();
            captured.Obligations = s.Obligations.CaptureSaveDto();
            captured.Store = s.Store.CaptureSaveDto();
            captured.PassXl = PassXlSliceSaveCoordinator.Capture(
                null, null, s.Procurement, s.Pass9, null, null, s.Millbrook);
            Assert.IsNotNull(captured.PassXl.series2Pass9, "capture: Pass 9 section present");
            Assert.IsNotNull(captured.PassXl.millbrookAid, "capture: Millbrook section present");
            Assert.IsNotNull(captured.PassXl.procurement, "capture: procurement section present");
            return captured;
        }

        // Restore into fresh instances, in LoadDefaultSlot order:
        // population ledgers/inventories, then the hub authorities
        // (tasks, schedules, budgets, errands, due events, equipment,
        // obligations), then the store runtime, then the Pass XL
        // restore with REAL freight hooks built from the restored hub
        // authorities (Slice 6's production shape).
        private static void RestoreQ04Scenario(Q04Captured captured, Q04Scenario target)
        {
            target.Ledgers.ImportState(captured.Ledgers);
            target.Inventories.ImportState(captured.Inventories);
            target.Tasks.LoadFromSaveDto(captured.Tasks);
            target.Schedules.LoadFromSaveDto(captured.Schedules);
            target.Budgets.LoadFromSaveDto(captured.Budgets, Q04DayIndex);
            target.Errands.LoadFromSaveDto(captured.Errands);
            target.DueEvents.LoadFromSaveDto(captured.DueEvents);
            target.Equipment.LoadFromSaveDto(captured.Equipment);
            target.Obligations.LoadFromSaveDto(captured.Obligations);
            target.Store = BusinessRuntimeState.FromSaveDto(captured.Store, BusinessType.GeneralStore);
            FreightShipmentHooks restoreHooks = BuildQ04FreightHooks(target);
            List<string> restoreDiag = PassXlSliceSaveCoordinator.Restore(
                captured.PassXl, null, null, target.Procurement, target.Pass9,
                restoreHooks, null, null, target.Millbrook);
            string joined = string.Join("; ", restoreDiag);
            Assert.IsTrue(restoreDiag.Any(d => d.Contains("Series2Pass9Save: restore complete")),
                "the Pass 9 restore must run to completion; diag: " + joined);
            Assert.IsTrue(joined.Contains("restored Millbrook aid table (160 lb flour, 768c retained cash, 1 cargo lots)"),
                "the Millbrook restore must report the saved values; diag: " + joined);
            Assert.IsTrue(joined.Contains(
                    "restored off-map procurement for business 'general-store-1' (1 orders, 1 lots, 0 sales, 1 delivered)"),
                "the procurement restore must report the saved counts; diag: " + joined);
        }

        // Exact-conservation assertions for one restored scenario,
        // item by item against the source scenario's expected values.
        // Called after every restore (first, second, third) so a
        // repeated load must reproduce the same state, not just the
        // first load.
        private static void AssertQ04ScenarioRestored(Q04Scenario source, Q04Scenario target)
        {
            // (1) Household errand plan: same id, Proposed, same
            // household, same claimed need, same estimate/departure.
            Assert.AreEqual(1, target.Errands.All.Count, "exactly one errand plan must restore");
            HouseholdErrandPlan plan = target.Errands.Get(source.PlanId);
            Assert.IsNotNull(plan, "the errand plan must restore with its id");
            Assert.AreEqual(HouseholdErrandPlanStatus.Proposed, plan.Status, "the plan must stay Proposed");
            Assert.AreEqual(1, plan.HouseholdId, "the plan household must restore");
            List<int> claimed = target.Errands.ClaimedNeedSequences(source.PlanId);
            Assert.AreEqual(1, claimed.Count, "exactly one need must stay claimed");
            Assert.AreEqual(7, claimed[0], "the claimed bread need sequence must restore");
            Assert.AreEqual(95, target.Errands.GetEstimateMinutes(source.PlanId), "the estimate must restore");
            Assert.AreEqual(720, target.Errands.GetDepartureMinute(source.PlanId), "the departure minute must restore");

            // (2) Purchased lot: bread 6u, 2 held, receipt id in the
            // SourceLabel; (3) second lot: flour 10 lb. No lot id
            // appears twice.
            HouseholdInventory inventory = target.Inventories.Get(1);
            Assert.IsNotNull(inventory, "household 1 inventory must restore");
            Assert.AreEqual(2, inventory.Lots.Count, "exactly two lots must restore, never duplicated");
            HouseholdItemLot breadLot = inventory.Lots.First(l => l.LotId == 1);
            Assert.AreEqual(HouseholdItemCatalog.BreadId, breadLot.ItemId);
            Assert.AreEqual(6, breadLot.QuantityUnits, "the bread lot must hold its 6 units");
            Assert.AreEqual(2, breadLot.HeldUnits, "the bread lot must keep its 2 held units");
            Assert.AreEqual(4, breadLot.AvailableUnits, "6 - 2 held = 4 available");
            Assert.IsTrue(breadLot.SourceLabel.Contains(Q04ReceiptId),
                "the receipt id must survive in the lot's SourceLabel: " + breadLot.SourceLabel);
            HouseholdItemLot flourLot = inventory.Lots.First(l => l.LotId == 2);
            Assert.AreEqual(HouseholdItemCatalog.FlourId, flourLot.ItemId);
            Assert.AreEqual(10, flourLot.QuantityUnits, "the flour lot must hold its 10 lb");
            Assert.AreEqual(6, inventory.GetTotalUnits(HouseholdItemCatalog.BreadId));
            Assert.AreEqual(10, inventory.GetTotalUnits(HouseholdItemCatalog.FlourId));

            // (11, household leg) Ledger: 10000c in, 30c out, 9970c,
            // exactly two entries, sequence cursor intact.
            HouseholdLedger ledger = target.Ledgers.Get(1);
            Assert.IsNotNull(ledger, "household 1 ledger must restore");
            Assert.AreEqual(9970, ledger.GetBalanceCents(), "ledger balance must restore at 9970c");
            Assert.AreEqual(2, ledger.Entries.Count, "exactly two ledger entries must restore");
            Assert.AreEqual(10000, ledger.Entries[0].AmountCents, "the wage inflow must restore");
            Assert.AreEqual(-30, ledger.Entries[1].AmountCents, "the bread outflow must restore");
            Assert.AreEqual(2, ledger.State.NextEntrySequence, "the entry sequence cursor must restore");

            // (4) Bakery: bin 20 lb flour + 1 notion (lot 904), fuel
            // 30u, same batch id Prepped with 12 lb flour + 1 notion
            // in custody, planned day 2.
            Assert.AreEqual(20, target.Pass9.BakeryRuntime.FlourStock.UnitsOnHand(BakeryBreadCatalog.FlourItemId),
                "the flour bin must restore at 20 lb");
            Assert.AreEqual(1, target.Pass9.BakeryRuntime.FlourStock.UnitsOnHand(BakeryBreadCatalog.NotionsItemId),
                "the notions bin must restore at 1u");
            Assert.AreEqual(30, target.Pass9.BakeryRuntime.FuelStock.UnitsOnHand(BakeryBreadCatalog.FuelItemId),
                "the fuel stock must restore at 30u");
            Assert.AreEqual(1, target.Pass9.BakeryRuntime.Batches.Count, "exactly one active batch must restore");
            BakeryDoughBatch restoredBatch = target.Pass9.BakeryRuntime.Batches[0];
            Assert.AreEqual(source.BatchId, restoredBatch.BatchId, "the batch id must survive verbatim");
            Assert.AreEqual(BakeryBatchStage.Prepped, restoredBatch.Stage, "the batch stage must restore");
            Assert.AreEqual(12, restoredBatch.FlourInCustody.Sum(l => l.UnitsTaken),
                "the batch must keep its 12 lb of flour in custody");
            Assert.AreEqual(1, restoredBatch.NotionsUsed.Sum(l => l.UnitsTaken),
                "the batch must keep its 1 notion in custody");
            Assert.AreEqual(Q04DayIndex, target.Pass9.BakeryDriver.PlannedDayIndex,
                "the planned day must restore");
            Assert.AreEqual(1, target.Pass9.BakeryDriver.PlannedBatchIds.Count,
                "exactly one planned batch id must restore");
            Assert.AreEqual(source.BatchId, target.Pass9.BakeryDriver.PlannedBatchIds[0],
                "the planned batch id must survive verbatim");

            // (5) Procurement: 1 order (day 1, arrival 15), 1 lot,
            // 1 delivered mark.
            Assert.AreEqual(1, target.Procurement.Orders.Count, "exactly one notions order must restore");
            NamedSkuOrder order = target.Procurement.Orders[0];
            Assert.AreEqual("notions-order-1", order.OrderId);
            Assert.AreEqual(1, order.OrderDayIndex, "the order day must restore");
            Assert.AreEqual(15, order.ExpectedArrivalDayIndex, "the arrival day must restore");
            Assert.AreEqual(1, target.Procurement.Lots.Count, "exactly one notions lot must restore");
            Assert.AreEqual("notions-lot-1", target.Procurement.Lots[0].LotId);
            Assert.AreEqual(1, target.Procurement.DeliveredSaleIds.Count, "exactly one delivered mark must restore");
            Assert.AreEqual("sale-notions-1", target.Procurement.DeliveredSaleIds[0]);
            Assert.IsTrue(target.Procurement.IsSaleDelivered("sale-notions-1"),
                "the delivered mark must answer as delivered");

            // (6)/(9) Millbrook: stock 160 lb, cash 768c, exactly the
            // one saved 16-sack lot, uncommitted, unreceived.
            Assert.AreEqual(160, target.Millbrook.GetInventoryUnits("bakery_flour_sack"),
                "Millbrook stock must restore at 160 lb");
            Assert.AreEqual(768, target.Millbrook.CashCents, "Millbrook cash must restore at 768c");
            Assert.AreEqual(1, target.Millbrook.Lots.Count, "exactly one Millbrook lot must restore");
            OffMapAidCargoLot millbrookLot = target.Millbrook.Lots[0];
            Assert.AreEqual(Q04MillbrookLotId, millbrookLot.LotId, "the Millbrook lot id must survive verbatim");
            Assert.AreEqual(16, millbrookLot.PackageUnits, "the lot holds 16 sacks");
            Assert.AreEqual(160, millbrookLot.InventoryUnits, "the lot holds 160 lb");
            Assert.AreEqual(768, millbrookLot.TotalCostCents, "the lot costs 16 x 48c");
            Assert.IsTrue(!millbrookLot.SourceCommitted, "the lot must stay uncommitted");
            Assert.IsTrue(!millbrookLot.Received, "the lot must stay unreceived");

            // (7) Freight shipment mid-journey: same id, InTransit,
            // loaded, undelivered, uncollected, 125c, same carrier /
            // payer / agreement, transport bound to P702 + wagon-1.
            List<LogisticsShipmentState> shipments = target.FreightDriver.AllShipments();
            Assert.AreEqual(1, shipments.Count, "exactly one shipment must restore, never duplicated");
            LogisticsShipmentState shipment = shipments[0];
            Assert.AreEqual(Q04ShipmentId, shipment.ShipmentId);
            Assert.AreEqual(LogisticsShipmentStatus.InTransit, shipment.Status, "the shipment must stay mid-journey");
            Assert.IsTrue(shipment.loadApplied, "the load must stay applied");
            Assert.IsTrue(!shipment.deliveryApplied, "the shipment must stay undelivered");
            Assert.IsTrue(!shipment.freightRevenueCollected, "no revenue may be collected by the restore");
            Assert.AreEqual(125, shipment.freightChargeCents, "the 125c freight charge must restore");
            Assert.AreEqual("carrier-1", shipment.CarrierBusinessInstanceId, "the carrier must restore");
            Assert.AreEqual("bakery-1", shipment.FreightPayerBusinessInstanceId, "the payer must restore");
            Assert.AreEqual(Q04AgreementId, shipment.FreightServiceAgreementId, "the agreement link must restore");
            Assert.IsTrue(shipment.physicalTransportBound, "the transport binding must restore");
            Assert.AreEqual(EntityId.For(EntityKind.Person, 702).ToString(), shipment.physicalDriverPersonId,
                "the driver binding must restore");
            Assert.AreEqual("wagon-1", shipment.physicalWagonAssetId, "the wagon binding must restore");
            Assert.Greater(shipment.physicalProgress01, 0f, "transit progress must restore above zero");
            Assert.IsTrue(shipment.physicalProgress01 < 1f, "transit must stay incomplete");

            // (8) Cargo custody: the shipment's cargo lot IS the
            // Millbrook lot, and the source-lot debit marker survives.
            // (The test-side source-stock dict is not persisted; the
            // persisted custody evidence is these two fields.)
            Assert.AreEqual(Q04MillbrookLotId, shipment.CargoLotId,
                "the shipment's cargo lot must be the Millbrook lot");
            Assert.AreEqual("bakery_flour_sack", shipment.cargoSkuId, "the cargo SKU must restore");
            Assert.IsTrue(shipment.requiresSourceLotCustody, "the shipment must stay lot-custody bound");
            Assert.IsTrue(shipment.sourceLotDebitApplied, "the source-lot debit marker must survive the restore");

            // (7, second store — deviation (c)) The Pass 9 freight
            // DTO and the service's own capture agree on the shipment:
            // same id, same flags. The LogisticsRuntimeManager half
            // is harness-excluded; Codex §5 item 5 owes it live.
            FreightShipmentService.FreightServiceSaveDto serviceDto =
                target.FreightDriver.CaptureSaveDto();
            Assert.AreEqual(1, serviceDto.Shipments.Count, "the service capture must hold exactly the one shipment");
            Assert.AreEqual(Q04ShipmentId, serviceDto.Shipments[0].shipmentId,
                "the service capture must agree with the Pass 9 section on the shipment id");
            Assert.IsTrue(serviceDto.Shipments[0].loadApplied, "the service capture must agree the load is applied");
            Assert.IsTrue(!serviceDto.Shipments[0].deliveryApplied, "the service capture must agree it is undelivered");
            Assert.IsTrue(serviceDto.ShipmentIdsWithDispatchAuthorities.Contains(Q04ShipmentId),
                "the dispatch authority must rebind on restore (Slice 6): the wagon hold is known again");

            // (10) Freight agreement: accepted, same id, same
            // shipment, same 125c price, same dispatch task.
            Assert.AreEqual(1, target.FreightDriver.ServiceAgreements.Agreements.Count,
                "exactly one agreement must restore");
            FreightServiceAgreement restoredAgreement =
                target.FreightDriver.ServiceAgreements.Agreements[0];
            Assert.AreEqual(Q04AgreementId, restoredAgreement.AgreementId, "the agreement id must survive verbatim");
            Assert.AreEqual(FreightServiceAgreementStatus.Accepted, restoredAgreement.Status,
                "the agreement must stay Accepted");
            Assert.AreEqual(Q04ShipmentId, restoredAgreement.ShipmentId, "the agreement's shipment must restore");
            Assert.AreEqual(125, restoredAgreement.AgreedPriceCents, "the agreed 125c price must restore");
            Assert.AreEqual(source.TaskId.ToString(), restoredAgreement.DispatchTaskId,
                "the dispatch task id must restore");
            Assert.AreEqual(1, target.FreightDriver.ServiceAgreements.Requests.Count,
                "exactly one agreement request must restore");
            Assert.AreEqual(Q04AgreementRequestId,
                target.FreightDriver.ServiceAgreements.Requests[0].RequestId,
                "the request id must restore");

            // (11, settlement legs) Obligation open with exactly one
            // 25c payment and 100c outstanding; wholesale settlement
            // record settled at 500c; carrier settlement not
            // collected (the flag, not a second list, is the
            // authority — a restored mid-journey shipment settles
            // nothing).
            FinancialObligation obligation = target.Obligations.Find(source.ObligationId);
            Assert.IsNotNull(obligation, "the obligation must restore with its id");
            Assert.AreEqual(FinancialObligationStatus.Active, obligation.Status, "the obligation must stay Active");
            Assert.AreEqual(125, obligation.OriginalPrincipalCents, "the original 125c principal must restore");
            Assert.AreEqual(100, obligation.OutstandingPrincipalCents, "100c must stay outstanding after the one payment");
            Assert.AreEqual(1, obligation.Payments.Count, "exactly one payment must restore, never duplicated");
            Assert.AreEqual(25, obligation.Payments[0].AmountCents, "the payment amount must restore");
            Assert.AreEqual(source.ObligationId + ":1", obligation.Payments[0].PaymentId,
                "the payment id must restore");
            Assert.IsTrue(!obligation.Settled, "the obligation must stay unsettled");
            Assert.AreEqual(1, target.Pass9.WholesaleSettlements.Count,
                "exactly one wholesale settlement record must restore");
            Assert.AreEqual("lot-1", target.Pass9.WholesaleSettlements[0].BakeryLotId);
            Assert.IsTrue(target.Pass9.WholesaleSettlements[0].Settled, "the wholesale record must stay settled");
            Assert.AreEqual(500, target.Pass9.WholesaleSettlements[0].AmountCents,
                "the wholesale settlement amount must restore at 500c");
            Assert.IsNotNull(target.Pass9.CarrierSettlement, "the carrier settlement must exist on the target state");
            Assert.IsTrue(!target.Pass9.CarrierSettlement.AlreadySettled(Q04ShipmentId),
                "the mid-journey shipment must not read as settled after restore");

            // (12) Pending task: same id, fr.load-freight, open
            // (ResourcesReserved), 15 planned minutes, 0 worked, same
            // assignee, same reservation handle id.
            WorkTask task = target.Tasks.FindTask(source.TaskId);
            Assert.IsNotNull(task, "the pending task must restore with its id");
            Assert.AreEqual("fr.load-freight", task.DefinitionId, "the task definition must restore");
            Assert.IsTrue(task.IsOpen, "the task must stay open");
            Assert.AreEqual(TaskStatus.ResourcesReserved, task.Status, "the task must stay ResourcesReserved");
            Assert.AreEqual(15, task.PlannedMinutes, "the task must keep its 15 planned minutes");
            Assert.AreEqual(0, task.MinutesWorked, "the task must keep 0 minutes worked");
            Assert.AreEqual(EntityId.For(EntityKind.Person, 701), task.AssigneeId, "the assignee must restore");
            Assert.AreEqual(source.HandleId, task.ReservationHandleId, "the task's handle id must restore verbatim");
            Assert.AreEqual(1, target.Tasks.GetOpenTasks(EntityId.For(EntityKind.Business, 3)).Count,
                "exactly one open task must exist for the freight owner (the completed load task is not open)");

            // (13) Person reservations + equipment holds + due event
            // + budget: the shopper's Work + Shopping windows, the
            // task's loading-dock hold, the dispatch wagon hold, the
            // shipment due event, and P701's 15 committed minutes.
            // Orphan count: every open task holding a handle must
            // resolve it to a live reservation — zero orphans.
            List<PersonActivityReservation> active = target.Schedules.ActiveFor(1, Q04DayIndex);
            Assert.AreEqual(2, active.Count, "both person reservations must restore");
            PersonActivityReservation work = active.First(r => r.Activity == PersonActivityKind.Work);
            Assert.AreEqual("sched-2-0", work.ReservationId, "the Work reservation id must restore");
            Assert.AreEqual(480, work.StartMinuteOfDay, "the Work window must restore");
            Assert.AreEqual(240, work.DurationMinutes, "the Work duration must restore");
            PersonActivityReservation shopping = active.First(r => r.Activity == PersonActivityKind.Shopping);
            Assert.AreEqual("sched-2-1", shopping.ReservationId, "the Shopping reservation id must restore");
            Assert.AreEqual(720, shopping.StartMinuteOfDay, "the Shopping window must restore");
            Assert.AreEqual(95, shopping.DurationMinutes, "the Shopping duration must restore");
            List<ReservationHandle> taskHandles =
                target.Equipment.ReservationsForTask(source.TaskId.ToString());
            Assert.AreEqual(1, taskHandles.Count, "exactly one live hold must exist for the pending task");
            Assert.AreEqual(source.HandleId, taskHandles[0].ReservationId,
                "the task's hold must restore with its id");
            Assert.AreEqual(1, taskHandles[0].Items.Count, "the hold must carry exactly one item");
            Assert.AreEqual(Q04LoadingDockKey, taskHandles[0].Items[0].ResourceKey,
                "the hold must be on the loading dock (deviation (a))");
            Assert.AreEqual(2, target.Equipment.LiveReservationCount,
                "exactly two live holds must restore: the task's dock hold and the dispatch wagon hold");
            Assert.IsTrue(target.Equipment.IsReserved(
                ReservationKeyConventions.Asset("wagon-1"), 10f),
                "the dispatch wagon hold must still be live after restore");
            int orphans = 0;
            foreach (WorkTask open in target.Tasks.CaptureSaveDto().tasks)
            {
                if (open == null || !open.IsOpen) continue;
                if (string.IsNullOrWhiteSpace(open.ReservationHandleId)) continue;
                bool resolves = false;
                foreach (ReservationHandle held in target.Equipment.ReservationsForTask(open.TaskId.ToString()))
                    if (held.ReservationId == open.ReservationHandleId) resolves = true;
                if (!resolves) orphans++;
            }
            Assert.AreEqual(0, orphans, "no open task may hold a handle with no live reservation (D-Q04-4)");
            DueEventSchedulerSaveDto dueDto = target.DueEvents.CaptureSaveDto();
            Assert.AreEqual(1, dueDto.events.Count, "exactly one due event must restore");
            Assert.AreEqual(source.DueEventId, dueDto.events[0].EventId, "the due event id must restore");
            Assert.AreEqual(DueEventKind.TaskCompletionDue, dueDto.events[0].Kind, "the due event kind must restore");
            Assert.AreEqual(3600f, dueDto.events[0].DueGameSeconds, "the due event time must restore");
            Assert.AreEqual(Q04ShipmentId, dueDto.events[0].PayloadRef, "the due event payload must restore");
            WorkTimeBudget targetBudget = null;
            foreach (WorkTimeBudget budget in target.Budgets.CaptureSaveDto().budgets)
                if (budget != null && budget.PersonId == EntityId.For(EntityKind.Person, 701))
                    targetBudget = budget;
            Assert.IsNotNull(targetBudget, "P701's work-time budget must restore");
            Assert.AreEqual(600, targetBudget.DailyBudgetMinutes, "the daily budget must restore at 600 minutes");
            // 20 committed minutes, composed exactly: 15 for the open
            // fr.load-freight task + 5 for the shipment's load task
            // (base 5 = max(5, (16+9)/10)). The load task worked its
            // 5 minutes and completed with 0 remaining, so its
            // commitment is not released — only unused commitment is.
            Assert.AreEqual(20, targetBudget.MinutesCommitted, "the committed minutes must restore (15 + 5)");
            Assert.AreEqual(5, targetBudget.MinutesWorked,
                "the 5 worked minutes on the completed load task must restore");

            // (2, store side) Store: one retail lot, 24 loaves,
            // category pool 24, endowment recorded exactly once and
            // refusing a second application (no duplicate seed stock).
            Assert.AreEqual(1, target.Store.RetailItemLots.Count, "exactly one store retail lot must restore");
            BusinessRetailItemLotState storeLot = target.Store.RetailItemLots[0];
            Assert.AreEqual("store-lot-bread-1", storeLot.LotId, "the store lot id must survive verbatim");
            Assert.AreEqual(HouseholdItemCatalog.BreadId, storeLot.ItemId);
            Assert.AreEqual(24, storeLot.QuantityUnits, "the store lot must hold its 24 loaves");
            CategoryStockState category = target.Store.GetCategoryStock("staple_food");
            Assert.IsNotNull(category, "the staple_food category must restore");
            Assert.AreEqual(24, category.CurrentStockUnits, "the category pool must restore at 24");
            Assert.IsTrue(target.Store.HasAppliedScenarioEndowment("dev-opening-stock"),
                "the endowment must still read as applied after restore");
            Assert.IsTrue(!target.Store.RecordAppliedScenarioEndowment("dev-opening-stock"),
                "re-applying the endowment must refuse — no duplicate seed stock");
        }

        [Test]
        public void Q04_MidWorkRoundTrip_ExactConservation_AllThirteenItems()
        {
            Q04Scenario source = BuildQ04SourceScenario();
            Q04Captured captured = CaptureQ04Scenario(source);

            Q04Scenario target = NewQ04Scenario();
            RestoreQ04Scenario(captured, target);
            AssertQ04ScenarioRestored(source, target);
        }

        [Test]
        public void Q04_RepeatedLoad_NoDuplicateSettlementOrCharge()
        {
            Q04Scenario source = BuildQ04SourceScenario();
            Q04Captured captured = CaptureQ04Scenario(source);

            Q04Scenario target = NewQ04Scenario();
            RestoreQ04Scenario(captured, target);
            AssertQ04ScenarioRestored(source, target);

            // Second restore of the same DTO object into the SAME live
            // instances: no second settlement record, no second cash
            // debit/credit on any ledger, no second lot with the same
            // id in any inventory, no second shipment, no second
            // errand plan, no second reservation — the E-DEFECT-02
            // double-restore shape (same in-memory DTO, already-run
            // live state).
            RestoreQ04Scenario(captured, target);
            AssertQ04ScenarioRestored(source, target);

            // Third restore, still the same DTO object, not a fresh
            // serialization: the DTO itself must be pristine (no
            // restore mutated it), and the state must match again.
            Assert.AreEqual(1, captured.PassXl.series2Pass9.Freight.Shipments.Count,
                "the captured DTO must not have been mutated by restoring it");
            Assert.AreEqual(1, captured.PassXl.series2Pass9.WholesaleSettlements.Count,
                "the captured DTO's settlement list must be pristine after two restores");
            Assert.AreEqual(1, captured.Ledgers.Count, "the captured ledger list must be pristine");
            Assert.AreEqual(1, captured.Inventories.Count, "the captured inventory list must be pristine");
            RestoreQ04Scenario(captured, target);
            AssertQ04ScenarioRestored(source, target);
        }

        [Test]
        public void Q04_RoundTrip_ReceiptIdSurvivesInSourceLabel()
        {
            // D-Q04-1 pin: HouseholdItemLot carries no typed
            // receipt-id field — receipt identity survives only as
            // SourceLabel text. This test passes on that carriage
            // alone and records it: the receipt id is recoverable
            // after the round trip from the lot's own fields, via
            // SourceLabel. If a typed field is ever added, this
            // test's reflection pin fails on purpose so the
            // coordinator revisits D-Q04-1 instead of the carriage
            // silently changing. The slice does NOT add the field.
            Q04Scenario source = BuildQ04SourceScenario();
            Q04Captured captured = CaptureQ04Scenario(source);
            Q04Scenario target = NewQ04Scenario();
            RestoreQ04Scenario(captured, target);

            HouseholdItemLot breadLot = target.Inventories.Get(1).Lots.First(l => l.LotId == 1);
            Assert.IsTrue(breadLot.SourceLabel.Contains(Q04ReceiptId),
                "the receipt id must be recoverable from the restored lot's own fields: " + breadLot.SourceLabel);
            HouseholdItemLot flourLot = target.Inventories.Get(1).Lots.First(l => l.LotId == 2);
            Assert.IsTrue(!flourLot.SourceLabel.Contains(Q04ReceiptId),
                "the receipt id must be per-lot: the flour lot must not carry the bread receipt");

            System.Reflection.FieldInfo[] fields = typeof(HouseholdItemLot).GetFields();
            foreach (System.Reflection.FieldInfo field in fields)
            {
                Assert.IsTrue(!field.Name.ToLowerInvariant().Contains("receipt"),
                    "D-Q04-1: HouseholdItemLot must carry no typed receipt field yet — "
                    + "the receipt id survives via SourceLabel text only; field found: " + field.Name);
            }
        }
    }
}
