using System.Collections.Generic;
using LandLedgers.Economy.GeneralStore;
using LandLedgers.Economy.Retail;
using NUnit.Framework;

namespace LandLedgers.Editor.Economy
{
    /// <summary>
    /// T1C: Customer Choice Resolver (Tech X §5.3) + lost-sale events (Tech X §5.4)
    /// + the waiting-customer queue with teeth.
    /// </summary>
    [TestFixture]
    public sealed class RetailChoiceTests
    {
        private static MerchantOption Option(
            string name, int price, int wait, float miles,
            bool inStock = true, bool open = true, bool reachable = true,
            int quality = 0, bool trusted = false)
        {
            return new MerchantOption
            {
                MerchantBusinessId = "m-" + name,
                MerchantName = name,
                PriceCentsPerUnit = price,
                WaitMinutesEstimate = wait,
                MilesDistance = miles,
                InStock = inStock,
                Open = open,
                Reachable = reachable,
                QualityRank = quality,
                TrustedSource = trusted,
            };
        }

        [Test]
        public void Resolver_FeasibilityFilters_BeforePolicy()
        {
            var intent = new PurchaseIntent { ActingPersonId = 1, CategoryId = "flour", UnitsWanted = 5, Type = PurchaseType.Staple };
            var options = new List<MerchantOption>
            {
                Option("cheap-closed", 50, 5, 1f, open: false),   // cheapest but closed
                Option("mid", 100, 5, 1f),                        // feasible
                Option("far-stockout", 60, 5, 1f, inStock: false), // cheap but no stock
            };

            ChoiceResult result = CustomerChoiceResolver.Resolve(intent, options);

            Assert.IsTrue(result.Resolved);
            Assert.AreEqual("mid", result.Chosen.MerchantName);
            Assert.AreEqual(1, result.FeasibleSet.Count);
        }

        [Test]
        public void Resolver_Policy_ByPurchaseType()
        {
            var options = new List<MerchantOption>
            {
                Option("cheap-far", 50, 30, 9f),
                Option("pricey-near", 120, 5, 1f),
            };

            var staple = new PurchaseIntent { Type = PurchaseType.Staple };
            Assert.AreEqual("cheap-far", CustomerChoiceResolver.Resolve(staple, options).Chosen.MerchantName);

            var urgent = new PurchaseIntent { Type = PurchaseType.Urgent };
            Assert.AreEqual("pricey-near", CustomerChoiceResolver.Resolve(urgent, options).Chosen.MerchantName);

            var planned = new PurchaseIntent { Type = PurchaseType.Planned };
            Assert.AreEqual("pricey-near", CustomerChoiceResolver.Resolve(planned, options).Chosen.MerchantName);
        }

        [Test]
        public void Resolver_Considered_UsesQualityThenTrust()
        {
            var options = new List<MerchantOption>
            {
                Option("good", 200, 5, 1f, quality: 3),
                Option("best", 200, 5, 1f, quality: 5),
                Option("trusted-good", 200, 5, 1f, quality: 5, trusted: true),
            };

            var intent = new PurchaseIntent { Type = PurchaseType.Considered };
            Assert.AreEqual("trusted-good", CustomerChoiceResolver.Resolve(intent, options).Chosen.MerchantName);
        }

        [Test]
        public void Resolver_EmptyFeasibleSet_ReturnsFailure_NoAutoReroute()
        {
            var intent = new PurchaseIntent { ActingPersonId = 1, CategoryId = "flour", UnitsWanted = 5 };
            var options = new List<MerchantOption>
            {
                Option("closed", 50, 5, 1f, open: false),
            };

            ChoiceResult result = CustomerChoiceResolver.Resolve(intent, options);

            Assert.IsFalse(result.Resolved);
            Assert.IsNull(result.Chosen);
            Assert.AreEqual(LostSaleReason.NoFeasibleChoice, result.FailureReason);
            Assert.IsTrue(result.FailureDetail.Contains("closed"));
        }

        [Test]
        public void Queue_PatienceExpiry_WritesLostSale()
        {
            var log = new LostSaleLog();
            var queue = new CustomerQueue(log)
            {
                MerchantBusinessId = "store-1",
                MerchantName = "General Store",
            };

            Assert.IsTrue(queue.Enqueue("cust-a", 1, "flour", 5, 100, 10, patienceMinutes: 20));
            Assert.IsTrue(queue.Enqueue("cust-b", 2, "flour", 3, 100, 10, patienceMinutes: 20));
            Assert.AreEqual(2, queue.WaitingCount);

            int balked = queue.Tick(25, 10);

            Assert.AreEqual(2, balked);
            Assert.AreEqual(0, queue.WaitingCount);
            Assert.AreEqual(2, log.Count);
            Assert.AreEqual(LostSaleReason.QueueBalk, log.EventsFor("store-1")[0].Reason);
        }

        [Test]
        public void Queue_OverlongLine_BalksOnArrival()
        {
            var log = new LostSaleLog();
            var queue = new CustomerQueue(log)
            {
                MerchantBusinessId = "store-1",
                MerchantName = "General Store",
            };

            for (int i = 0; i < CustomerQueue.MaxToleratedQueueLength; i++)
            {
                Assert.IsTrue(queue.Enqueue("cust-" + i, i, "flour", 1, 100, 10));
            }

            bool joined = queue.Enqueue("cust-late", 99, "flour", 4, 100, 10);

            Assert.IsFalse(joined);
            Assert.AreEqual(1, log.Count);
            Assert.AreEqual(LostSaleReason.QueueBalk, log.EventsFor("store-1")[0].Reason);
            Assert.AreEqual(4, log.EventsFor("store-1")[0].UnitsWanted);
        }

        [Test]
        public void Queue_ServeNext_IsFifo_AndNoLostSale()
        {
            var log = new LostSaleLog();
            var queue = new CustomerQueue(log);
            queue.Enqueue("cust-a", 1, "flour", 5, 100, 10);
            queue.Enqueue("cust-b", 2, "flour", 3, 100, 10);

            Assert.AreEqual("cust-a", queue.ServeNext());
            Assert.AreEqual(1, queue.WaitingCount);
            Assert.AreEqual(0, log.Count, "Served customers are not lost sales.");
            Assert.AreEqual("cust-b", queue.ServeNext());
            Assert.IsNull(queue.ServeNext());
        }

        [Test]
        public void LostSaleLog_BuildDiagnostics_SummarizesByReason()
        {
            var log = new LostSaleLog();
            log.Record(10, "store-1", "General Store", 1, "flour", 5, 100, LostSaleReason.QueueBalk, "patience");
            log.Record(10, "store-1", "General Store", 2, "flour", 2, 100, LostSaleReason.QueueBalk, "patience");
            log.Record(11, "store-1", "General Store", 3, "nails", 1, 250, LostSaleReason.Stockout, "no stock");

            string diagnostics = log.BuildDiagnostics("store-1", "General Store");

            StringAssert.Contains("3 lost sales", diagnostics);
            StringAssert.Contains("QueueBalk: 2 (7u)", diagnostics);
            StringAssert.Contains("Stockout: 1 (1u)", diagnostics);
            StringAssert.Contains("950c of demand unmet", diagnostics); // 7*100 + 1*250
        }
    }
}
