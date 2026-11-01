using System.Collections.Generic;
using LandLedgers.Economy.Farming.Dairy;
using LandLedgers.Primitives;
using NUnit.Framework;

namespace LandLedgers.Editor.Economy
{
    /// <summary>
    /// T1G: creamery aggregation (Canon §9.6). Larger cheese operations aggregating milk
    /// from surrounding farms — explicitly NOT one-click milk-to-value. Builds on the
    /// SWN-2 cheese chain: intake, rejection of sour milk, pooling, processing, curing.
    /// </summary>
    [TestFixture]
    public sealed class CreameryTests
    {
        private static MilkLot FreshMilk(int units, int producedDay)
        {
            return new MilkLot
            {
                LotId = EntityId.For(EntityKind.Lot, 5000 + producedDay),
                FarmId = "farm-a",
                ProducedDayIndex = producedDay,
                QuantityUnits = units,
            };
        }

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

        [Test]
        public void AcceptDelivery_BuysFreshMilk_PaysPatron()
        {
            var creamery = NewCreamery();
            var ids = new EntityIdRegistry();
            var diagnostics = new List<string>();

            var lot = FreshMilk(100, 400);
            CreameryDelivery delivery = creamery.AcceptDelivery(ids, "farm-a",
                new List<MilkLot> { lot }, 400, diagnostics);

            Assert.NotNull(delivery, string.Join("; ", diagnostics));
            Assert.AreEqual(100, delivery.AcceptedUnits);
            Assert.AreEqual(0, delivery.RejectedUnits);
            Assert.AreEqual(300, delivery.PaidCents);
            Assert.IsTrue(delivery.DeliveryId.IsValid);
            Assert.AreEqual(0, lot.QuantityUnits, "Accepted milk leaves the farm's lots.");
        }

        [Test]
        public void AcceptDelivery_RejectsSourMilk_NeverPooled()
        {
            var creamery = NewCreamery();
            var ids = new EntityIdRegistry();
            var diagnostics = new List<string>();

            // Produced day 390, delivered day 400: past FreshDays (2) — spoiled.
            var lot = FreshMilk(80, 390);
            CreameryDelivery delivery = creamery.AcceptDelivery(ids, "farm-a",
                new List<MilkLot> { lot }, 400, diagnostics);

            Assert.NotNull(delivery);
            Assert.AreEqual(0, delivery.AcceptedUnits);
            Assert.AreEqual(80, delivery.RejectedUnits);
            Assert.AreEqual(0, delivery.PaidCents);
            Assert.IsTrue(diagnostics.Exists(d => d.Contains("sour milk")));
        }

        [Test]
        public void AcceptDelivery_HonorsDailyCapacity()
        {
            var creamery = NewCreamery();
            var ids = new EntityIdRegistry();
            var diagnostics = new List<string>();

            creamery.AcceptDelivery(ids, "farm-a", new List<MilkLot> { FreshMilk(400, 400) }, 400, diagnostics);
            CreameryDelivery second = creamery.AcceptDelivery(ids, "farm-b",
                new List<MilkLot> { FreshMilk(200, 400) }, 400, diagnostics);

            Assert.AreEqual(100, second.AcceptedUnits, "Capacity is finite — vats are physical.");
            Assert.AreEqual(100, second.RejectedUnits);
        }

        [Test]
        public void ProcessDay_MakesCheeseThroughSwn2Chain()
        {
            var creamery = NewCreamery();
            var ids = new EntityIdRegistry();
            var diagnostics = new List<string>();

            var lot = FreshMilk(200, 400);
            creamery.AcceptDelivery(ids, "farm-a", new List<MilkLot> { lot }, 400, diagnostics);

            var chain = new CheeseChain();
            var pooled = new List<MilkLot>
            {
                new MilkLot
                {
                    LotId = lot.LotId,
                    FarmId = "farm-a",
                    ProducedDayIndex = 400,
                    QuantityUnits = 200,
                },
            };

            List<CheeseWheel> wheels = creamery.ProcessDay(ids, chain, pooled,
                EntityId.For(EntityKind.Person, 7), 400, diagnostics);

            Assert.NotNull(wheels, string.Join("; ", diagnostics));
            Assert.Greater(wheels.Count, 0);
            Assert.AreEqual("creamery-1", wheels[0].FarmId);
            Assert.AreEqual(CheeseAgeState.Green, wheels[0].AgeState, "Wheels are green — curing still takes real days.");
        }

        [Test]
        public void ProcessDay_Refuses_WithoutEquipment()
        {
            var creamery = new Creamery("creamery-1", "Valley Creamery"); // no equipment, no rennet
            var ids = new EntityIdRegistry();
            var diagnostics = new List<string>();

            List<CheeseWheel> wheels = creamery.ProcessDay(ids, new CheeseChain(),
                new List<MilkLot>(), EntityId.For(EntityKind.Person, 7), 400, diagnostics);

            Assert.IsNull(wheels, "No equipment, no cheese — Canon §9.6.");
        }
    }
}
