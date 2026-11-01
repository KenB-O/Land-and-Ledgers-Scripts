using System.Collections.Generic;
using LandLedgers.Economy;
using LandLedgers.Economy.AnimalServices;
using LandLedgers.Economy.Liabilities;
using LandLedgers.Population;
using LandLedgers.Primitives;
using NUnit.Framework;

namespace LandLedgers.Editor.Economy
{
    /// <summary>
    /// W10A: vet/medicine suppliers — closes the FVS-flagged hole. Medicine lots carry
    /// provenance (import order, merchant purchase, or explicit bootstrap); the chest
    /// dispenses FIFO and refuses shortfalls loudly; treatments stamp dose provenance;
    /// vet invoices settle as real payables.
    /// </summary>
    [TestFixture]
    public sealed class VetMedicineSupplyTests
    {
        private static EntityIdRegistry NewRegistry() => new EntityIdRegistry();

        [Test]
        public void BootstrapEndowment_AppliesOneTimeMarkedLots()
        {
            var stock = new VetMedicineStock();
            var diag = new List<string>();
            VetMedicineBootstrap.ApplyBootstrapEndowment(stock, NewRegistry(), 10, diag);

            Assert.AreEqual(VetMedicineBootstrap.BootstrapRemedyDoses, stock.DosesOnHand("veterinary-remedies"));
            Assert.AreEqual(VetMedicineBootstrap.BootstrapDressingUnits, stock.DosesOnHand("veterinary-dressings"));
            foreach (VetMedicineLot lot in stock.Lots)
            {
                Assert.IsTrue(lot.IsBootstrapEndowment);
                StringAssert.Contains("BOOTSTRAP", lot.ProvenanceChain());
            }
        }

        [Test]
        public void ReceiveLot_RefusesOrphanLot()
        {
            var stock = new VetMedicineStock();
            var diag = new List<string>();
            string rejection = stock.ReceiveLot(new VetMedicineLot
            {
                LotId = NewRegistry().Allocate(EntityKind.Lot),
                MedicineName = "veterinary-remedies",
                Doses = 10,
                AcquiredDayIndex = 5,
            }, diag);

            Assert.IsNotNull(rejection);
            Assert.AreEqual(0, stock.Lots.Count);
        }

        [Test]
        public void ReceiveLot_AcceptsImportLotWithProvenance()
        {
            var stock = new VetMedicineStock();
            var diag = new List<string>();
            string rejection = VetMedicineSupply.ReceiveImportArrival(
                stock, "veterinary-remedies", 25, "IMP-77", "Off-map wholesale drug house, via railhead",
                12, NewRegistry(), diag);

            Assert.IsNull(rejection);
            Assert.AreEqual(25, stock.DosesOnHand("veterinary-remedies"));
            StringAssert.Contains("IMP-77", stock.Lots[0].ProvenanceChain());
            StringAssert.Contains("wholesale drug house", stock.Lots[0].ProvenanceChain());
        }

        [Test]
        public void ReceiveLot_AcceptsMerchantPurchaseWithNamedSupplier()
        {
            var stock = new VetMedicineStock();
            var diag = new List<string>();
            string rejection = VetMedicineSupply.ReceiveMerchantPurchase(
                stock, "veterinary-remedies", 12, "biz-apothecary-1", "Hale's Apothecary",
                14, NewRegistry(), diag);

            Assert.IsNull(rejection);
            StringAssert.Contains("Hale's Apothecary", stock.Lots[0].ProvenanceChain());
            StringAssert.Contains("biz-apothecary-1", stock.Lots[0].ProvenanceChain());
        }

        [Test]
        public void ReceiveLot_RefusesDuplicateLotId()
        {
            var stock = new VetMedicineStock();
            var registry = NewRegistry();
            var diag = new List<string>();
            EntityId lotId = registry.Allocate(EntityKind.Lot);
            Assert.IsNull(VetMedicineSupply.ReceiveImportArrival(
                stock, "veterinary-remedies", 10, "IMP-1", "origin", 1, registry, diag));

            // Forge a second lot reusing the same id.
            string rejection = stock.ReceiveLot(new VetMedicineLot
            {
                LotId = lotId,
                MedicineName = "veterinary-remedies",
                Doses = 5,
                AcquiredDayIndex = 2,
                ImportOrderId = "IMP-2",
                OriginName = "origin",
            }, diag);
            Assert.IsNotNull(rejection);
            Assert.AreEqual(1, stock.Lots.Count);
        }

        [Test]
        public void TryDispenseDoses_FifoOldestFirst()
        {
            var stock = new VetMedicineStock();
            var registry = NewRegistry();
            var diag = new List<string>();
            Assert.IsNull(VetMedicineSupply.ReceiveImportArrival(
                stock, "veterinary-remedies", 10, "IMP-NEW", "origin", 20, registry, diag));
            Assert.IsNull(VetMedicineSupply.ReceiveImportArrival(
                stock, "veterinary-remedies", 10, "IMP-OLD", "origin", 5, registry, diag));

            List<VetMedicineDispenseLine> lines = stock.TryDispenseDoses("veterinary-remedies", 12, 21, diag);

            Assert.IsNotNull(lines);
            Assert.AreEqual(2, lines.Count);
            Assert.AreEqual(10, lines[0].DosesTaken); // oldest lot first
            StringAssert.Contains("IMP-OLD", lines[0].ProvenanceChain);
            Assert.AreEqual(2, lines[1].DosesTaken);
            Assert.AreEqual(8, stock.DosesOnHand("veterinary-remedies"));
        }

        [Test]
        public void TryDispenseDoses_ShortfallIsLoudAndConjuresNothing()
        {
            var stock = new VetMedicineStock();
            var diag = new List<string>();
            Assert.IsNull(VetMedicineSupply.ReceiveImportArrival(
                stock, "veterinary-remedies", 3, "IMP-1", "origin", 1, NewRegistry(), diag));

            List<VetMedicineDispenseLine> lines = stock.TryDispenseDoses("veterinary-remedies", 10, 2, diag);

            Assert.IsNull(lines);
            Assert.AreEqual(3, stock.DosesOnHand("veterinary-remedies")); // untouched
            CollectionAssert.IsNotEmpty(diag);
        }

        [Test]
        public void DispenseForTreatment_StampsDoseProvenanceOnTreatment()
        {
            var stock = new VetMedicineStock();
            var diag = new List<string>();
            VetMedicineBootstrap.ApplyBootstrapEndowment(stock, NewRegistry(), 1, diag);

            var treatment = new AnimalTreatment
            {
                AnimalId = EntityId.For(EntityKind.Animal, 7),
                Ailment = "garget",
                Treatment = "purgative drench",
            };
            string problem = VetMedicineDispensing.DispenseForTreatment(
                stock, treatment, "veterinary-remedies", 4, 3, diag);

            Assert.IsNull(problem);
            StringAssert.Contains("BOOTSTRAP", treatment.MedicineSource);
            Assert.AreEqual(VetMedicineBootstrap.BootstrapRemedyDoses - 4, stock.DosesOnHand("veterinary-remedies"));
        }

        [Test]
        public void DispenseForTreatment_ShortfallLeavesTreatmentUnmedicated()
        {
            var stock = new VetMedicineStock(); // empty chest
            var diag = new List<string>();
            var treatment = new AnimalTreatment { AnimalId = EntityId.For(EntityKind.Animal, 7) };

            string problem = VetMedicineDispensing.DispenseForTreatment(
                stock, treatment, "veterinary-remedies", 4, 3, diag);

            Assert.IsNotNull(problem);
            StringAssert.Contains("unmedicated", treatment.MedicineSource);
        }

        [Test]
        public void RecordInvoicePayable_BooksRealPayableOnFarm()
        {
            var liabilities = new BusinessLiabilityLedger();
            var registry = NewRegistry();
            var diag = new List<string>();
            var invoice = new VetInvoice
            {
                DayIndex = 9,
                PractitionerId = "vet-1",
                PractitionerName = "Dr. Hart",
                FarmOrBusinessId = "farm-1",
                CallOutFeeCents = 150,
                TravelFeeCents = 60,
                TravelMiles = 12f,
                Treatments = new List<AnimalTreatment>
                {
                    new AnimalTreatment { WorkCostCents = 100, MedicineCostCents = 40 },
                },
            };

            BusinessLiability liability = VetInvoiceSettlement.RecordInvoicePayable(
                invoice, liabilities, registry, 9, diag);

            Assert.IsNotNull(liability);
            Assert.AreEqual(350, liability.BalanceCents); // 150 + 60 + 140
            Assert.AreEqual("farm-1", liability.BusinessInstanceId);
            Assert.AreEqual("Dr. Hart", liability.Counterparty);
            Assert.AreEqual(1, liabilities.All.Count);
        }

        [Test]
        public void RecordInvoicePayable_ZeroInvoiceRefused()
        {
            var liabilities = new BusinessLiabilityLedger();
            var diag = new List<string>();
            var invoice = new VetInvoice { PractitionerId = "vet-1", FarmOrBusinessId = "farm-1" };

            BusinessLiability liability = VetInvoiceSettlement.RecordInvoicePayable(
                invoice, liabilities, NewRegistry(), 9, diag);

            Assert.IsNull(liability);
            Assert.AreEqual(0, liabilities.All.Count);
        }

        [Test]
        public void PayInvoice_SettlesPayableAndRecordsOutflow()
        {
            var liabilities = new BusinessLiabilityLedger();
            var registry = NewRegistry();
            var diag = new List<string>();
            var payer = new HouseholdLedger(42);
            var invoice = new VetInvoice
            {
                DayIndex = 9,
                PractitionerId = "vet-1",
                PractitionerName = "Dr. Hart",
                FarmOrBusinessId = "farm-1",
                CallOutFeeCents = 150,
            };

            BusinessLiability liability = VetInvoiceSettlement.RecordInvoicePayable(
                invoice, liabilities, registry, 9, diag);
            string problem = VetInvoiceSettlement.PayInvoice(liability, liabilities, payer, 10, diag);

            Assert.IsNull(problem);
            Assert.IsTrue(liability.Settled);
            Assert.AreEqual(0, liability.BalanceCents);
        }

        [Test]
        public void VetOccupation_CanonicalRoleNamesAndCompensationAuthority()
        {
            EmploymentRelationship vet = VetOccupation.CreateEmployment(
                12, "biz-vet-practice", VetOccupation.RoleVeterinarian,
                EmploymentKind.Permanent, 2100, 3, "traveling practitioner");
            EmploymentRelationship farrier = VetOccupation.CreateEmployment(
                13, "farm-1", VetOccupation.RoleFarrier,
                EmploymentKind.SeasonalOrCasual, 900, 3, null);

            Assert.AreEqual("Veterinarian", vet.RoleDisplayName);
            Assert.AreEqual(12, vet.EmployeePersonId);
            Assert.AreEqual(2100, vet.Compensation.AgreedWeeklyWageCents);
            Assert.IsTrue(vet.IsActive);
            Assert.AreEqual("Farrier", farrier.RoleDisplayName);
            Assert.AreEqual(EmploymentKind.SeasonalOrCasual, farrier.Kind);
        }

        [Test]
        public void SaveLoad_RoundTripsLots()
        {
            var stock = new VetMedicineStock();
            var diag = new List<string>();
            VetMedicineBootstrap.ApplyBootstrapEndowment(stock, NewRegistry(), 1, diag);
            stock.TryDispenseDoses("veterinary-remedies", 5, 2, diag);

            VetMedicineStock.VetMedicineStockSaveDto dto = stock.CaptureSaveDto();
            var restored = new VetMedicineStock();
            restored.LoadFromSaveDto(dto);

            Assert.AreEqual(stock.DosesOnHand("veterinary-remedies"), restored.DosesOnHand("veterinary-remedies"));
            Assert.AreEqual(stock.Lots.Count, restored.Lots.Count);
        }
    }
}
