using System.Collections.Generic;
using LandLedgers.Economy.AnimalServices;
using LandLedgers.Primitives;
using LandLedgers.World.Journeys;
using NUnit.Framework;

namespace LandLedgers.Editor.Economy
{
    /// <summary>
    /// T1F: vet/medicine suppliers (Canon XXVII §6.2). The practitioner travels to the
    /// farm; every treatment is per-animal; invoices itemize everything.
    /// </summary>
    [TestFixture]
    public sealed class VetServicesTests
    {
        private static JourneyModel NewJourney()
        {
            var journeys = new JourneyModel();
            journeys.RegisterLocation(new JourneyLocation("vet-office", JourneyLocationKind.TownBuilding, "Vet Office", 0f, 0f));
            journeys.RegisterLocation(new JourneyLocation("farm-1", JourneyLocationKind.Farmstead, "Morrow Farm", 6f, 0f));
            journeys.AddEdge("vet-office", "farm-1", 6.0f, "river road");
            return journeys;
        }

        private static VetPractitioner NewVet()
        {
            return new VetPractitioner
            {
                PractitionerId = "vet-1",
                DisplayName = "Dr. Hart",
                PersonId = 42,
                LocationId = "vet-office",
                CallOutFeeCents = 200,
                PerMileTravelCents = 10,
                SpeciesServiced = new List<string> { "cow", "horse" },
            };
        }

        [Test]
        public void CallOut_TravelsBothWays_TreatsEachAnimal_InvoicesLines()
        {
            var service = new VetService();
            service.RegisterPractitioner(NewVet());

            var cases = new List<(EntityId, string, string, string, int, int, string)>
            {
                (EntityId.For(EntityKind.Animal, 101), "cow", "milk fever", "calcium drench", 150, 60, "general store"),
                (EntityId.For(EntityKind.Animal, 102), "cow", "lame hoof", "trim and poultice", 100, 0, ""),
            };
            var diagnostics = new List<string>();

            VetInvoice invoice = service.CallOut("vet-1", "farm-1", "farm-1", cases, 300,
                NewJourney(), new EntityIdRegistry(), diagnostics);

            Assert.NotNull(invoice, string.Join("; ", diagnostics));
            Assert.AreEqual(200, invoice.CallOutFeeCents);
            Assert.AreEqual(6f, invoice.TravelMiles);
            Assert.AreEqual(120, invoice.TravelFeeCents, "6 miles x 2 ways x 10c (traveling-practitioner economics).");
            Assert.AreEqual(2, invoice.Treatments.Count, "Per-animal treatments — no herd-wide magic.");
            Assert.AreEqual(210, invoice.Treatments[0].TotalCostCents);
            Assert.AreEqual(200 + 120 + 210 + 100, invoice.TotalCents);
            Assert.AreEqual(2, service.TreatmentHistory.Count);
        }

        [Test]
        public void CallOut_HonorsStandingAgreement_Fee()
        {
            var service = new VetService();
            service.RegisterPractitioner(NewVet());
            service.RegisterAgreement(new VetServiceAgreement
            {
                AgreementId = EntityId.For(EntityKind.Contract, 1),
                FarmOrBusinessId = "farm-1",
                PractitionerId = "vet-1",
                StartDayIndex = 0,
                AgreedCallOutFeeCents = 100, // standing agreement beats the rack rate
                ScopeNotes = "dairy herd",
            });

            var diagnostics = new List<string>();
            VetInvoice invoice = service.CallOut("vet-1", "farm-1", "farm-1", null, 300,
                NewJourney(), new EntityIdRegistry(), diagnostics);

            Assert.NotNull(invoice);
            Assert.AreEqual(100, invoice.CallOutFeeCents);
        }

        [Test]
        public void CallOut_Refuses_WithoutRoute()
        {
            var service = new VetService();
            service.RegisterPractitioner(NewVet());

            var journeys = new JourneyModel();
            journeys.RegisterLocation(new JourneyLocation("vet-office", JourneyLocationKind.TownBuilding, "Vet Office", 0f, 0f));
            journeys.RegisterLocation(new JourneyLocation("far-farm", JourneyLocationKind.Farmstead, "Far Farm", 50f, 0f));
            // No edge: unreachable.

            var diagnostics = new List<string>();
            VetInvoice invoice = service.CallOut("vet-1", "farm-1", "far-farm", null, 300,
                journeys, new EntityIdRegistry(), diagnostics);

            Assert.IsNull(invoice, "No route, no visit — the travel burden is real.");
        }

        [Test]
        public void CallOut_Skips_UnservicedSpecies_Loudly()
        {
            var service = new VetService();
            service.RegisterPractitioner(NewVet()); // cows and horses only

            var cases = new List<(EntityId, string, string, string, int, int, string)>
            {
                (EntityId.For(EntityKind.Animal, 201), "chicken", "mites", "dust", 20, 10, "general store"),
            };
            var diagnostics = new List<string>();
            VetInvoice invoice = service.CallOut("vet-1", "farm-1", "farm-1", cases, 300,
                NewJourney(), new EntityIdRegistry(), diagnostics);

            Assert.NotNull(invoice);
            Assert.AreEqual(0, invoice.Treatments.Count);
            Assert.IsTrue(diagnostics.Exists(d => d.Contains("does not service")));
        }

        [Test]
        public void Farrier_ShoesPerHorse_ShoesFromNamedSmith()
        {
            var farrier = new FarrierService();
            var diagnostics = new List<string>();
            int shoeStock = 10;

            string problem = farrier.ShoeHorse(
                EntityId.For(EntityKind.Animal, 301), 4, ref shoeStock,
                "blacksmith: Tom's Forge", 25, 100, 310, 43, diagnostics);

            Assert.IsNull(problem, string.Join("; ", diagnostics));
            Assert.AreEqual(6, shoeStock, "Shoes are consumed from real stock.");

            // No source, no shoeing.
            string noSource = farrier.ShoeHorse(
                EntityId.For(EntityKind.Animal, 302), 4, ref shoeStock,
                "", 25, 100, 310, 43, diagnostics);
            Assert.NotNull(noSource);
            Assert.AreEqual(6, shoeStock);

            // Short stock, no faking.
            string shortStock = farrier.ShoeHorse(
                EntityId.For(EntityKind.Animal, 303), 8, ref shoeStock,
                "blacksmith: Tom's Forge", 25, 100, 310, 43, diagnostics);
            Assert.NotNull(shortStock);
            Assert.AreEqual(6, shoeStock);
        }

        [Test]
        public void Treatment_RequiresValidAnimalId()
        {
            var service = new VetService();
            service.RegisterPractitioner(NewVet());

            var cases = new List<(EntityId, string, string, string, int, int, string)>
            {
                (EntityId.Invalid, "cow", "sick", "dose", 50, 20, "store"),
            };
            var diagnostics = new List<string>();
            VetInvoice invoice = service.CallOut("vet-1", "farm-1", "farm-1", cases, 300,
                NewJourney(), new EntityIdRegistry(), diagnostics);

            Assert.NotNull(invoice);
            Assert.AreEqual(0, invoice.Treatments.Count, "Invalid animal ids are refused — no herd-wide treatments.");
        }
    }
}
