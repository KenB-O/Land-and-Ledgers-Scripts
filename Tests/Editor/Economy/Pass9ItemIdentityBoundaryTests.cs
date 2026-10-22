using System.Collections.Generic;
using LandLedgers.Economy;
using LandLedgers.Economy.Businesses.Mine;
using LandLedgers.Population;
using LandLedgers.Primitives;
using LandLedgers.World;
using NUnit.Framework;
using EntityId = LandLedgers.Primitives.EntityId;

namespace LandLedgers.EditorTests.Economy
{
    /// <summary>
    /// Pass IX WS6 (IDENT-001): item identity at the boundaries —
    /// split/merge lot operations with preserved ancestry on both lot
    /// models (household lots, mine ore lots), and provenance recorded at
    /// a production output seam and consumed at an arrival seam through
    /// the shared category-stock ledger.
    /// </summary>
    [TestFixture]
    public sealed class Pass9ItemIdentityBoundaryTests
    {
        // ---------- Household lots ----------

        [Test]
        public void HouseholdLots_Split_PreservesFacetsAndAncestry()
        {
            var inventory = new HouseholdInventory(householdId: 3);
            Assert.IsNull(inventory.AddLot(HouseholdItemCatalog.FlourId, 10, dayIndex: 5,
                sourceLabel: "general store", qualityBand: "fine", sourceKind: "purchase", ownerPersonId: 7));
            int parentId = inventory.Lots[0].LotId;

            int childId = inventory.SplitLot(parentId, 4, dayIndex: 6);
            Assert.AreNotEqual(-1, childId);
            Assert.AreEqual(2, inventory.Lots.Count);
            HouseholdItemLot parent = null, child = null;
            foreach (HouseholdItemLot lot in inventory.Lots)
            {
                if (lot.LotId == parentId) parent = lot;
                if (lot.LotId == childId) child = lot;
            }
            Assert.IsNotNull(parent);
            Assert.IsNotNull(child);
            Assert.AreEqual(6, parent.QuantityUnits);
            Assert.AreEqual(4, child.QuantityUnits);
            Assert.AreEqual(10, inventory.GetTotalUnits(HouseholdItemCatalog.FlourId), "split conserves units");
            Assert.AreEqual("fine", child.QualityBand);
            Assert.AreEqual("purchase", child.SourceKind);
            Assert.AreEqual(7, child.OwnerPersonId);
            Assert.Contains(parentId, child.AncestryLotIds, "the child records its parent in its ancestry");

            // Refusals: whole-lot split, over-split, unknown lot.
            Assert.AreEqual(-1, inventory.SplitLot(parentId, 6, 7), "splitting off everything is not a split");
            Assert.AreEqual(-1, inventory.SplitLot(parentId, 99, 7));
            Assert.AreEqual(-1, inventory.SplitLot(12345, 1, 7));
            Assert.AreEqual(10, inventory.GetTotalUnits(HouseholdItemCatalog.FlourId), "refused splits change nothing");
        }

        [Test]
        public void HouseholdLots_Split_RespectsHeldUnits()
        {
            var inventory = new HouseholdInventory(householdId: 3);
            Assert.IsNull(inventory.AddLot(HouseholdItemCatalog.FlourId, 10, 5, "general store",
                "fine", "purchase", 7));
            int lotId = inventory.Lots[0].LotId;
            Assert.IsNull(inventory.HoldForCommitment(HouseholdItemCatalog.FlourId, 8, "shipment-1"));
            Assert.AreEqual(-1, inventory.SplitLot(lotId, 3, 6),
                "only 2 units are available — held units stay committed");
            Assert.AreNotEqual(-1, inventory.SplitLot(lotId, 2, 6));
        }

        [Test]
        public void HouseholdLots_Merge_OldestSurvives_AncestryCombined_IncompatibleUntouched()
        {
            var inventory = new HouseholdInventory(householdId: 4);
            Assert.IsNull(inventory.AddLot(HouseholdItemCatalog.FlourId, 5, dayIndex: 2, sourceLabel: "store", qualityBand: "fine", sourceKind: "purchase", ownerPersonId: 7));
            Assert.IsNull(inventory.AddLot(HouseholdItemCatalog.FlourId, 3, dayIndex: 9, sourceLabel: "store", qualityBand: "fine", sourceKind: "purchase", ownerPersonId: 7));
            Assert.IsNull(inventory.AddLot(HouseholdItemCatalog.FlourId, 7, dayIndex: 3, sourceLabel: "mill", qualityBand: "coarse", sourceKind: "purchase", ownerPersonId: 7));
            int oldestId = inventory.Lots[0].LotId;
            int mergedId = inventory.Lots[1].LotId;

            Assert.AreEqual(1, inventory.MergeCompatibleLots(dayIndex: 10));
            Assert.AreEqual(2, inventory.Lots.Count);
            HouseholdItemLot survivor = null;
            foreach (HouseholdItemLot lot in inventory.Lots)
            {
                if (lot.LotId == oldestId) survivor = lot;
            }
            Assert.IsNotNull(survivor, "the oldest lot survives");
            Assert.AreEqual(8, survivor.QuantityUnits);
            Assert.Contains(mergedId, survivor.AncestryLotIds, "combined ancestry is preserved on the survivor");
            Assert.AreEqual(15, inventory.GetTotalUnits(HouseholdItemCatalog.FlourId), "merge conserves units");

            // Nothing left to merge (the coarse lot differs in quality band).
            Assert.AreEqual(0, inventory.MergeCompatibleLots(dayIndex: 11));
        }

        // ---------- Mine ore lots ----------

        private static MineOreStock StockWithLot(EntityIdRegistry registry, int tons, float grade, int minedDay,
            out MineOreLot lot, string vein = "vein-1")
        {
            var stock = new MineOreStock();
            lot = MineOreLot.Create(registry, MineralResourceKind.Gold, tons, grade,
                "shaft-1", "level-1", vein, minedDay, "ore-gold");
            Assert.IsNull(stock.ReceiveLot(lot, new List<string>()));
            return stock;
        }

        [Test]
        public void MineLots_Split_NewRegistryId_AncestryRecorded()
        {
            var registry = new EntityIdRegistry();
            MineOreStock stock = StockWithLot(registry, 10, 2.0f, minedDay: 3, out MineOreLot parent);
            var diag = new List<string>();

            MineOreLot child = stock.SplitLot(registry, parent.LotId, 4, diag);
            Assert.IsNotNull(child, string.Join(";", diag));
            Assert.AreNotEqual(parent.LotId, child.LotId);
            Assert.AreEqual(6, parent.Tons);
            Assert.AreEqual(4, child.Tons);
            Assert.AreEqual(10, stock.TotalTons, "split conserves tons");
            Assert.AreEqual("shaft-1", child.ShaftId);
            Assert.AreEqual("vein-1", child.VeinId);
            Assert.Contains(parent.LotId.Id, new List<int>(child.AncestryLotSeqs));

            Assert.IsNull(stock.SplitLot(null, parent.LotId, 2, diag), "no registry, no split — no anonymous ore");
            Assert.IsNull(stock.SplitLot(registry, parent.LotId, 6, diag), "splitting off everything is refused");
            Assert.AreEqual(10, stock.TotalTons);
        }

        [Test]
        public void MineLots_Merge_SameShaftLevelVeinGradeBand_AncestryUnioned_AssayedNeverMerges()
        {
            var registry = new EntityIdRegistry();
            MineOreStock stock = StockWithLot(registry, 6, 2.0f, minedDay: 3, out MineOreLot oldest);
            MineOreLot sameBand = MineOreLot.Create(registry, MineralResourceKind.Gold, 6, 2.1f,
                "shaft-1", "level-1", "vein-1", minedDayIndex: 5, "ore-gold");
            Assert.IsNull(stock.ReceiveLot(sameBand, new List<string>()));
            MineOreLot otherVein = MineOreLot.Create(registry, MineralResourceKind.Gold, 5, 2.0f,
                "shaft-1", "level-1", "vein-2", minedDayIndex: 4, "ore-gold");
            Assert.IsNull(stock.ReceiveLot(otherVein, new List<string>()));
            MineOreLot assayed = MineOreLot.Create(registry, MineralResourceKind.Gold, 5, 2.0f,
                "shaft-1", "level-1", "vein-1", minedDayIndex: 6, "ore-gold");
            Assert.IsNull(assayed.RecordAssay("assay-1", EntityId.For(EntityKind.Person, 9), 6, 2.0f));
            Assert.IsNull(stock.ReceiveLot(assayed, new List<string>()));

            Assert.AreEqual(1, stock.MergeCompatibleLots(), "only the same-band unassayed pair merges");
            Assert.AreEqual(3, stock.Lots.Count, "merged pair + other-vein lot + assayed lot");
            MineOreLot survivor = stock.FindLot(oldest.LotId);
            Assert.IsNotNull(survivor, "the oldest lot (earliest mined day) survives");
            Assert.AreEqual(12, survivor.Tons);
            Assert.AreEqual(22, stock.TotalTons, "merge conserves tons");
            Assert.Contains(sameBand.LotId.Id, new List<int>(survivor.AncestryLotSeqs), "ancestry is unioned onto the survivor");
            Assert.AreEqual(2.05f, survivor.GradeValue, 0.001f, "grade blends ton-weighted");
            Assert.IsNotNull(stock.FindLot(assayed.LotId), "assayed lots never merge — their assay certifies one specific lot");
            Assert.AreEqual(5, stock.FindLot(assayed.LotId).Tons);
        }

        // ---------- Boundary provenance ----------

        [Test]
        public void Provenance_ProductionOutputSeam_RecordsRealInputLots_ExactlyOnce()
        {
            var ledger = new SharedCategoryStockLedger();
            var ids = new EntityIdRegistry();
            ProductionMethodDefinition method = GenericVerticalCatalog.Bread();
            var process = new ProductionProcessState { ProcessId = "bread-output-prov" };
            process.Start(method);
            process.Completed = true;
            process.ConsumedInputLotIds.AddRange(new[] { "lot-flour-1", "lot-fuel-1" });
            var configuration = new GenericBusinessConfiguration();

            Assert.IsTrue(GenericProductionOutputAuthority.TryCreateInventoryOutputOnce(
                process, method, configuration,
                new GenericProductDefinition("bread", "Bread", ProductQuantityUnit.Each),
                25, "bakery-process", 4, out string reason, ledger, ids), reason);

            Assert.AreEqual(1, ledger.Provenance.Count);
            StockProvenanceRecord record = ledger.Provenance[0];
            Assert.AreEqual(StockProvenanceOriginKind.Production, record.OriginKind);
            Assert.AreEqual("bread", record.CategoryId);
            Assert.AreEqual("bread-output-prov", record.OriginRef);
            Assert.AreEqual(new[] { "lot-flour-1", "lot-fuel-1" }, record.SourceLotIds.ToArray(),
                "the record names the REAL input lots captured at consumption");
            Assert.IsFalse(record.LotId.StartsWith("local-lot-"), "a registry was provided, so the lot id is registry-issued");

            // Idempotent output = no second provenance record.
            Assert.IsFalse(GenericProductionOutputAuthority.TryCreateInventoryOutputOnce(
                process, method, configuration,
                new GenericProductDefinition("bread", "Bread", ProductQuantityUnit.Each),
                25, "bakery-process", 4, out _, ledger, ids));
            Assert.AreEqual(1, ledger.Provenance.Count);
        }

        [Test]
        public void Provenance_Arrival_ConsumesTheExternalSupplyRecord()
        {
            var ledger = new SharedCategoryStockLedger();
            var ids = new EntityIdRegistry();
            StockProvenanceRecord supply = ledger.RecordExternalSupply("shipment:S-9", "grain", 40f, dayIndex: 1, ids: ids);
            Assert.IsNotNull(supply);
            Assert.IsFalse(supply.Consumed);

            var business = new BusinessInstanceState();
            Assert.IsTrue(GenericShipmentReceivingAuthority.TryReceive(
                business, "S-9", "grain", 40f, 100,
                new GenericProductDefinition("grain", "Grain", ProductQuantityUnit.Sack),
                dayIndex: 3, out string reason, ledger, ids), reason);
            Assert.IsTrue(supply.Consumed, "arrival consumes the record opened at dispatch");
            Assert.AreEqual(3, supply.ConsumedDayIndex);
            Assert.AreEqual(1, ledger.Provenance.Count, "arrival consumed the existing record — no duplicate entry event");

            // A second receipt of the same shipment is refused by the
            // receiving authority and creates no further provenance.
            Assert.IsFalse(GenericShipmentReceivingAuthority.TryReceive(
                business, "S-9", "grain", 40f, 100,
                new GenericProductDefinition("grain", "Grain", ProductQuantityUnit.Sack),
                dayIndex: 4, out _, ledger, ids));
            Assert.AreEqual(1, ledger.Provenance.Count);

            // Arrival with no dispatch record: goods enter under an
            // ExternalSupply record created and consumed by the arrival.
            StockProvenanceRecord entered = ledger.RecordArrival("shipment:S-10", "grain", 5f, "biz-1", dayIndex: 5, ids: ids);
            Assert.IsNotNull(entered);
            Assert.AreEqual(StockProvenanceOriginKind.ExternalSupply, entered.OriginKind);
            Assert.IsTrue(entered.Consumed);
        }

        [Test]
        public void Provenance_LedgerSaveDto_RoundTrips_AndLocalSequenceIsNegative()
        {
            var ledger = new SharedCategoryStockLedger();
            StockProvenanceRecord local = ledger.RecordProductionOutput("bread", 2f, "proc-1", "biz-1", null, dayIndex: 1);
            Assert.IsNotNull(local);
            Assert.AreEqual("local-lot--1", local.LotId,
                "without a registry allocator, lot ids come from the documented NEGATIVE local sequence");

            SharedCategoryStockLedger.SharedCategoryStockLedgerSaveDto dto = ledger.CaptureSaveDto();
            var restored = new SharedCategoryStockLedger();
            restored.LoadFromSaveDto(dto);
            Assert.AreEqual(1, restored.Provenance.Count);
            Assert.AreEqual("local-lot--1", restored.Provenance[0].LotId);
            StockProvenanceRecord next = restored.RecordProductionOutput("bread", 1f, "proc-2", "biz-1", null, dayIndex: 2);
            Assert.AreEqual("local-lot--2", next.LotId, "the local sequence survives the roundtrip — no id reuse");

            restored.LoadFromSaveDto(null);
            Assert.AreEqual(0, restored.Provenance.Count, "clear-then-load: a null section loads empty");
        }

        [Test]
        public void Provenance_InputLotCapture_AtTheConsumptionSeam()
        {
            // The generic executor captures FIFO lot ids when a process
            // starts; here the capture seam itself is exercised end to end:
            // a lot-identified input position consumed through the
            // configuration reports exactly the lot it drew from.
            var configuration = new GenericBusinessConfiguration();
            var flour = new GenericProductDefinition("flour", "Flour", ProductQuantityUnit.Sack,
                requiresLotIdentity: true);
            configuration.AddInventory("flour", 5f, 10, flour, "mill-delivery", dayIndex: 1);
            GenericInventoryPosition position = configuration.GetOrCreateInventory("flour");
            Assert.AreEqual(1, position.Lots.Count);
            string lotId = position.Lots[0].LotId;
            Assert.IsFalse(string.IsNullOrEmpty(lotId));

            var captured = new List<string>();
            Assert.IsTrue(configuration.TryConsumeInventoryCapturingLots("flour", 3f, captured, out float consumed));
            Assert.AreEqual(3f, consumed);
            Assert.AreEqual(new[] { lotId }, captured.ToArray());
        }
    }
}
