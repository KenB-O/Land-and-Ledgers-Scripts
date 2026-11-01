using System.Collections.Generic;
using LandLedgers.Economy.Farming.Crops;
using LandLedgers.Economy.Trade;
using LandLedgers.Primitives;
using NUnit.Framework;

namespace LandLedgers.EditorTests.Economy.Farming
{
    /// <summary>
    /// D2G: seed quality as data — declared grades and germination rates ride
    /// the lot from merchant to farm, the farm's observed germination is
    /// recorded honestly, bad-seed incidents are filed/corroborated/dismissed/
    /// resolved as a ledger (never auto-applied to reputation), and variety
    /// trial results are recorded data that never touch yield calibration.
    /// </summary>
    [TestFixture]
    public sealed class SeedQualityTests
    {
        private static SeedMerchant StockedMerchant(EntityIdRegistry ids, List<string> diagnostics)
        {
            var merchant = new SeedMerchant("store-1", "General Store");
            merchant.SetPrice(CropKind.Wheat, 60);
            var importLot = new ImportLot
            {
                LotId = "IMP-ORD-0001-1",
                MaterialId = SeedMerchantSupply.SeedMaterialIdFor(CropKind.Wheat),
                MaterialName = "Seed wheat (Red Fife)",
                Units = 200,
                OriginName = SeedMerchantSupply.DefaultSeedHouseOrigin,
                OrderId = "IMP-ORD-0001",
                ArrivalDayIndex = 300,
                Imported = true,
            };
            SeedLot stocked = merchant.RestockFromImport(ids, importLot, CropKind.Wheat, "red-fife", 300, diagnostics);
            Assert.NotNull(stocked);
            return merchant;
        }

        private static FarmSeedStore StoreWithSeed(EntityIdRegistry ids, List<string> diagnostics)
        {
            var merchant = StockedMerchant(ids, diagnostics);
            var store = new FarmSeedStore("farm-1");
            SeedPurchase purchase = store.PurchaseSeed(merchant, CropKind.Wheat, "red-fife", 100, 305, ids, diagnostics);
            Assert.NotNull(purchase);
            return store;
        }

        [Test]
        public void SeedLot_QualityDefaultsToUndeclared()
        {
            var lot = new SeedLot();
            Assert.AreEqual(SeedQualityGrade.Unstated, lot.QualityGrade);
            Assert.AreEqual(SeedQuality.UndeclaredGerminationRate, lot.DeclaredGerminationRatePct);
            Assert.AreEqual(SeedQuality.UndeclaredGerminationRate, lot.ReportedGerminationRatePct);
            Assert.AreEqual(string.Empty, lot.QualityDeclarationNote);
        }

        [Test]
        public void SeedQuality_NormalizeGerminationRate_KeepsSentinelAndClamps()
        {
            Assert.AreEqual(-1, SeedQuality.NormalizeGerminationRate(-1), "The undeclared sentinel survives normalization.");
            Assert.AreEqual(100, SeedQuality.NormalizeGerminationRate(150));
            Assert.AreEqual(0, SeedQuality.NormalizeGerminationRate(-5));
            Assert.AreEqual(95, SeedQuality.NormalizeGerminationRate(95));
        }

        [Test]
        public void SeedMerchant_SetLotQualityDeclaration_StoresClaim()
        {
            var ids = new EntityIdRegistry();
            var diagnostics = new List<string>();
            SeedMerchant merchant = StockedMerchant(ids, diagnostics);
            EntityId lotId = merchant.Lots[0].LotId;

            string problem = merchant.SetLotQualityDeclaration(
                lotId, SeedQualityGrade.Cleaned, 120, "catalogue warranted 95%", diagnostics);

            Assert.IsNull(problem);
            Assert.AreEqual(SeedQualityGrade.Cleaned, merchant.Lots[0].QualityGrade);
            Assert.AreEqual(100, merchant.Lots[0].DeclaredGerminationRatePct, "Claimed rates clamp to 0-100.");
            Assert.AreEqual("catalogue warranted 95%", merchant.Lots[0].QualityDeclarationNote);
        }

        [Test]
        public void SeedMerchant_SetLotQualityDeclaration_UnknownLotRefused()
        {
            var ids = new EntityIdRegistry();
            var diagnostics = new List<string>();
            SeedMerchant merchant = StockedMerchant(ids, diagnostics);

            string problem = merchant.SetLotQualityDeclaration(
                ids.Allocate(EntityKind.Lot), SeedQualityGrade.Cleaned, 95, "note", diagnostics);

            Assert.IsNotNull(problem, "Declarations attach to real lots only.");
        }

        [Test]
        public void SellSeedLots_PropagatesDeclarationToFarmLot()
        {
            var ids = new EntityIdRegistry();
            var diagnostics = new List<string>();
            SeedMerchant stockedSource = StockedMerchant(ids, diagnostics);
            SeedLot stocked = stockedSource.Lots[0];
            Assert.IsNull(stockedSource.SetLotQualityDeclaration(
                stocked.LotId, SeedQualityGrade.Cleaned, 95, "seed house guarantee", diagnostics));

            List<SeedLot> sold = stockedSource.SellSeedLots(CropKind.Wheat, "red-fife", 100, 305, ids, diagnostics);

            Assert.NotNull(sold);
            Assert.AreEqual(1, sold.Count);
            Assert.AreEqual(SeedQualityGrade.Cleaned, sold[0].QualityGrade);
            Assert.AreEqual(95, sold[0].DeclaredGerminationRatePct);
            Assert.AreEqual("seed house guarantee", sold[0].QualityDeclarationNote);
            Assert.IsTrue(sold[0].Provenance.HasHops, "Provenance still travels with the declaration.");
        }

        [Test]
        public void FarmSeedStore_ReportSeedQuality_StampsObservedRate()
        {
            var ids = new EntityIdRegistry();
            var diagnostics = new List<string>();
            FarmSeedStore store = StoreWithSeed(ids, diagnostics);
            EntityId lotId = store.Lots[0].LotId;

            string problem = store.ReportSeedQuality(lotId, 62, "thin stand in the north forty", diagnostics);

            Assert.IsNull(problem);
            Assert.AreEqual(62, store.Lots[0].ReportedGerminationRatePct);
        }

        [Test]
        public void FarmSeedStore_ReportSeedQuality_InvalidRateOrUnknownLotRefused()
        {
            var ids = new EntityIdRegistry();
            var diagnostics = new List<string>();
            FarmSeedStore store = StoreWithSeed(ids, diagnostics);
            EntityId lotId = store.Lots[0].LotId;

            Assert.IsNotNull(store.ReportSeedQuality(lotId, 101, "note", diagnostics),
                "A quality report needs an observed rate 0-100.");
            Assert.AreEqual(SeedQuality.UndeclaredGerminationRate, store.Lots[0].ReportedGerminationRatePct,
                "Refused reports stamp nothing.");

            Assert.IsNotNull(store.ReportSeedQuality(ids.Allocate(EntityKind.Lot), 50, "note", diagnostics),
                "Reports attach to real lots only.");
        }

        [Test]
        public void IncidentBook_FileIncident_RequiresMerchantLotAndRate()
        {
            var ids = new EntityIdRegistry();
            var diagnostics = new List<string>();
            var book = new SeedQualityIncidentBook();
            EntityId lotId = ids.Allocate(EntityKind.Lot);

            Assert.IsNull(book.FileIncident(null, "store-1", "General Store", lotId, CropKind.Wheat, "red-fife",
                SeedQualityGrade.Cleaned, 95, 40, "farm-1", 320, "thin stand", diagnostics),
                "Filing needs an id registry.");
            Assert.IsNull(book.FileIncident(ids, "", "", lotId, CropKind.Wheat, "red-fife",
                SeedQualityGrade.Cleaned, 95, 40, "farm-1", 320, "thin stand", diagnostics),
                "Anonymous reports are refused — no vague blame.");
            Assert.IsNull(book.FileIncident(ids, "store-1", "General Store", EntityId.Invalid, CropKind.Wheat, "red-fife",
                SeedQualityGrade.Cleaned, 95, 40, "farm-1", 320, "thin stand", diagnostics),
                "Reports attach to lots, not vibes.");
            Assert.IsNull(book.FileIncident(ids, "store-1", "General Store", lotId, CropKind.Wheat, "red-fife",
                SeedQualityGrade.Cleaned, 95, -1, "farm-1", 320, "thin stand", diagnostics),
                "A report without an observed rate is a rumor, not a record.");

            SeedQualityIncident incident = book.FileIncident(ids, "store-1", "General Store", lotId, CropKind.Wheat,
                "red-fife", SeedQualityGrade.Cleaned, 95, 40, "farm-1", 320, "thin stand", diagnostics);

            Assert.NotNull(incident);
            Assert.AreEqual(SeedQualityIncidentStatus.Filed, incident.Status);
            Assert.AreEqual(40, incident.ReportedGerminationRatePct);
            Assert.AreEqual(95, incident.DeclaredGerminationRatePct);
            Assert.AreEqual(1, book.Incidents.Count);
        }

        [Test]
        public void IncidentBook_StatusTransitions_Enforced()
        {
            var ids = new EntityIdRegistry();
            var diagnostics = new List<string>();
            var book = new SeedQualityIncidentBook();
            EntityId lotId = ids.Allocate(EntityKind.Lot);
            SeedQualityIncident incident = book.FileIncident(ids, "store-1", "General Store", lotId, CropKind.Wheat,
                "red-fife", SeedQualityGrade.Cleaned, 95, 40, "farm-1", 320, "thin stand", diagnostics);
            Assert.NotNull(incident);

            Assert.IsNotNull(book.ResolveIncident(incident.IncidentId, 321, "made it right", diagnostics),
                "Only a corroborated incident can be resolved.");
            Assert.IsNull(book.CorroborateIncident(incident.IncidentId, 321, "second farm, same lot", diagnostics));
            Assert.AreEqual(SeedQualityIncidentStatus.Corroborated, incident.Status);
            Assert.IsNotNull(book.CorroborateIncident(incident.IncidentId, 322, "again", diagnostics),
                "Corroboration is a one-way gate.");
            Assert.IsNull(book.ResolveIncident(incident.IncidentId, 323, "merchant replaced the seed", diagnostics));
            Assert.AreEqual(SeedQualityIncidentStatus.Resolved, incident.Status);
            Assert.IsNotNull(book.DismissIncident(incident.IncidentId, 324, "late", diagnostics),
                "Terminal states do not move.");
            Assert.IsNotNull(book.CorroborateIncident(ids.Allocate(EntityKind.Lot), 325, "x", diagnostics),
                "Unknown incidents are refused.");
        }

        [Test]
        public void IncidentBook_DismissFiled_ClosesRecord()
        {
            var ids = new EntityIdRegistry();
            var diagnostics = new List<string>();
            var book = new SeedQualityIncidentBook();
            EntityId lotId = ids.Allocate(EntityKind.Lot);
            SeedQualityIncident incident = book.FileIncident(ids, "store-1", "General Store", lotId, CropKind.Wheat,
                "red-fife", SeedQualityGrade.Unstated, -1, 55, "farm-1", 320, "maybe old seed", diagnostics);
            Assert.NotNull(incident);

            Assert.IsNull(book.DismissIncident(incident.IncidentId, 321, "buyer's own storage fault", diagnostics));
            Assert.AreEqual(SeedQualityIncidentStatus.Dismissed, incident.Status);
            Assert.IsFalse(incident.IsOpen);
        }

        [Test]
        public void IncidentBook_BadSeedPressure_RisesWithCorroboratedIncidents()
        {
            var ids = new EntityIdRegistry();
            var diagnostics = new List<string>();
            var book = new SeedQualityIncidentBook();

            Assert.AreEqual(0f, book.BadSeedPressure01("store-1", "General Store"), "No record, no pressure.");

            SeedQualityIncident filed = book.FileIncident(ids, "store-1", "General Store", ids.Allocate(EntityKind.Lot),
                CropKind.Wheat, "red-fife", SeedQualityGrade.Cleaned, 95, 40, "farm-1", 320, "thin", diagnostics);
            float afterFiled = book.BadSeedPressure01("store-1", "General Store");
            Assert.Greater(afterFiled, 0f, "An open filed report registers lightly.");

            Assert.NotNull(filed);
            Assert.IsNull(book.CorroborateIncident(filed.IncidentId, 321, "second report", diagnostics));
            float afterCorroborated = book.BadSeedPressure01("store-1", "General Store");
            Assert.Greater(afterCorroborated, afterFiled, "Corroboration weighs more than a lone filing.");
            Assert.LessOrEqual(afterCorroborated, 1f);

            Assert.IsNull(book.DismissIncident(filed.IncidentId, 322, "settled", diagnostics));
            Assert.AreEqual(0f, book.BadSeedPressure01("store-1", "General Store"),
                "Dismissed incidents weigh nothing — the record forgives.");

            Assert.AreEqual(0f, book.BadSeedPressure01("store-2", "Other Store"),
                "Pressure is per merchant, never smeared across the trade.");
        }

        [Test]
        public void IncidentBook_BuildQualitySignal_SummarizesMerchant()
        {
            var ids = new EntityIdRegistry();
            var diagnostics = new List<string>();
            var book = new SeedQualityIncidentBook();
            SeedQualityIncident a = book.FileIncident(ids, "store-1", "General Store", ids.Allocate(EntityKind.Lot),
                CropKind.Wheat, "red-fife", SeedQualityGrade.Cleaned, 95, 40, "farm-1", 320, "thin", diagnostics);
            SeedQualityIncident b = book.FileIncident(ids, "store-1", "General Store", ids.Allocate(EntityKind.Lot),
                CropKind.Oats, "scotch-oats", SeedQualityGrade.AsThreshed, 80, 70, "farm-2", 321, "weedy", diagnostics);
            Assert.NotNull(a);
            Assert.NotNull(b);
            Assert.IsNull(book.CorroborateIncident(a.IncidentId, 322, "second report", diagnostics));

            SeedMerchantQualitySignal signal = book.BuildQualitySignal("store-1", "General Store");

            Assert.AreEqual("store-1", signal.MerchantBusinessId);
            Assert.AreEqual(2, signal.TotalIncidents);
            Assert.AreEqual(1, signal.FiledCount);
            Assert.AreEqual(1, signal.CorroboratedCount);
            Assert.AreEqual(0, signal.DismissedCount);
            Assert.AreEqual(0, signal.ResolvedCount);
            Assert.AreEqual(book.BadSeedPressure01("store-1", "General Store"), signal.BadSeedPressure01);
        }

        [Test]
        public void IncidentBook_SaveRoundTrip_PreservesIncidents()
        {
            var ids = new EntityIdRegistry();
            var diagnostics = new List<string>();
            var book = new SeedQualityIncidentBook();
            SeedQualityIncident incident = book.FileIncident(ids, "store-1", "General Store", ids.Allocate(EntityKind.Lot),
                CropKind.Wheat, "red-fife", SeedQualityGrade.Cleaned, 95, 40, "farm-1", 320, "thin", diagnostics);
            Assert.NotNull(incident);
            Assert.IsNull(book.CorroborateIncident(incident.IncidentId, 321, "second report", diagnostics));

            SeedQualityIncidentBook.SeedQualityIncidentBookSaveDto dto = book.CaptureSaveDto();
            var restored = new SeedQualityIncidentBook();
            restored.LoadFromSaveDto(dto);

            Assert.AreEqual(1, restored.Incidents.Count);
            SeedQualityIncident back = restored.Incidents[0];
            Assert.AreEqual(SeedQualityIncidentStatus.Corroborated, back.Status);
            Assert.AreEqual("store-1", back.MerchantBusinessId);
            Assert.AreEqual(40, back.ReportedGerminationRatePct);
            Assert.AreEqual(95, back.DeclaredGerminationRatePct);
        }

        [Test]
        public void TrialBook_RedFifeResearchDatum_Present()
        {
            var ids = new EntityIdRegistry();
            var diagnostics = new List<string>();

            VarietyTrialBook book = VarietyTrialBook.CreateWithResearchDatum(ids, 300, diagnostics);

            Assert.AreEqual(1, book.Records.Count);
            VarietyTrialRecord datum = book.Records[0];
            Assert.AreEqual("red-fife", datum.VarietyId);
            Assert.IsTrue(datum.LocationName.Contains("Otonabee"), "The datum names a real place.");
            Assert.AreEqual(VarietyTrialOutcome.Thrived, datum.Outcome);
            Assert.AreEqual(1, book.TrialsForVariety("red-fife").Count);
            Assert.AreEqual(0, book.TrialsForVariety("scotch-oats").Count, "Unrecorded performance is an honest gap.");
        }

        [Test]
        public void TrialBook_RecordTrial_ValidatesVarietyLocationOutcome()
        {
            var ids = new EntityIdRegistry();
            var diagnostics = new List<string>();
            var book = new VarietyTrialBook();

            Assert.IsNull(book.RecordTrial(ids, "invented-variety", "Somewhere", "1870s",
                VarietyTrialOutcome.Adequate, "note", 300, diagnostics),
                "Trials attach to catalogued varieties, never invented ones.");
            Assert.IsNull(book.RecordTrial(ids, "scotch-oats", "", "1870s",
                VarietyTrialOutcome.Adequate, "note", 300, diagnostics),
                "A trial must name where it was observed.");
            Assert.IsNull(book.RecordTrial(ids, "scotch-oats", "Lanark County", "1870s",
                VarietyTrialOutcome.Unrecorded, "note", 300, diagnostics),
                "A trial must state its recorded outcome.");
            Assert.AreEqual(0, book.Records.Count, "Refused trials record nothing.");

            VarietyTrialRecord record = book.RecordTrial(ids, "scotch-oats", "Lanark County", "1870s",
                VarietyTrialOutcome.Poor, "wet springs rotted the stand two years running", 300, diagnostics);

            Assert.NotNull(record);
            Assert.AreEqual(VarietyTrialOutcome.Poor, record.Outcome);
            Assert.AreEqual(1, book.TrialsForVariety("scotch-oats").Count);
        }

        [Test]
        public void TrialBook_RecordingTrialsLeavesYieldCalibrationUntouched()
        {
            var ids = new EntityIdRegistry();
            var diagnostics = new List<string>();
            var book = new VarietyTrialBook();
            var calibration = new CropCalibration(CropKind.Wheat, 20, 2, 90, 21);
            int yieldBefore = calibration.YieldUnitsPerAcre;
            int seedRateBefore = calibration.SeedUnitsPerAcre;

            book.RecordTrial(ids, "red-fife", "Otonabee", "1842-1900",
                VarietyTrialOutcome.Thrived, "dominance record", 300, diagnostics);
            book.RecordTrial(ids, "scotch-oats", "Lanark County", "1870s",
                VarietyTrialOutcome.Poor, "wet springs", 300, diagnostics);
            book.RecordTrial(ids, "yellow-dent", "Kent County", "1870s",
                VarietyTrialOutcome.Mixed, "good years and bad", 300, diagnostics);

            Assert.AreEqual(yieldBefore, calibration.YieldUnitsPerAcre,
                "Trial outcomes never feed yield calibration — data only, by design.");
            Assert.AreEqual(seedRateBefore, calibration.SeedUnitsPerAcre);
        }

        [Test]
        public void TrialBook_SaveRoundTrip_PreservesRecords()
        {
            var ids = new EntityIdRegistry();
            var diagnostics = new List<string>();
            VarietyTrialBook book = VarietyTrialBook.CreateWithResearchDatum(ids, 300, diagnostics);

            VarietyTrialBook.VarietyTrialBookSaveDto dto = book.CaptureSaveDto();
            var restored = new VarietyTrialBook();
            restored.LoadFromSaveDto(dto);

            Assert.AreEqual(1, restored.Records.Count);
            Assert.AreEqual("red-fife", restored.Records[0].VarietyId);
            Assert.AreEqual(VarietyTrialOutcome.Thrived, restored.Records[0].Outcome);
        }
    }
}
