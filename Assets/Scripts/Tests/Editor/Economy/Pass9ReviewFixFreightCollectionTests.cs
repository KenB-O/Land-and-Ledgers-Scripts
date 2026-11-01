using System.Collections.Generic;
using LandLedgers.Economy;
using LandLedgers.Economy.Freight;
using LandLedgers.Economy.Logistics;
using LandLedgers.Tasks;
using LandLedgers.Time;
using NUnit.Framework;

namespace LandLedgers.EditorTests.Economy
{
    /// <summary>Pass IX review R-04: the production freight driver collects
    /// through the verified resolver — payer debited, carrier credited
    /// equally; an unresolvable payer yields no credit and the charge
    /// remains outstanding.</summary>
    [TestFixture]
    public sealed class Pass9ReviewFixFreightCollectionTests
    {
        private static BusinessRuntimeState Runtime(string id, int cash)
        {
            var runtime = new BusinessRuntimeState();
            runtime.BindBusinessIdentity(id);
            runtime.SetCurrentCashCents(cash);
            return runtime;
        }

        private static FreightShipmentHooks Hooks() => new FreightShipmentHooks
        {
            Tasks = new TaskAuthority(),
            Budgets = new WorkTimeBudgetStore(),
            DayIndex = 42,
            Diagnostics = new List<string>(),
        };

        private static FreightProductionDriver DriverWithCompletedShipment(
            out BusinessRuntimeState carrier, out FreightCompanyRuntime company)
        {
            var driver = new FreightProductionDriver(new FreightShipmentService());
            carrier = Runtime("biz-freight-1", 0);
            company = driver.EnsureCompany("biz-freight-1", carrier);
            var diag = new List<string>();
            var shipment = new LogisticsShipmentState
            {
                shipmentId = "ship-r04",
                plannedQuantityUnits = 10,
                remainingQuantityUnits = 0,
                freightChargeCents = 500,
                freightPayerBusinessInstanceId = "biz-store-1",
                cargoClass = LogisticsCargoClass.PackagedGoods,
                state = LogisticsShipmentStatus.Completed,
                deliveryApplied = true,
            };
            Assert.IsTrue(company.TryAcceptShipment(shipment, diag));
            return driver;
        }

        [Test]
        public void DriverCollection_DebitsPayerAndCreditsCarrierEqually()
        {
            FreightProductionDriver driver = DriverWithCompletedShipment(out BusinessRuntimeState carrier, out FreightCompanyRuntime company);
            BusinessRuntimeState payer = Runtime("biz-store-1", 600);
            driver.RegisterBusinessRuntime("biz-store-1", payer);
            var diag = new List<string>();

            driver.AdvanceTick(60f, Hooks(), LandLedgers.Primitives.EntityId.Invalid, diag);

            Assert.AreEqual(100, payer.CurrentCashCents, "payer debited the 500c charge");
            Assert.AreEqual(500, carrier.CurrentCashCents, "carrier credited exactly the debit");
            Assert.AreEqual(0, company.FindUncollectedFreightChargeShipmentIds().Count, "collected exactly once");

            driver.AdvanceTick(60f, Hooks(), LandLedgers.Primitives.EntityId.Invalid, diag);
            Assert.AreEqual(100, payer.CurrentCashCents, "no second debit on later ticks");
            Assert.AreEqual(500, carrier.CurrentCashCents);
        }

        [Test]
        public void DriverCollection_UnresolvablePayer_NoCredit_ChargeRemainsOutstanding()
        {
            FreightProductionDriver driver = DriverWithCompletedShipment(out BusinessRuntimeState carrier, out FreightCompanyRuntime company);
            var diag = new List<string>();
            // No payer runtime registered anywhere — the payer cannot be resolved.

            driver.AdvanceTick(60f, Hooks(), LandLedgers.Primitives.EntityId.Invalid, diag);

            Assert.AreEqual(0, carrier.CurrentCashCents, "no phantom carrier credit without a payer debit");
            Assert.AreEqual(1, company.FindUncollectedFreightChargeShipmentIds().Count,
                "the charge remains outstanding for a later, resolvable tick");
        }

        [Test]
        public void DriverCollection_PayerResolverSeam_AlsoResolves()
        {
            FreightProductionDriver driver = DriverWithCompletedShipment(out BusinessRuntimeState carrier, out _);
            BusinessRuntimeState payer = Runtime("biz-store-1", 700);
            driver.PayerRuntimeResolver = id => id == "biz-store-1" ? payer : null;
            var diag = new List<string>();

            driver.AdvanceTick(60f, Hooks(), LandLedgers.Primitives.EntityId.Invalid, diag);

            Assert.AreEqual(200, payer.CurrentCashCents);
            Assert.AreEqual(500, carrier.CurrentCashCents);
        }
    }
}
