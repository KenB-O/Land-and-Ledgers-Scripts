// Land & Ledgers — F3 (2026-11-16, slice: consolidated mixed-cargo freight).
// A mixed load — one bed, one table, four chairs — dispatches as ONE
// shipment on ONE wagon trip, with per-line custody exactly once and a
// per-load capacity backstop that still refuses an over-capacity manifest.
// The plan's brief example: bed + table + 4 chairs = 93 kg / 1.45 m3, one
// trip on the 450 kg / 4 m3 wagon. Furniture measures come from the F0
// registry (FurnitureItemSpecs) through TryBuildManifestLine; no prices
// are invented here — the DEF-016 card prices per trip, and a
// consolidated shipment is one trip.

using System;
using System.Collections.Generic;
using NUnit.Framework;
using LandLedgers.Primitives;
using EntityId = LandLedgers.Primitives.EntityId;
using LandLedgers.Tasks;
using LandLedgers.Time;
using LandLedgers.Economy;
using LandLedgers.Economy.Blacksmith;
using LandLedgers.Economy.Logistics;
using LandLedgers.Economy.Storage;

namespace LandLedgers.Editor.Economy
{
    [TestFixture]
    public class ConsolidatedFreightF3Tests
    {
        private FreightShipmentService service;
        private TaskAuthority tasks;
        private WorkTimeBudgetStore budgets;
        private FreightShipmentHooks hooks;
        private EntityId loader;
        private EntityId driver;
        private EquipmentAsset wagon;
        private Dictionary<string, int> sourceLots;
        private Dictionary<string, int> destinationLots;
        private Dictionary<string, int> debitedShipments;
        private Dictionary<string, int> receivedShipments;
        private bool refuseReceipt;
        private string receiptRefusal;

        [SetUp]
        public void SetUp()
        {
            // Mirrors FreightShipmentServiceTests: day 42, one loader, one
            // driver, one production wagon (450 kg / 4 m3).
            tasks = new TaskAuthority();
            budgets = new WorkTimeBudgetStore();
            budgets.EnsureDay(42);
            service = new FreightShipmentService();
            loader = EntityId.For(EntityKind.Person, 701);
            driver = EntityId.For(EntityKind.Person, 702);
            wagon = new EquipmentAsset
            {
                AssetId = "opening-freight-wagon", Kind = "wagon", DisplayName = "Opening wagon",
                Condition01 = 1f, PayloadMassCapacityKg = 450f, CargoVolumeCapacityM3 = 4f,
            };
            service.RegisterWagon(wagon);

            sourceLots = new Dictionary<string, int>();
            destinationLots = new Dictionary<string, int>();
            debitedShipments = new Dictionary<string, int>();
            receivedShipments = new Dictionary<string, int>();
            refuseReceipt = false;
            receiptRefusal = string.Empty;

            hooks = new FreightShipmentHooks
            {
                DayIndex = 42,
                Tasks = tasks,
                Budgets = budgets,
                Diagnostics = new List<string>(),
                ReceiveShipment = (LogisticsShipmentState shipment, int quantityUnits, out string reason) =>
                {
                    reason = string.Empty;
                    return quantityUnits;
                },
                CommitConsolidatedSourceLots = (LogisticsShipmentState shipment, out string reason) =>
                {
                    reason = string.Empty;
                    // Idempotent per shipment, like the single-lot fixture:
                    // a second call for the same shipment debits nothing.
                    if (debitedShipments.ContainsKey(shipment.ShipmentId)) return true;
                    foreach (FreightManifestLine line in shipment.ManifestLines)
                    {
                        if (!sourceLots.TryGetValue(line.LotId, out int units) || units < line.Units)
                        {
                            reason = $"source stock shortfall on lot '{line.LotId}'";
                            return false;
                        }
                    }
                    foreach (FreightManifestLine line in shipment.ManifestLines)
                    {
                        sourceLots[line.LotId] -= line.Units;
                    }
                    debitedShipments[shipment.ShipmentId] = 1;
                    return true;
                },
                ReceiveConsolidatedCargoLots = (LogisticsShipmentState shipment, out string reason) =>
                {
                    reason = string.Empty;
                    if (refuseReceipt)
                    {
                        reason = receiptRefusal;
                        return false;
                    }
                    if (receivedShipments.ContainsKey(shipment.ShipmentId))
                    {
                        reason = "duplicate consolidated shipment receipt refused";
                        return false;
                    }
                    foreach (FreightManifestLine line in shipment.ManifestLines)
                    {
                        if (!destinationLots.ContainsKey(line.LotId)) destinationLots[line.LotId] = 0;
                        destinationLots[line.LotId] += line.Units;
                    }
                    receivedShipments[shipment.ShipmentId] = 1;
                    return true;
                },
            };
        }

        /// <summary>
        /// The plan's brief example: 1 bed (40 kg / 0.50 m3) + 1 table
        /// (25 kg / 0.35 m3) + 4 chairs (7 kg / 0.15 m3 each) = 93 kg /
        /// 1.45 m3, one trip on the 450 kg / 4 m3 wagon.
        /// </summary>
        private List<FreightManifestLine> MixedLoadLines()
        {
            var lines = new List<FreightManifestLine>();
            Assert.IsNull(FurnitureCargoSpecs.TryBuildManifestLine(FurnitureItemSpecs.BedId, "bed-lot-1", 1, out FreightManifestLine bed), "bed line must build");
            Assert.IsNull(FurnitureCargoSpecs.TryBuildManifestLine(FurnitureItemSpecs.TableId, "table-lot-1", 1, out FreightManifestLine table), "table line must build");
            Assert.IsNull(FurnitureCargoSpecs.TryBuildManifestLine(FurnitureItemSpecs.ChairId, "chair-lot-1", 4, out FreightManifestLine chairs), "chair line must build");
            lines.Add(bed);
            lines.Add(table);
            lines.Add(chairs);
            sourceLots["bed-lot-1"] = 1;
            sourceLots["table-lot-1"] = 1;
            sourceLots["chair-lot-1"] = 4;
            return lines;
        }

        private LogisticsShipmentState CreateMixedLoad(out string reason)
        {
            List<FreightManifestLine> lines = MixedLoadLines();
            return service.CreateConsolidatedShipment("mixed-load-1", "carpenter-1", "general-store-1",
                lines, "opening-freight-wagon", hooks, out reason);
        }

        /// <summary>
        /// Drives a planned shipment through load, dispatch, transit and
        /// unload until it completes (or a bounded guard runs out).
        /// </summary>
        private void DriveToCompleted(LogisticsShipmentState shipment)
        {
            Assert.IsTrue(service.BeginLoading(shipment, loader, hooks, out string loadingReason), loadingReason);
            int guard = 0;
            while (!shipment.loadApplied && guard++ < 10)
            {
                Assert.IsTrue(service.WorkLoading(shipment, 60, hooks, out string workReason), workReason);
            }
            Assert.IsTrue(shipment.loadApplied, "loading must complete");
            Assert.IsTrue(service.Dispatch(shipment, driver, null, hooks, out string dispatchReason), dispatchReason);
            service.AdvanceTransit(shipment, shipment.transitDurationGameSeconds, hooks);
            Assert.AreEqual(LogisticsShipmentStatus.Arrived, shipment.Status);
            Assert.IsTrue(service.BeginUnloading(shipment, loader, hooks, out string unloadReason), unloadReason);
            guard = 0;
            while (shipment.Status != LogisticsShipmentStatus.Completed && guard++ < 10)
            {
                Assert.IsTrue(service.WorkUnloading(shipment, 60, hooks, out string unloadWorkReason), unloadWorkReason);
            }
            Assert.AreEqual(LogisticsShipmentStatus.Completed, shipment.Status);
        }

        [Test]
        public void MixedLoad_BedTableFourChairs_IsOneShipmentOneTrip()
        {
            LogisticsShipmentState shipment = CreateMixedLoad(out string createReason);
            Assert.IsNotNull(shipment, createReason);
            Assert.IsTrue(shipment.IsConsolidated);
            Assert.AreEqual(LogisticsShipmentStatus.Planned, shipment.Status);
            Assert.AreEqual(3, shipment.ManifestLines.Count);
            Assert.AreEqual(6, shipment.PlannedQuantityUnits);
            Assert.AreEqual(93f, FreightConsolidatedManifest.TotalMassKg(shipment.ManifestLines), 0.01f);
            Assert.AreEqual(1.45f, FreightConsolidatedManifest.TotalVolumeM3(shipment.ManifestLines), 0.01f);

            // One wagon, one live shipment: a second shipment on the same
            // wagon — single-lot or consolidated — is refused.
            LogisticsShipmentState second = service.CreateShipment("second", "carpenter-1", "general-store-1",
                1, new FreightCargoSpec { MassKgPerUnit = 1f, VolumeM3PerUnit = 0.01f }, "opening-freight-wagon", hooks, out string secondReason);
            Assert.IsNull(second, "a second shipment must not bind the same wagon");
            StringAssert.Contains("already bound", secondReason);
            Assert.AreEqual(1, service.TrackedShipments.Count);
        }

        [Test]
        public void MixedLoad_FullLifecycle_PerLineCustodyExactlyOnce()
        {
            LogisticsShipmentState shipment = CreateMixedLoad(out string createReason);
            Assert.IsNotNull(shipment, createReason);
            DriveToCompleted(shipment);

            Assert.AreEqual(6, shipment.DeliveredQuantityUnits);
            Assert.AreEqual(0, shipment.RemainingQuantityUnits);
            Assert.IsTrue(shipment.deliveryApplied);
            Assert.IsTrue(shipment.sourceLotDebitApplied);

            // Every line debited exactly once, credited exactly once.
            Assert.AreEqual(0, sourceLots["bed-lot-1"]);
            Assert.AreEqual(0, sourceLots["table-lot-1"]);
            Assert.AreEqual(0, sourceLots["chair-lot-1"]);
            Assert.AreEqual(1, destinationLots["bed-lot-1"]);
            Assert.AreEqual(1, destinationLots["table-lot-1"]);
            Assert.AreEqual(4, destinationLots["chair-lot-1"]);
            Assert.AreEqual(1, debitedShipments["mixed-load-1"], "debit hook must run once per shipment");
            Assert.AreEqual(1, receivedShipments["mixed-load-1"], "receive hook must run once per shipment");

            // Conservation: planned == delivered + remaining.
            Assert.IsTrue(service.VerifyConservation(shipment, 6, out string conservationReason), conservationReason);
        }

        [Test]
        public void OverVolumeManifest_BlockedPreLoad_ZeroCargo()
        {
            // 9 beds = 4.5 m3 on a 4 m3 wagon: refused before loading. The
            // service convention (as in CreateShipment) is to return the
            // blocked record itself — Failed, owning zero cargo — not null,
            // so the caller can inspect the refusal.
            Assert.IsNull(FurnitureCargoSpecs.TryBuildManifestLine(FurnitureItemSpecs.BedId, "bed-lot-9", 9, out FreightManifestLine beds), "9-bed line must build");
            sourceLots["bed-lot-9"] = 9;
            LogisticsShipmentState shipment = service.CreateConsolidatedShipment("over-volume", "carpenter-1", "general-store-1",
                new List<FreightManifestLine> { beds }, "opening-freight-wagon", hooks, out string createReason);
            Assert.IsNotNull(shipment, "an over-volume manifest returns its blocked record, not a phantom");
            Assert.IsTrue(shipment.IsPreLoadBlocked);
            Assert.AreEqual(LogisticsShipmentStatus.Failed, shipment.Status);
            Assert.IsFalse(shipment.loadApplied);
            Assert.AreEqual(0, shipment.RemainingQuantityUnits, "a blocked record owns zero cargo");
            StringAssert.Contains("volume", shipment.BlockedReason);
            Assert.IsFalse(service.BeginLoading(shipment, loader, hooks, out string loadingReason), "a blocked shipment cannot begin loading");
            Assert.AreEqual(9, sourceLots["bed-lot-9"], "no source stock may move on a refused manifest");
            Assert.AreEqual(1, service.TrackedShipments.Count, "the blocked record is tracked");
        }

        [Test]
        public void OverMassManifest_BlockedPreLoad_ZeroCargo()
        {
            // 12 beds = 480 kg on a 450 kg wagon: refused before loading.
            // Same blocked-record convention as the over-volume case.
            Assert.IsNull(FurnitureCargoSpecs.TryBuildManifestLine(FurnitureItemSpecs.BedId, "bed-lot-12", 12, out FreightManifestLine beds), "12-bed line must build");
            sourceLots["bed-lot-12"] = 12;
            LogisticsShipmentState shipment = service.CreateConsolidatedShipment("over-mass", "carpenter-1", "general-store-1",
                new List<FreightManifestLine> { beds }, "opening-freight-wagon", hooks, out string createReason);
            Assert.IsNotNull(shipment, "an over-mass manifest returns its blocked record, not a phantom");
            Assert.IsTrue(shipment.IsPreLoadBlocked);
            Assert.AreEqual(LogisticsShipmentStatus.Failed, shipment.Status);
            Assert.IsFalse(shipment.loadApplied);
            Assert.AreEqual(0, shipment.RemainingQuantityUnits, "a blocked record owns zero cargo");
            StringAssert.Contains("mass", shipment.BlockedReason);
            Assert.IsFalse(service.BeginLoading(shipment, loader, hooks, out string loadingReason), "a blocked shipment cannot begin loading");
            Assert.AreEqual(12, sourceLots["bed-lot-12"], "no source stock may move on a refused manifest");
            Assert.AreEqual(1, service.TrackedShipments.Count, "the blocked record is tracked");
        }

        [Test]
        public void ManifestCapacityRecheck_BlocksWhenWagonShrinks()
        {
            // A manifest that fits at creation must still be refused at
            // BeginLoading if the wagon's capacity shrinks in between.
            LogisticsShipmentState shipment = CreateMixedLoad(out string createReason);
            Assert.IsNotNull(shipment, createReason);
            wagon.CargoVolumeCapacityM3 = 1f;
            Assert.IsFalse(service.BeginLoading(shipment, loader, hooks, out string loadingReason));
            StringAssert.Contains("volume", loadingReason);
            Assert.AreEqual(LogisticsShipmentStatus.Failed, shipment.Status);
            Assert.IsFalse(shipment.loadApplied);
            Assert.IsTrue(tasks.GetDefinition("freight:load:mixed-load-1") == null, "no load task may be created for a refused shipment");
            Assert.AreEqual(1, sourceLots["bed-lot-1"], "source stock untouched");
        }

        [Test]
        public void DuplicateLotAssignment_Refused()
        {
            // A lot already bound to a live single-lot shipment cannot be
            // pulled into a consolidated manifest.
            LogisticsShipmentState first = service.CreateShipment("lot-owner-1", "mill-1", "bakery-1",
                2, new FreightCargoSpec { MassKgPerUnit = 1f, VolumeM3PerUnit = 0.01f }, "opening-freight-wagon", hooks, out string firstReason);
            Assert.IsNotNull(first, firstReason);
            Assert.IsTrue(service.TryBindSourceLot(first, "shared-lot", "bakery_flour_sack", out string firstBind), firstBind);

            Assert.IsNull(FurnitureCargoSpecs.TryBuildManifestLine(FurnitureItemSpecs.BedId, "shared-lot", 1, out FreightManifestLine sharedLine), "shared line must build");
            LogisticsShipmentState second = service.CreateConsolidatedShipment("dup-lot", "carpenter-1", "general-store-1",
                new List<FreightManifestLine> { sharedLine }, "opening-freight-wagon", hooks, out string secondReason);
            Assert.IsNull(second, "a lot already assigned elsewhere must be refused");
            StringAssert.Contains("already assigned", secondReason);

            // And a duplicate lot inside one manifest is refused too.
            Assert.IsNull(FurnitureCargoSpecs.TryBuildManifestLine(FurnitureItemSpecs.BedId, "dup-lot-1", 1, out FreightManifestLine firstLine), "first line must build");
            Assert.IsNull(FurnitureCargoSpecs.TryBuildManifestLine(FurnitureItemSpecs.ChairId, "dup-lot-1", 2, out FreightManifestLine secondLine), "second line must build");
            LogisticsShipmentState third = service.CreateConsolidatedShipment("dup-inside", "carpenter-1", "general-store-1",
                new List<FreightManifestLine> { firstLine, secondLine }, "opening-freight-wagon", hooks, out string thirdReason);
            Assert.IsNull(third, "a manifest listing one lot twice must be refused");
            StringAssert.Contains("appears twice", thirdReason);
        }

        [Test]
        public void TryBindSourceLot_OnConsolidated_Refuses()
        {
            LogisticsShipmentState shipment = CreateMixedLoad(out string createReason);
            Assert.IsNotNull(shipment, createReason);
            Assert.IsFalse(service.TryBindSourceLot(shipment, "extra-lot", FurnitureItemSpecs.BedId, out string bindReason));
            StringAssert.Contains("consolidated", bindReason);
            Assert.AreEqual(3, shipment.ManifestLines.Count, "the manifest is unchanged by a refused bind");
        }

        [Test]
        public void ConsolidatedLoad_WithoutDebitHook_RefusesLoudly()
        {
            LogisticsShipmentState shipment = CreateMixedLoad(out string createReason);
            Assert.IsNotNull(shipment, createReason);
            hooks.CommitConsolidatedSourceLots = null;
            Assert.IsFalse(service.BeginLoading(shipment, loader, hooks, out string loadingReason));
            StringAssert.Contains("source-debit", loadingReason);
            Assert.AreEqual(LogisticsShipmentStatus.Planned, shipment.Status, "the shipment stays Planned for a retry once the hook returns");
            Assert.IsTrue(tasks.GetDefinition("freight:load:mixed-load-1") == null, "no load task may be created without the debit authority");
            Assert.AreEqual(1, sourceLots["bed-lot-1"], "no source stock may move without the debit authority");
            Assert.AreEqual(0, debitedShipments.Count);
        }

        [Test]
        public void ConsolidatedReceive_Refusal_HoldsCargoInWagon()
        {
            LogisticsShipmentState shipment = CreateMixedLoad(out string createReason);
            Assert.IsNotNull(shipment, createReason);
            Assert.IsTrue(service.BeginLoading(shipment, loader, hooks, out string loadingReason), loadingReason);
            Assert.IsTrue(service.WorkLoading(shipment, 60, hooks, out string workReason), workReason);
            Assert.IsTrue(service.Dispatch(shipment, driver, null, hooks, out string dispatchReason), dispatchReason);
            service.AdvanceTransit(shipment, shipment.transitDurationGameSeconds, hooks);
            Assert.AreEqual(LogisticsShipmentStatus.Arrived, shipment.Status);

            refuseReceipt = true;
            receiptRefusal = "general store is closed; no receiver on duty";
            Assert.IsTrue(service.BeginUnloading(shipment, loader, hooks, out string unloadReason), unloadReason);
            Assert.IsTrue(service.WorkUnloading(shipment, 60, hooks, out string unloadWorkReason), unloadWorkReason);
            Assert.AreEqual(LogisticsShipmentStatus.Delayed, shipment.Status, "a refused receipt delays the shipment, it does not drop the cargo");
            StringAssert.Contains("closed", shipment.BlockedReason);
            Assert.AreEqual(6, shipment.RemainingQuantityUnits, "the cargo stays in wagon custody");
            Assert.IsFalse(shipment.deliveryApplied);
            Assert.AreEqual(0, destinationLots.Count, "nothing is credited on a refused receipt");
        }

        [Test]
        public void Manifest_SurvivesSaveLoad()
        {
            LogisticsShipmentState shipment = CreateMixedLoad(out string createReason);
            Assert.IsNotNull(shipment, createReason);
            FreightShipmentService.FreightServiceSaveDto dto = service.CaptureSaveDto(new List<LogisticsShipmentState> { shipment });
            Assert.AreEqual(1, dto.Manifests.Count);

            var restoredService = new FreightShipmentService();
            List<LogisticsShipmentState> restored = restoredService.RestoreSaveDto(dto, hooks);
            Assert.AreEqual(1, restored.Count);
            LogisticsShipmentState reloaded = restored[0];
            Assert.IsTrue(reloaded.IsConsolidated);
            Assert.AreEqual(3, reloaded.ManifestLines.Count);
            Assert.AreEqual("bed-lot-1", reloaded.ManifestLines[0].LotId);
            Assert.AreEqual(FurnitureItemSpecs.BedId, reloaded.ManifestLines[0].SkuId);
            Assert.AreEqual(1, reloaded.ManifestLines[0].Units);
            Assert.AreEqual(40f, reloaded.ManifestLines[0].MassKgPerUnit, 0.01f);
            Assert.AreEqual(0.5f, reloaded.ManifestLines[0].VolumeM3PerUnit, 0.01f);
            Assert.AreEqual("chair-lot-1", reloaded.ManifestLines[2].LotId);
            Assert.AreEqual(4, reloaded.ManifestLines[2].Units);
            Assert.AreEqual(6, reloaded.PlannedQuantityUnits);
            Assert.AreEqual(93f, FreightConsolidatedManifest.TotalMassKg(reloaded.ManifestLines), 0.01f);
            Assert.AreEqual(1.45f, FreightConsolidatedManifest.TotalVolumeM3(reloaded.ManifestLines), 0.01f);

            // The restored record is its own copy: mutating it must not
            // move the live record's lines.
            reloaded.manifestLines[0].Units = 99;
            Assert.AreEqual(1, shipment.ManifestLines[0].Units);
        }

        [Test]
        public void TryBuildManifestLine_RefusesBadInput()
        {
            Assert.IsNotNull(FurnitureCargoSpecs.TryBuildManifestLine("nonexistent-item", "lot-1", 1, out FreightManifestLine unknownLine));
            Assert.IsNull(unknownLine, "an unknown item builds no line");
            Assert.IsNotNull(FurnitureCargoSpecs.TryBuildManifestLine(FurnitureItemSpecs.BedId, "  ", 1, out FreightManifestLine emptyLotLine));
            Assert.IsNull(emptyLotLine, "an empty lot id builds no line");
            Assert.IsNotNull(FurnitureCargoSpecs.TryBuildManifestLine(FurnitureItemSpecs.BedId, "lot-1", 0, out FreightManifestLine zeroLine));
            Assert.IsNull(zeroLine, "zero units build no line");
            Assert.IsNotNull(FurnitureCargoSpecs.TryBuildManifestLine(FurnitureItemSpecs.BedId, "lot-1", -2, out FreightManifestLine negativeLine));
            Assert.IsNull(negativeLine, "negative units build no line");
        }
    }
}
