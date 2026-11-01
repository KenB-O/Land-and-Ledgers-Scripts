using System.Collections.Generic;
using LandLedgers.Economy.Farming.Dairy;
using LandLedgers.Primitives;
using LandLedgers.Tasks;
using NUnit.Framework;

namespace LandLedgers.Editor.Economy
{
    /// <summary>
    /// D3F: creamery depth (Canon XXVII Part IX §9.6 — the creamery aggregates milk
    /// from surrounding farms). Fills the width gaps: patron farms are named, the
    /// daily vat pool is accounted (ProcessDay can only process milk the creamery
    /// actually accepted), production days leave an auditable batch record, the
    /// creamery and the settlement service survive saves, and a delivery settles
    /// exactly once. Cheese-factory links and patron co-op equity were canon-silent
    /// and are NOT built (audit verdicts).
    /// </summary>
    [TestFixture]
    public sealed class CreameryDepth3FTests
    {
        private static Creamery NewCreamery()
        {
            return new Creamery("creamery-1", "Valley Creamery")
            {
                PricePerMilkUnitCents = 3,
                MaxIntakeUnitsPerDay = 500,
                Equipment = "copper vats + screw press",
                RennetSource = "butcher: town butcher",
            };
        }

        private static MilkLot FreshMilk(string farmId, int units, int producedDay)
        {
            return new MilkLot
            {
                LotId = EntityId.For(EntityKind.Lot, 9000 + units + producedDay),
                FarmId = farmId,
                ProducedDayIndex = producedDay,
                QuantityUnits = units,
            };
        }

        private static CreameryDelivery Deliver(Creamery creamery, EntityIdRegistry ids, string farmId, int units, int day)
        {
            return creamery.AcceptDelivery(ids, farmId,
                new List<MilkLot> { FreshMilk(farmId, units, day) }, day, new List<string>());
        }

        private static List<MilkLot> RematerializedPool(string farmId, int units, int day)
        {
            // The caller's re-materialized view of the creamery's held milk (unit
            // counts, not lot identity) — the shape ProcessDay expects.
            return new List<MilkLot> { FreshMilk(farmId, units, day) };
        }

        [Test]
        public void Patron_AutoRegisteredOnFirstDelivery_WithTotals()
        {
            var creamery = NewCreamery();
            var ids = new EntityIdRegistry();

            Deliver(creamery, ids, "farm-a", 100, 400);
            Deliver(creamery, ids, "farm-a", 50, 401);

            CreameryPatron patron = creamery.GetPatron("farm-a");
            Assert.IsNotNull(patron, "Patrons are real farms — the farm is named on delivery.");
            Assert.AreEqual("farm-a", patron.FarmId);
            Assert.AreEqual(400, patron.FirstDeliveryDayIndex);
            Assert.AreEqual(150, patron.TotalAcceptedUnits);
            Assert.AreEqual(1, creamery.Patrons.Count);
        }

        [Test]
        public void RegisterPatron_NamesOwnerPerson_UpdatesOnReregister()
        {
            var creamery = NewCreamery();
            var diag = new List<string>();
            var owner = EntityId.For(EntityKind.Person, 42);

            CreameryPatron patron = creamery.RegisterPatron("farm-a", "Morrow Farm", owner, 399, diag);
            Assert.IsNotNull(patron);
            Assert.AreEqual("Morrow Farm", patron.DisplayName);
            Assert.AreEqual(owner, patron.OwnerPersonId);

            // Re-registering the same farm updates rather than duplicating.
            creamery.RegisterPatron("farm-a", "Morrow Farm Ltd.", owner, 399, diag);
            Assert.AreEqual(1, creamery.Patrons.Count);
            Assert.AreEqual("Morrow Farm Ltd.", creamery.GetPatron("farm-a").DisplayName);
        }

        [Test]
        public void RegisterPatron_RefusesNonPersonOwner_Loudly()
        {
            var creamery = NewCreamery();
            var diag = new List<string>();

            CreameryPatron patron = creamery.RegisterPatron(
                "farm-a", "Morrow Farm", EntityId.For(EntityKind.Lot, 7), 399, diag);

            Assert.IsNotNull(patron, "The farm still registers — only the owner stays unnamed.");
            Assert.IsFalse(patron.OwnerPersonId.IsValid);
            Assert.IsTrue(diag.Exists(d => d.Contains("not a person")));
        }

        [Test]
        public void ProcessDay_RefusesMilkNeverReceived()
        {
            var creamery = NewCreamery();
            var ids = new EntityIdRegistry();
            var diag = new List<string>();

            List<CheeseWheel> wheels = creamery.ProcessDay(
                ids, new CheeseChain(), RematerializedPool("farm-a", 100, 400),
                EntityId.For(EntityKind.Person, 7), 400, diag);

            Assert.IsNull(wheels, "An empty vat makes no cheese — milk is not conjured.");
            Assert.IsTrue(diag.Exists(d => d.Contains("no milk in the day-400 vat pool")));
        }

        [Test]
        public void ProcessDay_RefusesMoreThanThePoolHolds()
        {
            var creamery = NewCreamery();
            var ids = new EntityIdRegistry();
            var diag = new List<string>();

            Deliver(creamery, ids, "farm-a", 100, 400);

            List<CheeseWheel> wheels = creamery.ProcessDay(
                ids, new CheeseChain(), RematerializedPool("farm-a", 250, 400),
                EntityId.For(EntityKind.Person, 7), 400, diag);

            Assert.IsNull(wheels, "Milk the creamery never received cannot become cheese.");
            Assert.IsTrue(diag.Exists(d => d.Contains("never received")));
        }

        [Test]
        public void ProcessDay_ConsumesPool_NeverTwice()
        {
            var creamery = NewCreamery();
            var ids = new EntityIdRegistry();
            var diag = new List<string>();

            Deliver(creamery, ids, "farm-a", 200, 400);

            List<CheeseWheel> first = creamery.ProcessDay(
                ids, new CheeseChain(), RematerializedPool("farm-a", 200, 400),
                EntityId.For(EntityKind.Person, 7), 400, diag);
            Assert.IsNotNull(first, string.Join("; ", diag));
            Assert.AreEqual(0, creamery.GetPoolDay(400).RemainingUnits);

            // The same milk cannot be processed a second time.
            var diag2 = new List<string>();
            List<CheeseWheel> second = creamery.ProcessDay(
                ids, new CheeseChain(), RematerializedPool("farm-a", 200, 400),
                EntityId.For(EntityKind.Person, 7), 400, diag2);
            Assert.IsNull(second);
            Assert.IsTrue(diag2.Exists(d => d.Contains("no milk in the day-400 vat pool")));
        }

        [Test]
        public void ProcessDay_RecordsBatchWithWheyByproduct()
        {
            var creamery = NewCreamery();
            var ids = new EntityIdRegistry();
            var diag = new List<string>();

            Deliver(creamery, ids, "farm-a", 200, 400);
            List<CheeseWheel> wheels = creamery.ProcessDay(
                ids, new CheeseChain(), RematerializedPool("farm-a", 200, 400),
                EntityId.For(EntityKind.Person, 7), 400, diag);

            Assert.IsNotNull(wheels, string.Join("; ", diag));
            Assert.AreEqual(1, creamery.Batches.Count);
            CreameryBatchRecord batch = creamery.Batches[0];
            Assert.AreEqual(400, batch.DayIndex);
            Assert.AreEqual(200, batch.InputMilkUnits);
            Assert.AreEqual((200 * CheeseChain.WheyUnitsPer10Milk) / 10, batch.WheyUnits,
                "Whey byproduct is accounted for downstream pig-feed routing (Canon §9.7).");
            Assert.IsNotEmpty(batch.WheelLotIds);
            Assert.IsNotEmpty(batch.InputMilkLotIds);
            Assert.AreEqual(EntityId.For(EntityKind.Person, 7), batch.WorkerId);
        }

        [Test]
        public void SaveLoad_RoundTripsCreameryState()
        {
            var creamery = NewCreamery();
            var ids = new EntityIdRegistry();
            var diag = new List<string>();

            creamery.RegisterPatron("farm-a", "Morrow Farm", EntityId.For(EntityKind.Person, 42), 399, diag);
            Deliver(creamery, ids, "farm-a", 200, 400);
            creamery.ProcessDay(ids, new CheeseChain(), RematerializedPool("farm-a", 200, 400),
                EntityId.For(EntityKind.Person, 7), 400, diag);

            CreamerySaveDto dto = creamery.CaptureSaveDto();

            var restored = new Creamery("other", "Other");
            restored.LoadFromSaveDto(dto);

            Assert.AreEqual("creamery-1", restored.CreameryBusinessId);
            Assert.AreEqual("Valley Creamery", restored.CreameryName);
            Assert.AreEqual(1, restored.Deliveries.Count);
            Assert.AreEqual(200, restored.Deliveries[0].AcceptedUnits);
            Assert.AreEqual(1, restored.Patrons.Count);
            Assert.AreEqual("Morrow Farm", restored.GetPatron("farm-a").DisplayName);
            Assert.AreEqual(200, restored.GetPatron("farm-a").TotalAcceptedUnits);
            Assert.AreEqual(0, restored.GetPoolDay(400).RemainingUnits, "The vat was consumed before the save.");
            Assert.AreEqual(1, restored.Batches.Count);
            Assert.AreEqual(160, restored.Batches[0].WheyUnits);
        }

        [Test]
        public void BuildPatronStatement_RefusesDoubleCoverage()
        {
            var creamery = NewCreamery();
            var ids = new EntityIdRegistry();
            var service = new MilkCheckService();
            var diag = new List<string>();

            Deliver(creamery, ids, "farm-a", 100, 40);

            PatronMilkStatement first = service.BuildPatronStatement(
                creamery, "farm-a", 31, 60, null, ids, diag);
            Assert.IsNotNull(first);

            // Overlapping month range covers the same delivery — refused, loudly.
            var diag2 = new List<string>();
            PatronMilkStatement second = service.BuildPatronStatement(
                creamery, "farm-a", 40, 70, null, ids, diag2);
            Assert.IsNull(second, "A delivery settles exactly once — no double payment.");
            Assert.IsTrue(diag2.Exists(d => d.Contains("already covered")));
        }

        [Test]
        public void BuildPatronStatement_AllowsFreshDeliveriesAfterPriorStatement()
        {
            var creamery = NewCreamery();
            var ids = new EntityIdRegistry();
            var service = new MilkCheckService();
            var diag = new List<string>();

            Deliver(creamery, ids, "farm-a", 100, 40);
            PatronMilkStatement first = service.BuildPatronStatement(
                creamery, "farm-a", 31, 60, null, ids, diag);
            Assert.IsNotNull(first);

            // A later, non-overlapping delivery settles fine.
            Deliver(creamery, ids, "farm-a", 100, 65);
            PatronMilkStatement second = service.BuildPatronStatement(
                creamery, "farm-a", 61, 90, null, ids, diag);
            Assert.IsNotNull(second);
            Assert.AreEqual(100, second.TotalAcceptedUnits);
        }

        [Test]
        public void StatementService_SaveLoad_PreservesCoverage()
        {
            var creamery = NewCreamery();
            var ids = new EntityIdRegistry();
            var service = new MilkCheckService();
            var diag = new List<string>();

            Deliver(creamery, ids, "farm-a", 100, 40);
            PatronMilkStatement statement = service.BuildPatronStatement(
                creamery, "farm-a", 31, 60, null, ids, diag);
            Assert.IsNotNull(statement);

            MilkCheckServiceSaveDto dto = service.CaptureSaveDto();

            var restored = new MilkCheckService();
            restored.LoadFromSaveDto(dto);
            Assert.AreEqual(1, restored.Statements.Count);
            Assert.AreEqual(300, restored.Statements[0].GrossPayableCents);

            // Coverage survives the save: the delivery cannot settle again.
            var diag2 = new List<string>();
            Assert.IsNull(restored.BuildPatronStatement(
                creamery, "farm-a", 31, 60, null, ids, diag2));
            Assert.IsTrue(diag2.Exists(d => d.Contains("already covered")));
        }

        [Test]
        public void Labor_RegistersIntakeAndTestingTasks()
        {
            var authority = new TaskAuthority();

            CreameryLabor.RegisterTaskDefinitions(authority);

            TaskDefinition receive = authority.GetDefinition(CreameryLabor.ReceiveMilkDeliveryTaskId);
            TaskDefinition test = authority.GetDefinition(CreameryLabor.TestButterfatTaskId);
            Assert.IsNotNull(receive);
            Assert.IsNotNull(test);
            Assert.AreEqual(DairyChain.DairyProcessingSkillId, receive.RequiredSkillId);
            Assert.AreEqual(DairyChain.DairyProcessingSkillId, test.RequiredSkillId);
            Assert.Greater(receive.BaseMinutes, 0);
            Assert.Greater(test.BaseMinutes, 0);
        }
    }
}
