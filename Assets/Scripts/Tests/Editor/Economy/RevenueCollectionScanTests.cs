using System.Collections.Generic;
using LandLedgers.Economy;
using LandLedgers.Economy.Freight;
using LandLedgers.Economy.Postal;
using LandLedgers.World.Journeys;
using NUnit.Framework;

namespace LandLedgers.EditorTests.Economy
{
    /// <summary>
    /// G-02: revenue collection scans. FreightCompanyRuntime.CollectFreightCharge
    /// and PostalService.CollectPostageRevenue had no live callers, so
    /// freight/postal businesses could not earn. Pure scans find
    /// completed-uncollected shipments/deliveries; the collect methods run;
    /// positive amounts post to the business ledger with provenance —
    /// idempotent via the collected flag (freight) / the draining pot (postal).
    /// </summary>
    [TestFixture]
    public sealed class RevenueCollectionScanTests
    {
        private static FreightCompanyRuntime BuildCompany()
        {
            var company = new FreightCompanyRuntime("biz_freight_001");
            company.AddRoute(new FreightRoute("r1", "Town Run", "Depot", "Town", 120));
            return company;
        }

        private static LogisticsShipmentState BuildShipment(string id, string payer)
        {
            return new LogisticsShipmentState
            {
                shipmentId = id,
                plannedQuantityUnits = 100,
                remainingQuantityUnits = 0,
                freightChargeCents = 500,
                freightPayerBusinessInstanceId = payer,
                cargoClass = LogisticsCargoClass.PackagedGoods,
                state = LogisticsShipmentStatus.Planned,
            };
        }

        private static LogisticsShipmentState CompletedShipment(string id, string payer)
        {
            LogisticsShipmentState shipment = BuildShipment(id, payer);
            shipment.state = LogisticsShipmentStatus.Completed;
            shipment.deliveryApplied = true;
            return shipment;
        }

        [Test]
        public void FreightScan_FindsOnlyCompletedDeliveredUncollected()
        {
            var company = BuildCompany();
            var diag = new List<string>();
            company.TryAcceptShipment(CompletedShipment("done-external", "biz_store_001"), diag);
            company.TryAcceptShipment(BuildShipment("planned", "biz_store_001"), diag);
            var completedNoDelivery = CompletedShipment("no-delivery", "biz_store_001");
            completedNoDelivery.deliveryApplied = false;
            company.TryAcceptShipment(completedNoDelivery, diag);

            List<string> ids = company.FindUncollectedFreightChargeShipmentIds();

            Assert.AreEqual(1, ids.Count);
            Assert.AreEqual("done-external", ids[0]);
        }

        [Test]
        public void FreightCollectAndPost_PostsOnce_Idempotent()
        {
            var company = BuildCompany();
            var diag = new List<string>();
            company.TryAcceptShipment(CompletedShipment("s1", "biz_store_001"), diag);
            var businessRuntime = new BusinessRuntimeState();

            int posted = company.CollectAndPostUncollectedRevenue(businessRuntime, diag);

            Assert.AreEqual(500, posted);
            Assert.AreEqual(500, businessRuntime.CurrentCashCents, "The freight business earned its charge.");
            StringAssert.Contains("provenance: freight charge", string.Join(" ", diag.ToArray()));

            int reposted = company.CollectAndPostUncollectedRevenue(businessRuntime, diag);
            Assert.AreEqual(0, reposted, "Idempotent: the collected flag makes re-scans yield nothing.");
            Assert.AreEqual(500, businessRuntime.CurrentCashCents, "No double posting.");
            Assert.IsEmpty(company.FindUncollectedFreightChargeShipmentIds());
        }

        [Test]
        public void FreightCollectAndPost_SelfPayer_YieldsZero()
        {
            var company = BuildCompany();
            var diag = new List<string>();
            company.TryAcceptShipment(CompletedShipment("self", "biz_freight_001"), diag); // self
            var businessRuntime = new BusinessRuntimeState();

            int posted = company.CollectAndPostUncollectedRevenue(businessRuntime, diag);

            Assert.AreEqual(0, posted, "Canon §3.6: self-payer yields zero — no fabricated revenue.");
            Assert.AreEqual(0, businessRuntime.CurrentCashCents);
        }

        [Test]
        public void FreightCollectAndPost_MissingPayer_YieldsZero()
        {
            var company = BuildCompany();
            var diag = new List<string>();
            company.TryAcceptShipment(CompletedShipment("nopayer", string.Empty), diag);
            var businessRuntime = new BusinessRuntimeState();

            int posted = company.CollectAndPostUncollectedRevenue(businessRuntime, diag);

            Assert.AreEqual(0, posted, "Missing payer yields zero.");
            Assert.AreEqual(0, businessRuntime.CurrentCashCents);
        }

        [Test]
        public void FreightCollectedFlag_SurvivesSaveRoundTrip()
        {
            LogisticsShipmentState shipment = CompletedShipment("s1", "biz_store_001");
            shipment.freightRevenueCollected = true;

            LogisticsShipmentState restored = LogisticsShipmentState.FromSaveDto(shipment.CaptureSaveDto());

            Assert.IsTrue(restored.FreightRevenueCollected,
                "Idempotency survives save/load: a collected shipment is never rescanned.");
            Assert.IsFalse(new LogisticsShipmentState().FreightRevenueCollected,
                "Legacy/default shipments read uncollected.");
        }

        private static PostalService TwoOffices()
        {
            var postal = new PostalService();
            var diag = new List<string>();
            Assert.IsNull(postal.RegisterOffice("po-a", "town-a", "biz-po-a", new List<int> { 0 }, 7, diag));
            Assert.IsNull(postal.RegisterOffice("po-b", "town-b", "biz-po-b", new List<int> { 0 }, 9, diag));
            return postal;
        }

        [Test]
        public void PostalScan_IsPure_CollectAndPost_DrainsPotOnce()
        {
            var postal = TwoOffices();
            var diag = new List<string>();
            postal.PostItem(MailKind.Letter, "Kennedy", 1, "Emma", "po-a", "po-b", 0, diag);
            postal.PostItem(MailKind.Letter, "Kennedy", 1, "Emma", "po-a", "po-b", 0, diag);
            var businessRuntime = new BusinessRuntimeState();

            int scanned = postal.FindUncollectedPostageCents();
            Assert.AreEqual(6, scanned, "Two letters at 3c postage.");
            Assert.AreEqual(6, postal.FindUncollectedPostageCents(), "The scan never mutates.");

            int posted = postal.CollectAndPostPostageRevenue(businessRuntime, diag);

            Assert.AreEqual(6, posted);
            Assert.AreEqual(6, businessRuntime.CurrentCashCents, "The post office business earned its postage.");
            Assert.AreEqual(0, postal.FindUncollectedPostageCents());
            StringAssert.Contains("provenance: postage", string.Join(" ", diag.ToArray()));

            Assert.AreEqual(0, postal.CollectAndPostPostageRevenue(businessRuntime, diag),
                "Idempotent: the drained pot yields nothing on re-scan.");
            Assert.AreEqual(6, businessRuntime.CurrentCashCents, "No double posting.");
        }
    }
}
