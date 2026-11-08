using System.Collections.Generic;
using System.Linq;
using LandLedgers.Economy;
using LandLedgers.Economy.BreadDrivers;
using LandLedgers.Economy.Businesses.Bakery;
using LandLedgers.Economy.Businesses.GeneralStore;
using LandLedgers.Economy.Freight;
using LandLedgers.Economy.Logistics;
using LandLedgers.Economy.Ordering;
using LandLedgers.Persistence;
using LandLedgers.Primitives;
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
    }
}
