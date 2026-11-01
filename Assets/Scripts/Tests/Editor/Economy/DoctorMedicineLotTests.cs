using EntityId = LandLedgers.Primitives.EntityId;

using System.Collections.Generic;
using LandLedgers.Economy.Businesses.Doctor;
using LandLedgers.Economy.Trade;
using LandLedgers.Primitives;
using NUnit.Framework;

namespace LandLedgers.Editor.Economy
{
    /// <summary>
    /// W1A: medicine lots carry full upstream provenance; doses are never conjured.
    /// </summary>
    [TestFixture]
    public sealed class DoctorMedicineLotTests
    {
        private static DoctorMedicineLot ImportedLot(EntityIdRegistry registry, string name, int doses, int day)
        {
            return new DoctorMedicineLot
            {
                LotId = registry.Allocate(EntityKind.Lot),
                MedicineName = name,
                Doses = doses,
                AcquiredDayIndex = day,
                ImportOrderId = "IMP-ORD-0001",
                OriginName = "Off-map wholesale drug house, via railhead",
            };
        }

        [Test]
        public void BootstrapEndowment_IsExplicitlyMarked_OneTime()
        {
            var stock = new DoctorMedicineStock();
            var registry = new EntityIdRegistry();
            var diag = new List<string>();

            DoctorMedicineBootstrap.ApplyBootstrapEndowment(stock, registry, 100, diag);

            Assert.AreEqual(2, stock.Lots.Count, "Opening chest: remedies + dressings.");
            foreach (var lot in stock.Lots)
            {
                Assert.IsTrue(lot.IsBootstrapEndowment, "Bootstrap lots must be explicitly marked.");
                StringAssert.Contains("BOOTSTRAP", lot.ProvenanceChain(),
                    "Bootstrap provenance chain must say BOOTSTRAP.");
            }

            Assert.AreEqual(DoctorMedicineBootstrap.BootstrapRemedyDoses,
                stock.DosesOnHand(DoctorTreatmentCatalog.MedicineDoseItemId));
            Assert.AreEqual(DoctorMedicineBootstrap.BootstrapDressingUnits,
                stock.DosesOnHand(DoctorTreatmentCatalog.DressingItemId));
        }

        [Test]
        public void ReceiveLot_RefusesOrphanAnonymousAndDuplicateLots()
        {
            var stock = new DoctorMedicineStock();
            var registry = new EntityIdRegistry();
            var diag = new List<string>();

            var orphan = new DoctorMedicineLot
            {
                LotId = registry.Allocate(EntityKind.Lot),
                MedicineName = DoctorTreatmentCatalog.MedicineDoseItemId,
                Doses = 10,
                AcquiredDayIndex = 100,
                // no import order, no origin, not bootstrap
            };
            Assert.NotNull(stock.ReceiveLot(orphan, diag), "Orphan lots must be refused.");

            var nameless = new DoctorMedicineLot
            {
                LotId = registry.Allocate(EntityKind.Lot),
                Doses = 10,
                AcquiredDayIndex = 100,
                ImportOrderId = "IMP-ORD-0001",
                OriginName = "Off-map wholesale drug house, via railhead",
            };
            Assert.NotNull(stock.ReceiveLot(nameless, diag), "Nameless lots must be refused.");

            var good = ImportedLot(registry, DoctorTreatmentCatalog.MedicineDoseItemId, 10, 100);
            Assert.Null(stock.ReceiveLot(good, diag), "A sourced lot must be accepted.");
            Assert.NotNull(stock.ReceiveLot(good, diag), "The same lot id twice must be refused.");
        }

        [Test]
        public void TryDispenseDoses_FifoWithProvenanceLines()
        {
            var stock = new DoctorMedicineStock();
            var registry = new EntityIdRegistry();
            var diag = new List<string>();

            stock.ReceiveLot(ImportedLot(registry, DoctorTreatmentCatalog.MedicineDoseItemId, 3, 110), diag);
            stock.ReceiveLot(ImportedLot(registry, DoctorTreatmentCatalog.MedicineDoseItemId, 5, 100), diag);

            var lines = stock.TryDispenseDoses(DoctorTreatmentCatalog.MedicineDoseItemId, 6, 120, diag);

            Assert.NotNull(lines, "A covered dispense must succeed.");
            Assert.AreEqual(2, lines.Count, "FIFO across two lots.");
            Assert.AreEqual(5, lines[0].DosesTaken, "Oldest lot (day 100) first.");
            Assert.AreEqual(1, lines[1].DosesTaken);
            foreach (var line in lines)
            {
                StringAssert.Contains("IMP-ORD-0001", line.ProvenanceChain,
                    "Every dispense line carries its upstream chain.");
                StringAssert.Contains("wholesale drug house", line.ProvenanceChain);
            }

            Assert.AreEqual(2, stock.DosesOnHand(DoctorTreatmentCatalog.MedicineDoseItemId),
                "3 - 1 used from the newer lot.");
            Assert.AreEqual(1, stock.Lots.Count, "The emptied lot drops out — no ghost stock.");
        }

        [Test]
        public void TryDispenseDoses_ShortfallRefusesLoudly_ConjuresNothing()
        {
            var stock = new DoctorMedicineStock();
            var registry = new EntityIdRegistry();
            var diag = new List<string>();

            stock.ReceiveLot(ImportedLot(registry, DoctorTreatmentCatalog.MedicineDoseItemId, 2, 100), diag);

            var lines = stock.TryDispenseDoses(DoctorTreatmentCatalog.MedicineDoseItemId, 5, 120, diag);

            Assert.Null(lines, "Shortfall must return null — no doses conjured.");
            Assert.AreEqual(2, stock.DosesOnHand(DoctorTreatmentCatalog.MedicineDoseItemId),
                "Failed dispense must not touch the cabinet.");
            Assert.IsTrue(diag.Count > 0, "The refusal must be loud.");
        }

        [Test]
        public void EnsureMedicineImportable_DeclaresNamedOffMapLink()
        {
            var diag = new List<string>();
            DoctorMedicineSupply.EnsureMedicineImportable(diag);

            ImportMaterial material = ImportCatalog.Get(DoctorMedicineSupply.MedicineStockMaterialId);
            Assert.NotNull(material, "Medicine must become an importable material.");
            Assert.IsFalse(string.IsNullOrWhiteSpace(material.OriginName), "EQU-1: no anonymous sources.");
            Assert.Greater(material.TransitDays, 0, "Transit is real days, not instant.");
        }

        [Test]
        public void ReceiveImportArrival_RecordsImportChain()
        {
            var stock = new DoctorMedicineStock();
            var registry = new EntityIdRegistry();
            var diag = new List<string>();

            string rejection = DoctorMedicineSupply.ReceiveImportArrival(
                stock, DoctorTreatmentCatalog.MedicineDoseItemId, 50,
                "IMP-ORD-0042", "Off-map wholesale drug house, via railhead",
                130, registry, diag);

            Assert.Null(rejection, rejection);
            Assert.AreEqual(50, stock.DosesOnHand(DoctorTreatmentCatalog.MedicineDoseItemId));

            var lines = stock.TryDispenseDoses(DoctorTreatmentCatalog.MedicineDoseItemId, 1, 131, diag);
            Assert.NotNull(lines);
            StringAssert.Contains("IMP-ORD-0042", lines[0].ProvenanceChain);
        }

        [Test]
        public void SaveLoad_RoundTripsLotsWithProvenance()
        {
            var stock = new DoctorMedicineStock();
            var registry = new EntityIdRegistry();
            var diag = new List<string>();

            DoctorMedicineBootstrap.ApplyBootstrapEndowment(stock, registry, 100, diag);

            var restored = new DoctorMedicineStock();
            restored.LoadFromSaveDto(stock.CaptureSaveDto());

            Assert.AreEqual(2, restored.Lots.Count);
            Assert.AreEqual(DoctorMedicineBootstrap.BootstrapRemedyDoses,
                restored.DosesOnHand(DoctorTreatmentCatalog.MedicineDoseItemId));
            Assert.IsTrue(restored.Lots[0].IsBootstrapEndowment, "Bootstrap flag must survive save/load.");
        }
    }
}
