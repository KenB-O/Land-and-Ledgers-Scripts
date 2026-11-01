using EntityId = LandLedgers.Primitives.EntityId;

using System.Collections.Generic;
using LandLedgers.Economy;
using LandLedgers.Economy.Businesses.LumberYard;
using LandLedgers.Economy.Businesses.Sawmill;
using LandLedgers.Economy.Trade;
using LandLedgers.Primitives;
using NUnit.Framework;

namespace LandLedgers.Editor.Economy
{
    /// <summary>
    /// W4B: LumberYard runtime — the town-facing storage and retail arm
    /// (Canon §8.2). Lumber intake from named sawmills with full provenance
    /// (any named mill: the yard is trade, not production), yard-assigned
    /// grades as data, nails/simple-hardware intake from named blacksmiths
    /// (survey: the smith is the real supplier, forging from EQU-1 imported
    /// iron), the minimal EQU-1 hardware import fallback, the one-time
    /// opening endowment, construction-material sales in the Canon cost
    /// buckets with loud refusal on shortfall, and save/load.
    /// </summary>
    [TestFixture]
    public sealed class LumberYardRuntimeTests
    {
        private static SawmillLumberLot DocumentedMillLot(int lotNumber, int units, string millId, string standId)
        {
            return new SawmillLumberLot
            {
                LotId = EntityId.For(EntityKind.Lot, lotNumber),
                LumberUnits = units,
                SourceLogLotId = "log-" + lotNumber,
                StandId = standId,
                Species = "white pine",
                MillBusinessId = millId,
                SawedBy = EntityId.For(EntityKind.Person, 5002),
                SawedDayIndex = 210,
                ConversionProfileId = "softwood-standard",
            };
        }

        private static LumberYardShopRuntime StockedYard()
        {
            var yard = new LumberYardShopRuntime("yard-1", new EntityIdRegistry());
            var diag = new List<string>();
            Assert.IsNull(yard.ReceiveLumberFromSawmill(DocumentedMillLot(9301, 40, "mill-1", "stand-1"), "merchantable", 220, 85, diag));
            Assert.IsNull(yard.ReceiveLumberFromSawmill(DocumentedMillLot(9302, 30, "mill-2", "stand-2"), "clear", 230, 95, diag));
            Assert.IsNull(yard.ReceiveHardwareFromBlacksmith(LumberYardHardwareKind.Nails, 20, "smith-1", 218, "iron-order-7", 220, 30, diag));
            Assert.IsNull(yard.ReceiveHardwareFromBlacksmith(LumberYardHardwareKind.SimpleHardware, 10, "smith-1", 219, "iron-order-7", 221, 45, diag));
            return yard;
        }

        // ---- Lumber intake: provenance validation ----

        [Test]
        public void LumberIntake_Refuses_LotWithoutStand_Loudly()
        {
            var yard = new LumberYardShopRuntime("yard-1", new EntityIdRegistry());
            var diag = new List<string>();

            var orphan = DocumentedMillLot(9301, 40, "mill-1", string.Empty);
            string refusal = yard.ReceiveLumberFromSawmill(orphan, "merchantable", 220, 85, diag);

            Assert.NotNull(refusal, "Stand-less lumber must be refused.");
            Assert.AreEqual(0, yard.LumberStock.TotalLumberUnits, "Refused lumber touches nothing.");
        }

        [Test]
        public void LumberIntake_Refuses_AnonymousLot()
        {
            var yard = new LumberYardShopRuntime("yard-1", new EntityIdRegistry());
            var diag = new List<string>();

            var lot = DocumentedMillLot(9301, 40, "mill-1", "stand-1");
            lot.LotId = EntityId.Invalid;
            Assert.NotNull(yard.ReceiveLumberFromSawmill(lot, "merchantable", 220, 85, diag));
            Assert.AreEqual(0, yard.LumberStock.TotalLumberUnits);
        }

        [Test]
        public void LumberIntake_Accepts_AnyNamedMill_IndependentYard()
        {
            var yard = new LumberYardShopRuntime("yard-1", new EntityIdRegistry());
            var diag = new List<string>();

            // Canon §8.2: the yard is "either independent or paired with an
            // owned mill" — unlike the mill's own production stock (W4A),
            // foreign-mill lumber is welcome trade here, recorded honestly.
            Assert.IsNull(yard.ReceiveLumberFromSawmill(DocumentedMillLot(9301, 40, "mill-9", "stand-1"), "clear", 220, 85, diag));

            Assert.AreEqual(40, yard.LumberStock.TotalLumberUnits);
            var lot = yard.LumberStock.Lots[0];
            Assert.AreEqual("mill-9", lot.SourceMillBusinessId, "The source mill is recorded, never hidden.");
            Assert.AreEqual("stand-1", lot.StandId);
            Assert.AreEqual(EntityId.For(EntityKind.Lot, 9301).ToString(), lot.SourceSawmillLotId);
            Assert.AreEqual("clear", lot.YardGradeId);
            Assert.IsTrue(lot.ProvenanceChain().Contains("mill-9"));
            Assert.IsTrue(lot.ProvenanceChain().Contains("stand-1"));
        }

        [Test]
        public void LumberIntake_AssignsDefaultGrade_Loudly_WhenUndeclared()
        {
            var yard = new LumberYardShopRuntime("yard-1", new EntityIdRegistry());
            var diag = new List<string>();

            Assert.IsNull(yard.ReceiveLumberFromSawmill(DocumentedMillLot(9301, 40, "mill-1", "stand-1"), null, 220, 85, diag));

            Assert.AreEqual(LumberYardGradeCatalog.MerchantableGradeId, yard.LumberStock.Lots[0].YardGradeId);
            Assert.IsTrue(string.Join(" ", diag).Contains(LumberYardGradeCatalog.MerchantableGradeId),
                "The default sort must be recorded loudly, not silently invented.");
        }

        [Test]
        public void LumberIntake_Refuses_UnknownGrade()
        {
            var yard = new LumberYardShopRuntime("yard-1", new EntityIdRegistry());
            var diag = new List<string>();

            Assert.NotNull(yard.ReceiveLumberFromSawmill(DocumentedMillLot(9301, 40, "mill-1", "stand-1"), "select-fancy", 220, 85, diag),
                "Grades are data — unknown ids are data errors, refused.");
            Assert.AreEqual(0, yard.LumberStock.TotalLumberUnits);
        }

        [Test]
        public void PurchaseIntake_Refuses_AnonymousSeller()
        {
            var yard = new LumberYardShopRuntime("yard-1", new EntityIdRegistry());
            var diag = new List<string>();

            Assert.NotNull(yard.ReceiveLumberPurchase(25, "oak", "merchantable", string.Empty, "freight manifest 12", 220, 90, diag));
            Assert.AreEqual(0, yard.LumberStock.TotalLumberUnits);
        }

        [Test]
        public void PurchaseIntake_Accepts_NamedSeller_WithProvenance()
        {
            var yard = new LumberYardShopRuntime("yard-1", new EntityIdRegistry());
            var diag = new List<string>();

            Assert.IsNull(yard.ReceiveLumberPurchase(25, "oak", "merchantable", "regional-freight-depot", "wagon manifest 12", 220, 90, diag));

            var lot = yard.LumberStock.Lots[0];
            Assert.AreEqual(25, lot.LumberUnits);
            Assert.AreEqual(LumberYardLumberSourceKind.Purchase, lot.SourceKind);
            Assert.IsTrue(lot.ProvenanceChain().Contains("regional-freight-depot"));
        }

        [Test]
        public void ImportArrival_Requires_NamedOrderAndOrigin()
        {
            var yard = new LumberYardShopRuntime("yard-1", new EntityIdRegistry());
            var diag = new List<string>();

            Assert.NotNull(yard.ReceiveLumberImportArrival(25, "white pine", "merchantable", string.Empty, "Off-map timber country", 220, 25, diag),
                "Import order id is required — no orphan stock.");
            Assert.NotNull(yard.ReceiveLumberImportArrival(25, "white pine", "merchantable", "imp-3", string.Empty, 220, 25, diag),
                "Origin must be named — no anonymous sources.");
            Assert.AreEqual(0, yard.LumberStock.TotalLumberUnits);

            Assert.IsNull(yard.ReceiveLumberImportArrival(25, "white pine", "merchantable", "imp-3", "Off-map timber country, via wagon road", 220, 25, diag));
            Assert.AreEqual(25, yard.LumberStock.TotalLumberUnits);
            Assert.AreEqual(LumberYardLumberSourceKind.Import, yard.LumberStock.Lots[0].SourceKind);
        }

        // ---- FIFO withdrawal ----

        [Test]
        public void LumberWithdrawal_FIFO_OldestAcquisitionFirst()
        {
            var yard = StockedYard();
            var diag = new List<string>();

            // 40 merchantable (day 220) + 30 clear (day 230); withdraw 50.
            var lines = yard.LumberStock.TryWithdrawUnits(50, null, null, diag);

            Assert.AreEqual(2, lines.Count);
            Assert.AreEqual(40, lines[0].UnitsTaken, "Oldest acquisition drains first.");
            Assert.AreEqual("merchantable", lines[0].YardGradeId);
            Assert.AreEqual(10, lines[1].UnitsTaken);
            Assert.AreEqual("clear", lines[1].YardGradeId);
            Assert.AreEqual(20, yard.LumberStock.TotalLumberUnits);
            Assert.IsTrue(lines[0].ProvenanceChain.Contains("stand-1"));
            Assert.IsTrue(lines[1].ProvenanceChain.Contains("stand-2"));
        }

        [Test]
        public void LumberWithdrawal_Shortfall_ReturnsPartial_AndNotes()
        {
            var yard = StockedYard();
            var diag = new List<string>();

            var lines = yard.LumberStock.TryWithdrawUnits(500, null, null, diag);

            int taken = 0;
            foreach (var line in lines) taken += line.UnitsTaken;
            Assert.AreEqual(70, taken, "Shortfall returns what exists — never invented units.");
            Assert.AreEqual(0, yard.LumberStock.TotalLumberUnits);
            Assert.IsTrue(string.Join(" ", diag).Contains("shortfall"));
        }

        [Test]
        public void LumberWithdrawal_SpeciesFilter_SelectsMatchingLots()
        {
            var yard = new LumberYardShopRuntime("yard-1", new EntityIdRegistry());
            var diag = new List<string>();
            var pine = DocumentedMillLot(9301, 40, "mill-1", "stand-1");
            var oak = DocumentedMillLot(9302, 30, "mill-1", "stand-2");
            oak.Species = "oak";
            Assert.IsNull(yard.ReceiveLumberFromSawmill(pine, "merchantable", 220, 85, diag));
            Assert.IsNull(yard.ReceiveLumberFromSawmill(oak, "merchantable", 221, 95, diag));

            var lines = yard.LumberStock.TryWithdrawUnits(25, "oak", null, diag);

            Assert.AreEqual(1, lines.Count);
            Assert.AreEqual(25, lines[0].UnitsTaken);
            Assert.AreEqual("oak", lines[0].Species);
            Assert.AreEqual(45, yard.LumberStock.TotalLumberUnits, "Unmatched lots stay on the shelf.");
        }

        // ---- Hardware intake: the blacksmith is the real supplier ----

        [Test]
        public void HardwareIntake_Refuses_AnonymousBlacksmith_Loudly()
        {
            var yard = new LumberYardShopRuntime("yard-1", new EntityIdRegistry());
            var diag = new List<string>();

            string refusal = yard.ReceiveHardwareFromBlacksmith(
                LumberYardHardwareKind.Nails, 20, string.Empty, 218, "iron-order-7", 220, 30, diag);

            Assert.NotNull(refusal, "The forging smith must be named — anonymous hardware is refused.");
            Assert.AreEqual(0, yard.HardwareStock.TotalHardwareUnits);
        }

        [Test]
        public void HardwareIntake_Accepts_NamedBlacksmith_WithProvenance()
        {
            var yard = new LumberYardShopRuntime("yard-1", new EntityIdRegistry());
            var diag = new List<string>();

            Assert.IsNull(yard.ReceiveHardwareFromBlacksmith(
                LumberYardHardwareKind.Nails, 20, "smith-1", 218, "iron-order-7", 220, 30, diag));

            var lot = yard.HardwareStock.Lots[0];
            Assert.AreEqual(20, lot.HardwareUnits);
            Assert.AreEqual("smith-1", lot.SourceBlacksmithBusinessId);
            Assert.AreEqual("iron-order-7", lot.IronImportOrderId, "The iron it was forged from is recorded when known.");
            Assert.IsTrue(lot.ProvenanceChain().Contains("smith-1"));
        }

        [Test]
        public void HardwareImportArrival_Requires_NamedOrderAndOrigin()
        {
            var yard = new LumberYardShopRuntime("yard-1", new EntityIdRegistry());
            var diag = new List<string>();

            Assert.NotNull(yard.ReceiveHardwareImportArrival(LumberYardHardwareKind.Nails, 20, string.Empty, "Pittsburgh ironworks", 220, 30, diag));
            Assert.AreEqual(0, yard.HardwareStock.TotalHardwareUnits);

            Assert.IsNull(yard.ReceiveHardwareImportArrival(LumberYardHardwareKind.Nails, 20, "hw-imp-2", "Pittsburgh ironworks, via railhead", 220, 30, diag));
            Assert.AreEqual(20, yard.HardwareStock.TotalHardwareUnits);
        }

        [Test]
        public void HardwareWithdrawal_NailsFillBefore_SimpleHardware()
        {
            var yard = StockedYard();
            var diag = new List<string>();

            // 20 nails + 10 simple hardware; unfiltered withdraw of 25.
            var lines = yard.HardwareStock.TryWithdrawUnits(25, null, diag);

            Assert.AreEqual(2, lines.Count);
            Assert.AreEqual(LumberYardHardwareKind.Nails, lines[0].HardwareKind);
            Assert.AreEqual(20, lines[0].UnitsTaken, "Nails are the primary fill for the bucket.");
            Assert.AreEqual(LumberYardHardwareKind.SimpleHardware, lines[1].HardwareKind);
            Assert.AreEqual(5, lines[1].UnitsTaken);
            Assert.AreEqual(5, yard.HardwareStock.TotalHardwareUnits);
        }

        // ---- EQU-1 import supply registration ----

        [Test]
        public void Supply_RegistersHardwareImportMaterial_WithNamedOrigin()
        {
            var diag = new List<string>();

            LumberYardSupply.EnsureHardwareImportable(diag);

            ImportMaterial material = ImportCatalog.Get(LumberYardSupply.HardwareImportMaterialId);
            Assert.NotNull(material, "Nails/hardware must be importable per the EQU-1 precedent.");
            Assert.IsFalse(string.IsNullOrWhiteSpace(material.OriginName), "No anonymous sources — the origin is named.");
            Assert.Greater(material.DistanceMiles, 0);
            Assert.GreaterOrEqual(material.TransitDays, 1, "Off-map transitions preserve real transit time.");
        }

        // ---- One-time opening endowment ----

        [Test]
        public void OpeningStock_AppliesOnce_SecondCallRefused()
        {
            var yard = new LumberYardShopRuntime("yard-1", new EntityIdRegistry());
            var diag = new List<string>();

            Assert.IsNull(LumberYardOpeningStock.ApplyOneTime(yard, 60, "white pine", 30, 12, 200, diag));
            Assert.IsTrue(yard.OpeningStockApplied);
            Assert.AreEqual(60, yard.LumberStock.TotalLumberUnits);
            Assert.AreEqual(42, yard.HardwareStock.TotalHardwareUnits);
            Assert.IsTrue(yard.LumberStock.Lots[0].IsBootstrapEndowment);
            Assert.IsTrue(yard.HardwareStock.Lots[0].IsBootstrapEndowment);
            Assert.IsTrue(yard.LumberStock.Lots[0].ProvenanceChain().Contains("OPENING ENDOWMENT"));

            string refusal = LumberYardOpeningStock.ApplyOneTime(yard, 60, "white pine", 30, 12, 200, diag);
            Assert.NotNull(refusal, "The endowment is one-time — it never repeats.");
            Assert.AreEqual(60, yard.LumberStock.TotalLumberUnits, "The refused re-application touches nothing.");
            Assert.AreEqual(42, yard.HardwareStock.TotalHardwareUnits);
        }

        // ---- Construction sales: real lots, loud refusal ----

        [Test]
        public void ConstructionSale_SellsRealLots_WithProvenance()
        {
            var yard = StockedYard();
            var diag = new List<string>();

            var record = yard.TrySellToConstructionProject("project-house-3", 50, 95, 15, 35, 240, diag);

            Assert.NotNull(record, "A covered sale must succeed.");
            Assert.AreEqual("project-house-3", record.BuyerLabel);
            Assert.IsTrue(record.IsConstructionSale);
            Assert.AreEqual(50, record.UnitsSoldFor(ConstructionResourceKind.Lumber));
            Assert.AreEqual(15, record.UnitsSoldFor(ConstructionResourceKind.Nails));
            Assert.AreEqual(50 * 95 + 15 * 35, record.TotalCents);
            Assert.AreEqual(20, yard.LumberStock.TotalLumberUnits, "Real lots leave the shelf.");
            Assert.AreEqual(15, yard.HardwareStock.TotalHardwareUnits);
            Assert.AreEqual(1, yard.SalesHistory.Count);

            string provenance = string.Join(" | ", record.AllProvenanceLines());
            Assert.IsTrue(provenance.Contains("stand-1"), "Lumber provenance survives the sale.");
            Assert.IsTrue(provenance.Contains("mill-1"));
            Assert.IsTrue(provenance.Contains("smith-1"), "Hardware provenance survives the sale.");
        }

        [Test]
        public void ConstructionSale_RefusesLoudly_OnLumberShortfall_ConsumesNothing()
        {
            var yard = StockedYard();
            var diag = new List<string>();

            var record = yard.TrySellToConstructionProject("project-house-3", 500, 95, 5, 35, 240, diag);

            Assert.IsNull(record, "Shortfall is refused loudly — never partial, never invented.");
            Assert.AreEqual(70, yard.LumberStock.TotalLumberUnits, "Refused sales consume nothing.");
            Assert.AreEqual(30, yard.HardwareStock.TotalHardwareUnits, "The covered bucket is untouched too — atomic.");
            Assert.AreEqual(0, yard.SalesHistory.Count);
            Assert.IsTrue(string.Join(" ", diag).Contains("REFUSED"));
        }

        [Test]
        public void ConstructionSale_RefusesLoudly_OnNailsShortfall_ConsumesNothing()
        {
            var yard = StockedYard();
            var diag = new List<string>();

            var record = yard.TrySellToConstructionProject("project-house-3", 10, 95, 500, 35, 240, diag);

            Assert.IsNull(record);
            Assert.AreEqual(70, yard.LumberStock.TotalLumberUnits, "Atomic refusal — the covered lumber bucket is untouched.");
            Assert.AreEqual(30, yard.HardwareStock.TotalHardwareUnits);
            Assert.AreEqual(0, yard.SalesHistory.Count);
        }

        [Test]
        public void ConstructionSale_LumberOnly_WhenNailsZero()
        {
            var yard = StockedYard();
            var diag = new List<string>();

            var record = yard.TrySellToConstructionProject("project-repair-1", 20, 95, 0, 0, 240, diag);

            Assert.NotNull(record);
            Assert.AreEqual(20, record.UnitsSoldFor(ConstructionResourceKind.Lumber));
            Assert.AreEqual(0, record.UnitsSoldFor(ConstructionResourceKind.Nails));
            Assert.AreEqual(50, yard.LumberStock.TotalLumberUnits);
            Assert.AreEqual(30, yard.HardwareStock.TotalHardwareUnits, "Zero-nail orders leave hardware alone.");
        }

        [Test]
        public void ConstructionSale_Refuses_AnonymousProject()
        {
            var yard = StockedYard();
            var diag = new List<string>();

            Assert.IsNull(yard.TrySellToConstructionProject(string.Empty, 10, 95, 5, 35, 240, diag));
            Assert.AreEqual(70, yard.LumberStock.TotalLumberUnits);
        }

        // ---- Town retail ----

        [Test]
        public void RetailSale_SellsLumber_WithFilters_AndRefusesShortfall()
        {
            var yard = StockedYard();
            var diag = new List<string>();

            var record = yard.TrySellLumberRetail("wheelwright-2", 10, null, "clear", 110, 240, diag);

            Assert.NotNull(record);
            Assert.IsFalse(record.IsConstructionSale);
            Assert.AreEqual("wheelwright-2", record.BuyerLabel);
            Assert.AreEqual(10, record.UnitsSoldFor(ConstructionResourceKind.Lumber));
            Assert.AreEqual(60, yard.LumberStock.TotalLumberUnits);

            // Only clear stock left is 20 units (30 clear - 10); ask 25 clear.
            Assert.IsNull(yard.TrySellLumberRetail("wheelwright-2", 25, null, "clear", 110, 240, diag),
                "Filtered shortfall refuses even when unfiltered stock would cover it.");
            Assert.AreEqual(60, yard.LumberStock.TotalLumberUnits);
        }

        // ---- Save / load ----

        [Test]
        public void SaveLoad_RoundTrip_PreservesEverything()
        {
            var yard = StockedYard();
            var diag = new List<string>();
            yard.TrySellToConstructionProject("project-house-3", 50, 95, 15, 35, 240, diag);

            var dto = yard.CaptureSaveDto();

            var restored = new LumberYardShopRuntime("yard-1", new EntityIdRegistry());
            restored.LoadFromSaveDto(dto);

            Assert.AreEqual(20, restored.LumberStock.TotalLumberUnits);
            Assert.AreEqual(15, restored.HardwareStock.TotalHardwareUnits);
            Assert.AreEqual(1, restored.SalesHistory.Count);
            var sale = restored.SalesHistory[0];
            Assert.AreEqual("project-house-3", sale.BuyerLabel);
            Assert.AreEqual(50 * 95 + 15 * 35, sale.TotalCents);
            Assert.AreEqual(50, sale.UnitsSoldFor(ConstructionResourceKind.Lumber));
            Assert.AreEqual(15, sale.UnitsSoldFor(ConstructionResourceKind.Nails));
            Assert.IsTrue(string.Join(" | ", sale.AllProvenanceLines()).Contains("stand-1"),
                "Provenance survives the save round-trip.");
            var lot = restored.LumberStock.Lots[0];
            Assert.AreEqual("clear", lot.YardGradeId, "Yard grades survive the round-trip.");
            Assert.AreEqual("mill-2", lot.SourceMillBusinessId);
            Assert.AreEqual("stand-2", lot.StandId);
            var hw = restored.HardwareStock.Lots[0];
            Assert.AreEqual("smith-1", hw.SourceBlacksmithBusinessId);
        }

        [Test]
        public void SaveLoad_Preserves_OpeningStockFlag()
        {
            var yard = new LumberYardShopRuntime("yard-1", new EntityIdRegistry());
            var diag = new List<string>();
            Assert.IsNull(LumberYardOpeningStock.ApplyOneTime(yard, 60, "white pine", 30, 0, 200, diag));
            Assert.IsTrue(yard.OpeningStockApplied);

            var restored = new LumberYardShopRuntime("yard-1", new EntityIdRegistry());
            restored.LoadFromSaveDto(yard.CaptureSaveDto());

            Assert.IsTrue(restored.OpeningStockApplied, "The once-guard survives save/load.");
            Assert.NotNull(LumberYardOpeningStock.ApplyOneTime(restored, 60, "white pine", 30, 0, 200, diag),
                "A restored yard still refuses a second endowment.");
        }
    }
}
