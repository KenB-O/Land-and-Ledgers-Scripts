using System.Collections.Generic;
using LandLedgers.Economy.GeneralStore;
using LandLedgers.Economy.Retail;
using NUnit.Framework;

namespace LandLedgers.EditorTests.Economy
{
    /// <summary>
    /// NX-1B: merchant-side scarcity allocation — queue order (FIFO); the
    /// unserved remainder becomes honest lost-sale evidence (T1C).
    /// </summary>
    public sealed class MerchantScarcityTests
    {
        private static WaitingCustomer Customer(string ref_, int units)
        {
            return new WaitingCustomer
            {
                CustomerRef = ref_, ActingPersonId = 1,
                CategoryId = "staple_food", UnitsWanted = units,
                PriceCentsPerUnit = 10,
            };
        }

        [Test]
        public void FifoServesHeadFirst()
        {
            var queue = new List<WaitingCustomer>
            {
                Customer("first", 3), Customer("second", 3),
            };
            List<MerchantScarcityAllocator.ServiceResult> results =
                MerchantScarcityAllocator.Allocate(queue, 4);

            Assert.AreEqual(3, results[0].UnitsServed);
            Assert.AreEqual(0, results[0].UnitsUnserved);
            Assert.AreEqual(1, results[1].UnitsServed);
            Assert.AreEqual(2, results[1].UnitsUnserved);
        }

        [Test]
        public void FullStockServesEveryone()
        {
            var queue = new List<WaitingCustomer>
            {
                Customer("a", 2), Customer("b", 2),
            };
            List<MerchantScarcityAllocator.ServiceResult> results =
                MerchantScarcityAllocator.Allocate(queue, 10);
            Assert.AreEqual(0, results[0].UnitsUnserved);
            Assert.AreEqual(0, results[1].UnitsUnserved);
        }

        [Test]
        public void UnservedBecomesLostSaleEvidence()
        {
            var queue = new List<WaitingCustomer> { Customer("a", 5) };
            var results = MerchantScarcityAllocator.Allocate(queue, 2);
            var log = new LostSaleLog();
            int recorded = MerchantScarcityAllocator.RecordLostSales(
                results, log, 10, "store-1", "General Store");

            Assert.AreEqual(1, recorded);
            Assert.AreEqual(1, log.Count);
            Assert.AreEqual(LostSaleReason.Stockout, log.EventsFor("store-1")[0].Reason);
            Assert.AreEqual(3, log.EventsFor("store-1")[0].UnitsWanted);
        }

        [Test]
        public void FullyServedRecordsNothing()
        {
            var queue = new List<WaitingCustomer> { Customer("a", 2) };
            var results = MerchantScarcityAllocator.Allocate(queue, 5);
            var log = new LostSaleLog();
            Assert.AreEqual(0, MerchantScarcityAllocator.RecordLostSales(
                results, log, 10, "store-1", "General Store"));
            Assert.AreEqual(0, log.Count);
        }
    }
}
