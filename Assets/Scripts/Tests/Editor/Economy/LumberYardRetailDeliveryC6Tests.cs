using EntityId = LandLedgers.Primitives.EntityId;

using System.Collections.Generic;
using LandLedgers.Economy;
using LandLedgers.Economy.Blacksmith;
using LandLedgers.Economy.Businesses.GeneralStore;
using LandLedgers.Economy.Businesses.Logging;
using LandLedgers.Economy.Businesses.LumberYard;
using LandLedgers.Economy.Businesses.Sawmill;
using LandLedgers.Economy.Freight;
using LandLedgers.Economy.Logistics;
using LandLedgers.Population;
using LandLedgers.Primitives;
using LandLedgers.Tasks;
using LandLedgers.Time;
using NUnit.Framework;

// C-6 (expanded campaign 2026-11-16, shared with F): yard retail
// delivery. The yard's retail sale withdraws real lots; this service is
// the DELIVERY leg only — it quotes the delivery through the DEF-016
// cargo-aware calculator, settles the delivery charge per trip (buyer to
// carrier, paid before the trip runs), moves the lumber through the real
// freight chain, and marks the order delivered. The goods money is the
// buyer-side flow's job (the F4 Pay pattern) and is NOT settled here:
// the yard's goods balance is untouched by this service, and these tests
// pin exactly that split. A buyer who cannot pay the delivery charge
// waits loudly; a refused receiver holds the cargo in wagon custody.
namespace LandLedgers.Editor.Economy
{
    [TestFixture]
    public sealed class LumberYardRetailDeliveryC6Tests
    {
        private const int Day = 210;
        private const int DeliveryDay = 220;

        // The production card (DevelopmentTownConfiguration asset):
        // 25c base + 1c/mile, round trip charged. 28u on 6 miles is one
        // trip: ceil(6 x 2) = 12 chargeable miles x 1c + 25c = 37c.
        // 40u on 6 miles is two trips: 2 x 37c = 74c.
        private const int SixMileTripChargeCents = 37;

        private sealed class World
        {
            public SawmillShopRuntime Mill;
            public LumberYardShopRuntime Yard;
            public List<string> Diagnostics = new List<string>();
            public SawmillToYardTransferDriver TransferDriver;
            public TestBusinessCashPort YardCash;
            public TestBusinessCashPort MillCash;
            public FreightShipmentService Freight = new FreightShipmentService();
            public TaskAuthority Tasks = new TaskAuthority();
            public WorkTimeBudgetStore Budgets = new WorkTimeBudgetStore();
            public PersonScheduleTracker Schedules = new PersonScheduleTracker();
            public EquipmentAsset Wagon;
            public DevelopmentTownConfiguration.FreightServiceRateCard RateCard;
            public TestBusinessCashPort BuyerCash;
            public TestBusinessCashPort CarrierCash;
            public int ReceivedUnits;
            public bool RefuseReceiving;

            public int ReceiveShipment(LogisticsShipmentState shipment, int quantityUnits, out string reason)
            {
                reason = string.Empty;
                if (RefuseReceiving)
                {
                    reason = "test buyer refuses the delivery";
                    return 0;
                }
                if (shipment == null || quantityUnits <= 0)
                {
                    reason = "no shipment or nothing to receive";
                    return 0;
                }
                ReceivedUnits += quantityUnits;
                return quantityUnits;
            }
        }

        /// <summary>
        /// C-4: in-memory business cash port. Mirrors the production
        /// Series2BusinessRuntimeCashPort contract: Credit cannot fail
        /// for a non-negative amount on a bound port; TryDebit is atomic
        /// (refuses when short, moving nothing) and returns null on
        /// success, a diagnostic on failure.
        /// </summary>
        private sealed class TestBusinessCashPort : IBusinessIdentityCashPort
        {
            public string BusinessId { get; }
            public int BalanceCents { get; private set; }
            public bool RefuseCredit { get; set; }
            public bool RefuseDebit { get; set; }

            public TestBusinessCashPort(string businessId, int openingBalanceCents)
            {
                BusinessId = businessId;
                BalanceCents = openingBalanceCents;
            }

            public void SetBalance(int balanceCents)
            {
                BalanceCents = balanceCents;
            }

            public string TryDebit(int amountCents, string reason)
            {
                if (RefuseDebit) return "TestBusinessCashPort: debit refused by the test double.";
                if (amountCents < 0) return "TestBusinessCashPort: negative debit.";
                if (BalanceCents < amountCents) return $"TestBusinessCashPort: insufficient funds ({BalanceCents}c for {amountCents}c).";
                BalanceCents -= amountCents;
                return null;
            }

            public string Credit(int amountCents, string reason)
            {
                if (RefuseCredit) return "TestBusinessCashPort: credit refused by the test double.";
                if (amountCents < 0) return "TestBusinessCashPort: negative credit.";
                BalanceCents += amountCents;
                return null;
            }
        }

        private static World NewWorld()
        {
            var mill = new SawmillShopRuntime("mill-1", new EntityIdRegistry());
            var yard = new LumberYardShopRuntime("yard-1", new EntityIdRegistry());
            var diagnostics = new List<string>();
            var transferDriver = new SawmillToYardTransferDriver(mill, yard, diagnostics);
            var world = new World
            {
                Mill = mill,
                Yard = yard,
                Diagnostics = diagnostics,
                TransferDriver = transferDriver,
                YardCash = new TestBusinessCashPort("yard-1", 10000),
                MillCash = new TestBusinessCashPort("mill-1", 500),
                Wagon = new EquipmentAsset
                {
                    AssetId = "opening-freight-wagon",
                    Kind = "wagon",
                    DisplayName = "farm wagon",
                    PayloadMassCapacityKg = 450f,
                    CargoVolumeCapacityM3 = 4f,
                    Condition01 = 1f,
                },
                RateCard = new DevelopmentTownConfiguration.FreightServiceRateCard
                {
                    baseServiceFeeCents = 25,
                    ratePerMileCents = 1,
                    chargeRoundTrip = true,
                },
                BuyerCash = new TestBusinessCashPort("carpenter-shop", 10000),
                CarrierCash = new TestBusinessCashPort("freight-co", 0),
            };
            world.TransferDriver.YardCash = world.YardCash;
            world.TransferDriver.MillCash = world.MillCash;
            return world;
        }

        private static LumberYardGradePricePolicy PricedPolicy()
        {
            var policy = new LumberYardGradePricePolicy { MarkupRatio = 0.25f, SeasonalMultiplier = 1f };
            policy.GradeMultipliers.Add(new LumberYardGradePriceEntry("clear", 1.5f));
            policy.GradeMultipliers.Add(new LumberYardGradePriceEntry("merchantable", 1.0f));
            policy.GradeMultipliers.Add(new LumberYardGradePriceEntry("cull", 0.4f));
            return policy;
        }

        private static SawmillLumberLot DocumentedMillLot(int lotNumber, int units)
        {
            return new SawmillLumberLot
            {
                LotId = EntityId.For(EntityKind.Lot, lotNumber),
                LumberUnits = units,
                OwnerId = "mill-1",
                SourceLogLotId = "log-" + lotNumber,
                StandId = "stand-1",
                Species = "white pine",
                MillBusinessId = "mill-1",
                SawedBy = EntityId.For(EntityKind.Person, 5002),
                SawedDayIndex = 210,
                ConversionProfileId = "softwood-standard",
            };
        }

        /// <summary>
        /// C-4: wholesale 40u into the yard at 32c (the yard pays 1280c),
        /// so the yard's retail quote is 40c and the yard holds real stock
        /// it paid for. Returns the yard's remaining stock for the caller.
        /// </summary>
        private static void StockYard(World world, int units = 40)
        {
            Assert.IsNull(world.Mill.LumberStock.ReceiveLot(DocumentedMillLot(9301, units), world.Diagnostics));
            SawmillToYardTransferJob job = world.TransferDriver.BeginTransfer(
                EntityId.For(EntityKind.Lot, 9301).ToString(), "merchantable",
                SawmillToYardTransferDriver.WholesaleLumberUnitCostCents, Day, out string beginRefusal);
            Assert.IsNotNull(job);
            Assert.IsNull(beginRefusal);
            Assert.IsTrue(world.TransferDriver.RunTransfer(job.JobId, Day + 10, out string refusal));
            Assert.IsNull(refusal);
            Assert.AreEqual(units, world.Yard.LumberStock.TotalLumberUnits);
        }

        private static LumberYardSaleRecord SellRetail(World world, int units)
        {
            int price = world.Yard.QuoteRetailLumberPrice(null, "merchantable", PricedPolicy(), world.Diagnostics);
            Assert.AreEqual(40, price, "32c landed x 1.25 markup = 40c retail.");
            LumberYardSaleRecord sale = world.Yard.TrySellLumberRetail(
                "carpenter-shop", units, null, "merchantable", price, Day, world.Diagnostics);
            Assert.IsNotNull(sale, string.Join("; ", world.Diagnostics));
            Assert.AreEqual(units, sale.UnitsSoldFor(ConstructionResourceKind.Lumber));
            return sale;
        }

        private static LumberYardRetailDeliveryRequest MakeRequest(World world, LumberYardSaleRecord sale, int miles = 6)
        {
            return new LumberYardRetailDeliveryRequest
            {
                SaleId = sale.SaleId,
                DestinationLabel = "carpenter-shop",
                DestinationBusinessInstanceId = "carpenter-shop",
                Miles = miles,
                TransitGameSeconds = 10000f,
                ConditionMultiplier = 0f,
                YardDelivers = true,
                CarrierLabel = "freight-co",
                BuyerProvidesUnloading = false,
                BuyerBusinessId = "carpenter-shop",
                ReceiveShipment = world.ReceiveShipment,
            };
        }

        private static LumberYardRetailDeliveryExecution MakeExecution(World world)
        {
            return new LumberYardRetailDeliveryExecution
            {
                RateCard = world.RateCard,
                Wagon = world.Wagon,
                BuyerCash = world.BuyerCash,
                CarrierCash = world.CarrierCash,
                LoaderId = EntityId.For(EntityKind.Person, 41),
                DriverId = EntityId.For(EntityKind.Person, 42),
                DraftAnimalIds = new List<EntityId> { EntityId.For(EntityKind.Animal, 1) },
                UnloaderId = EntityId.For(EntityKind.Person, 43),
                Tasks = world.Tasks,
                Budgets = world.Budgets,
                Schedules = world.Schedules,
            };
        }

        private static LumberYardRetailDelivery NewService(World world)
        {
            return new LumberYardRetailDelivery(world.Yard, world.Freight, world.Diagnostics);
        }

        private static LumberYardDeliveryOrder OrderForSale(World world, LumberYardSaleRecord sale)
        {
            foreach (LumberYardDeliveryOrder order in world.Yard.DeliveryRegister.Orders)
            {
                if (order != null && order.SaleId.Equals(sale.SaleId)) return order;
            }
            return null;
        }

        [Test]
        public void C6_EndToEnd_OneTrip_SettlesDeliveryChargeOnce()
        {
            World world = NewWorld();
            StockYard(world);
            LumberYardSaleRecord sale = SellRetail(world, 28);
            int yardCashBefore = world.YardCash.BalanceCents;

            string refusal = NewService(world).ExecuteRetailDelivery(
                sale, MakeRequest(world, sale), MakeExecution(world), DeliveryDay, world.Diagnostics);

            Assert.IsNull(refusal, string.Join("; ", world.Diagnostics));
            LumberYardDeliveryOrder order = OrderForSale(world, sale);
            Assert.IsNotNull(order);
            Assert.IsTrue(order.IsDelivered);
            Assert.AreEqual(SixMileTripChargeCents, order.ChargeCents, "The recorded charge IS the DEF-016 quote.");
            Assert.AreEqual(SixMileTripChargeCents, order.SettledDeliveryCents);
            Assert.AreEqual(1, order.ShipmentIds.Count);
            Assert.AreEqual(1, order.TripFaresPaidCents.Count);
            Assert.AreEqual(SixMileTripChargeCents, order.TripFaresPaidCents[0]);
            Assert.AreEqual(10000 - SixMileTripChargeCents, world.BuyerCash.BalanceCents);
            Assert.AreEqual(SixMileTripChargeCents, world.CarrierCash.BalanceCents);
            Assert.AreEqual(yardCashBefore, world.YardCash.BalanceCents,
                "The service settles the DELIVERY charge only — the goods money is the buyer-side flow's job.");
            Assert.AreEqual(28, world.ReceivedUnits, "The receiver took custody of exactly the trip's units.");
            Assert.AreEqual(12, world.Yard.LumberStock.TotalLumberUnits, "The sale withdrew 28u; delivery moves custody, not stock.");
            Assert.AreEqual(1, world.Freight.TrackedShipments.Count);
            Assert.AreEqual(LogisticsShipmentStatus.Completed, world.Freight.TrackedShipments[0].Status);
            Assert.AreEqual("ydship-0", world.Freight.TrackedShipments[0].ShipmentId);
        }

        [Test]
        public void C6_MultiTrip_FortyUnits_TwoShipments_ChargeTwice()
        {
            World world = NewWorld();
            StockYard(world);
            LumberYardSaleRecord sale = SellRetail(world, 40);

            string refusal = NewService(world).ExecuteRetailDelivery(
                sale, MakeRequest(world, sale), MakeExecution(world), DeliveryDay, world.Diagnostics);

            Assert.IsNull(refusal, string.Join("; ", world.Diagnostics));
            LumberYardDeliveryOrder order = OrderForSale(world, sale);
            Assert.IsNotNull(order);
            Assert.IsTrue(order.IsDelivered);
            Assert.AreEqual(2 * SixMileTripChargeCents, order.ChargeCents);
            Assert.AreEqual(2 * SixMileTripChargeCents, order.SettledDeliveryCents);
            Assert.AreEqual(2, order.ShipmentIds.Count);
            Assert.AreEqual(2, order.TripFaresPaidCents.Count);
            Assert.AreEqual(SixMileTripChargeCents, order.TripFaresPaidCents[0]);
            Assert.AreEqual(SixMileTripChargeCents, order.TripFaresPaidCents[1]);
            Assert.AreEqual(10000 - 2 * SixMileTripChargeCents, world.BuyerCash.BalanceCents);
            Assert.AreEqual(2 * SixMileTripChargeCents, world.CarrierCash.BalanceCents);
            Assert.AreEqual(40, world.ReceivedUnits);
            Assert.AreEqual(0, world.Yard.LumberStock.TotalLumberUnits);
            Assert.AreEqual(2, world.Freight.TrackedShipments.Count);
            Assert.AreEqual("ydship-0", world.Freight.TrackedShipments[0].ShipmentId);
            Assert.AreEqual("ydship-1", world.Freight.TrackedShipments[1].ShipmentId);
        }

        [Test]
        public void C6_BuyerCannotPayDeliveryCharge_WaitsLoudly_RetryAfterFundingSucceeds()
        {
            World world = NewWorld();
            StockYard(world);
            LumberYardSaleRecord sale = SellRetail(world, 28);
            world.BuyerCash.SetBalance(10);
            var service = NewService(world);

            string refusal = service.ExecuteRetailDelivery(
                sale, MakeRequest(world, sale), MakeExecution(world), DeliveryDay, world.Diagnostics);

            Assert.IsNotNull(refusal);
            Assert.IsTrue(string.Join(" ", world.Diagnostics).Contains("cannot pay")
                || refusal.Contains("cannot pay") || refusal.Contains("holds 10c"),
                "The refusal names the shortfall: " + refusal);
            LumberYardDeliveryOrder order = OrderForSale(world, sale);
            Assert.IsNotNull(order, "The obligation is recorded — the retry reuses it.");
            Assert.IsTrue(!order.IsDelivered);
            Assert.AreEqual(0, order.SettledDeliveryCents);
            Assert.AreEqual(0, order.ShipmentIds.Count, "No freight is created for a buyer who cannot pay.");
            Assert.AreEqual(0, world.Freight.TrackedShipments.Count);
            Assert.AreEqual(10, world.BuyerCash.BalanceCents);
            Assert.AreEqual(0, world.CarrierCash.BalanceCents);
            Assert.AreEqual(12, world.Yard.LumberStock.TotalLumberUnits, "The lumber stays at the yard.");

            world.BuyerCash.SetBalance(10000);
            Assert.IsNull(service.ExecuteRetailDelivery(
                sale, MakeRequest(world, sale), MakeExecution(world), DeliveryDay + 1, world.Diagnostics),
                string.Join("; ", world.Diagnostics));
            Assert.IsTrue(order.IsDelivered);
            Assert.AreEqual(SixMileTripChargeCents, order.SettledDeliveryCents, "The delivery charge settles once, on the retry.");
            Assert.AreEqual(10000 - SixMileTripChargeCents, world.BuyerCash.BalanceCents);
            Assert.AreEqual(SixMileTripChargeCents, world.CarrierCash.BalanceCents);
            Assert.AreEqual(28, world.ReceivedUnits);
        }

        [Test]
        public void C6_NoStock_PriceIsMinusOne_AndQuoteRefuses()
        {
            World world = NewWorld();
            Assert.AreEqual(-1, world.Yard.QuoteRetailLumberPrice(null, "merchantable", PricedPolicy(), world.Diagnostics),
                "No stock, no price — never a guessed price.");

            Assert.IsTrue(!LumberYardRetailDelivery.TryQuoteDeliveryCharge(
                0, 6f, world.RateCard, world.Wagon, 0f, out _, out string reason));
            Assert.IsNotNull(reason);
        }

        [Test]
        public void C6_ConstructionSale_Refused()
        {
            World world = NewWorld();
            StockYard(world);
            LumberYardSaleRecord sale = world.Yard.TrySellToConstructionProject(
                "proj-1", 10, 40, 0, 0, Day, world.Diagnostics);
            Assert.IsNotNull(sale);
            Assert.IsTrue(sale.IsConstructionSale);

            string refusal = NewService(world).ExecuteRetailDelivery(
                sale, MakeRequest(world, sale), MakeExecution(world), DeliveryDay, world.Diagnostics);

            Assert.IsNotNull(refusal);
            Assert.IsTrue(refusal.Contains("ConstructionMaterialFreightCoordinator"),
                "The refusal names the construction freight path: " + refusal);
            Assert.IsNull(OrderForSale(world, sale), "A refused construction sale leaves no retail delivery order.");
            Assert.AreEqual(0, world.Freight.TrackedShipments.Count);
        }

        [Test]
        public void C6_UnknownSale_Refused()
        {
            World world = NewWorld();
            StockYard(world);
            var phantom = new LumberYardSaleRecord
            {
                SaleId = EntityId.For(EntityKind.Contract, 9999),
                BuyerLabel = "carpenter-shop",
                DayIndex = Day,
            };
            phantom.Lines.Add(new LumberYardMaterialSaleLine
            {
                ResourceKind = ConstructionResourceKind.Lumber,
                UnitsSold = 10,
                UnitPriceCents = 40,
            });

            string refusal = NewService(world).ExecuteRetailDelivery(
                phantom, MakeRequest(world, phantom), MakeExecution(world), DeliveryDay, world.Diagnostics);

            Assert.IsNotNull(refusal);
            Assert.IsTrue(refusal.Contains("never recorded"),
                "Deliveries fulfill real sales, never thin air: " + refusal);
            Assert.AreEqual(0, world.Freight.TrackedShipments.Count);
        }

        [Test]
        public void C6_NoRateCard_Refused()
        {
            World world = NewWorld();
            StockYard(world);
            LumberYardSaleRecord sale = SellRetail(world, 28);
            var execution = MakeExecution(world);
            execution.RateCard = null;

            string refusal = NewService(world).ExecuteRetailDelivery(
                sale, MakeRequest(world, sale), execution, DeliveryDay, world.Diagnostics);

            Assert.IsNotNull(refusal);
            Assert.IsTrue(refusal.Contains("rate card"), refusal);
            Assert.AreEqual(0, world.Freight.TrackedShipments.Count);
            Assert.AreEqual(10000, world.BuyerCash.BalanceCents);
        }

        [Test]
        public void C6_NoWagon_Refused()
        {
            World world = NewWorld();
            StockYard(world);
            LumberYardSaleRecord sale = SellRetail(world, 28);
            var execution = MakeExecution(world);
            execution.Wagon = null;

            string refusal = NewService(world).ExecuteRetailDelivery(
                sale, MakeRequest(world, sale), execution, DeliveryDay, world.Diagnostics);

            Assert.IsNotNull(refusal);
            Assert.IsTrue(refusal.Contains("wagon"), refusal);
            Assert.AreEqual(0, world.Freight.TrackedShipments.Count);
        }

        [Test]
        public void C6_NoCarrier_Refused()
        {
            World world = NewWorld();
            StockYard(world);
            LumberYardSaleRecord sale = SellRetail(world, 28);
            var request = MakeRequest(world, sale);
            request.CarrierLabel = string.Empty;

            string refusal = NewService(world).ExecuteRetailDelivery(
                sale, request, MakeExecution(world), DeliveryDay, world.Diagnostics);

            Assert.IsNotNull(refusal);
            Assert.IsTrue(refusal.Contains("carrier"), refusal);
            Assert.AreEqual(0, world.Freight.TrackedShipments.Count);
        }

        [Test]
        public void C6_SecondExecute_Idempotent_AlreadyDeliveredIsNoOp()
        {
            World world = NewWorld();
            StockYard(world);
            LumberYardSaleRecord sale = SellRetail(world, 28);
            var service = NewService(world);
            Assert.IsNull(service.ExecuteRetailDelivery(
                sale, MakeRequest(world, sale), MakeExecution(world), DeliveryDay, world.Diagnostics));

            string second = service.ExecuteRetailDelivery(
                sale, MakeRequest(world, sale), MakeExecution(world), DeliveryDay + 1, world.Diagnostics);

            Assert.IsNull(second, "An already-delivered sale re-executes as a no-op, not a second charge.");
            Assert.AreEqual(10000 - SixMileTripChargeCents, world.BuyerCash.BalanceCents, "No second charge.");
            Assert.AreEqual(SixMileTripChargeCents, world.CarrierCash.BalanceCents);
            Assert.AreEqual(1, world.Freight.TrackedShipments.Count, "No second shipment.");
            Assert.AreEqual(28, world.ReceivedUnits);
        }

        [Test]
        public void C6_CreditRefusal_RefundsBuyerInFull_RetryChargesOnce()
        {
            World world = NewWorld();
            StockYard(world);
            LumberYardSaleRecord sale = SellRetail(world, 28);
            world.CarrierCash.RefuseCredit = true;
            var service = NewService(world);

            string refusal = service.ExecuteRetailDelivery(
                sale, MakeRequest(world, sale), MakeExecution(world), DeliveryDay, world.Diagnostics);

            Assert.IsNotNull(refusal);
            Assert.IsTrue(refusal.Contains("refunded"), "The refusal names the refund: " + refusal);
            Assert.AreEqual(10000, world.BuyerCash.BalanceCents, "The buyer is refunded the trip fare in full.");
            Assert.AreEqual(0, world.CarrierCash.BalanceCents);
            Assert.AreEqual(0, world.Freight.TrackedShipments.Count, "A payment refusal leaves no shipment behind.");
            LumberYardDeliveryOrder order = OrderForSale(world, sale);
            Assert.IsNotNull(order);
            Assert.IsTrue(!order.IsDelivered);
            Assert.AreEqual(0, order.SettledDeliveryCents, "The fare record is cleared for the retry.");

            world.CarrierCash.RefuseCredit = false;
            Assert.IsNull(service.ExecuteRetailDelivery(
                sale, MakeRequest(world, sale), MakeExecution(world), DeliveryDay + 1, world.Diagnostics),
                string.Join("; ", world.Diagnostics));
            Assert.IsTrue(order.IsDelivered);
            Assert.AreEqual(SixMileTripChargeCents, order.SettledDeliveryCents, "The delivery charge settles once.");
            Assert.AreEqual(10000 - SixMileTripChargeCents, world.BuyerCash.BalanceCents);
            Assert.AreEqual(SixMileTripChargeCents, world.CarrierCash.BalanceCents);
            Assert.AreEqual(28, world.ReceivedUnits);
            Assert.AreEqual(1, world.Freight.TrackedShipments.Count);
        }

        [Test]
        public void C6_BuyerHaulage_RecordsObligation_NoChargeNoFreightNoCash()
        {
            World world = NewWorld();
            StockYard(world);
            LumberYardSaleRecord sale = SellRetail(world, 28);
            var request = MakeRequest(world, sale);
            request.YardDelivers = false;

            string refusal = NewService(world).ExecuteRetailDelivery(
                sale, request, MakeExecution(world), DeliveryDay, world.Diagnostics);

            Assert.IsNull(refusal, string.Join("; ", world.Diagnostics));
            LumberYardDeliveryOrder order = OrderForSale(world, sale);
            Assert.IsNotNull(order);
            Assert.IsTrue(!order.YardDelivers);
            Assert.AreEqual(0, order.ChargeCents, "Buyer haulage carries a zero delivery charge.");
            Assert.AreEqual(0, order.SettledDeliveryCents);
            Assert.AreEqual(0, order.ShipmentIds.Count);
            Assert.AreEqual(0, world.Freight.TrackedShipments.Count, "The yard performs no delivery.");
            Assert.AreEqual(10000, world.BuyerCash.BalanceCents);
            Assert.AreEqual(0, world.CarrierCash.BalanceCents);
            Assert.AreEqual(0, world.ReceivedUnits, "Pickup is the buyer's act, not the yard's.");
        }

        [Test]
        public void C6_QuoteMatchesDef016_PerTripPricing()
        {
            World world = NewWorld();

            Assert.IsTrue(LumberYardRetailDelivery.TryQuoteDeliveryCharge(
                28, 6f, world.RateCard, world.Wagon, 0f, out FreightServiceQuote oneTrip, out string reason1));
            Assert.AreEqual(SixMileTripChargeCents, oneTrip.TotalPriceCents);
            Assert.AreEqual(1, oneTrip.TripCount);
            Assert.AreEqual(SixMileTripChargeCents, oneTrip.PerTripTotalCents);

            Assert.IsTrue(LumberYardRetailDelivery.TryQuoteDeliveryCharge(
                40, 6f, world.RateCard, world.Wagon, 0f, out FreightServiceQuote twoTrips, out string reason2));
            Assert.AreEqual(2 * SixMileTripChargeCents, twoTrips.TotalPriceCents);
            Assert.AreEqual(2, twoTrips.TripCount);

            Assert.IsTrue(!LumberYardRetailDelivery.TryQuoteDeliveryCharge(
                28, 0f, world.RateCard, world.Wagon, 0f, out _, out string zeroMiles));
            Assert.IsNotNull(zeroMiles);
        }

        [Test]
        public void C6_PolicyForQuote_CarriesExactCharge()
        {
            var policy = LumberYardDeliveryPolicy.FromQuotedCharge(SixMileTripChargeCents);
            Assert.AreEqual(SixMileTripChargeCents, policy.ComputeChargeCents(28, 6, false));
            Assert.AreEqual(SixMileTripChargeCents, policy.ComputeChargeCents(28, 6, true),
                "Buyer-provided unloading is recorded on the order but the wagon trip costs what it costs.");
            Assert.AreEqual(0, policy.PerUnitMileCents, "The card prices per trip, not per unit-mile — no second charge.");
            Assert.AreEqual(0, policy.BuyerUnloadDiscountCents);
        }

        [Test]
        public void C6_SaveLoad_RoundTrip_PreservesDeliveryRecord()
        {
            World world = NewWorld();
            StockYard(world);
            LumberYardSaleRecord sale = SellRetail(world, 28);
            Assert.IsNull(NewService(world).ExecuteRetailDelivery(
                sale, MakeRequest(world, sale), MakeExecution(world), DeliveryDay, world.Diagnostics));

            var restored = new LumberYardShopRuntime("yard-1", new EntityIdRegistry());
            restored.LoadFromSaveDto(world.Yard.CaptureSaveDto());

            LumberYardDeliveryOrder order = null;
            foreach (LumberYardDeliveryOrder candidate in restored.DeliveryRegister.Orders)
            {
                if (candidate != null && candidate.SaleId.Equals(sale.SaleId)) order = candidate;
            }
            Assert.IsNotNull(order, "The delivery order survives the yard's save round trip.");
            Assert.IsTrue(order.IsDelivered);
            Assert.AreEqual(SixMileTripChargeCents, order.ChargeCents);
            Assert.AreEqual(SixMileTripChargeCents, order.SettledDeliveryCents);
            Assert.AreEqual(1, order.ShipmentIds.Count);
            Assert.AreEqual("ydship-0", order.ShipmentIds[0]);
            Assert.AreEqual(1, order.TripFaresPaidCents.Count);
            Assert.AreEqual(SixMileTripChargeCents, order.TripFaresPaidCents[0]);
        }

        [Test]
        public void C6_ReceiverRefuses_CargoHeld_OrderPaidNotDelivered_RetryRefusesLoudly()
        {
            World world = NewWorld();
            StockYard(world);
            LumberYardSaleRecord sale = SellRetail(world, 28);
            world.RefuseReceiving = true;
            var service = NewService(world);

            string refusal = service.ExecuteRetailDelivery(
                sale, MakeRequest(world, sale), MakeExecution(world), DeliveryDay, world.Diagnostics);

            Assert.IsNotNull(refusal, "A refused receiver must not produce a delivered order.");
            Assert.AreEqual(0, world.ReceivedUnits);
            LumberYardDeliveryOrder order = OrderForSale(world, sale);
            Assert.IsNotNull(order);
            Assert.IsTrue(!order.IsDelivered);
            Assert.AreEqual(SixMileTripChargeCents, order.SettledDeliveryCents,
                "The trip departed, so its fare stands for the attempt.");
            Assert.AreEqual(10000 - SixMileTripChargeCents, world.BuyerCash.BalanceCents);
            Assert.AreEqual(SixMileTripChargeCents, world.CarrierCash.BalanceCents);
            Assert.AreEqual(1, world.Freight.TrackedShipments.Count);
            Assert.AreEqual(LogisticsShipmentStatus.Failed, world.Freight.TrackedShipments[0].Status,
                "The trip failed in transit: the shipment is abandoned as Failed with its cargo quarantined in wagon custody — never falsely delivered.");
            Assert.IsTrue(world.Freight.TrackedShipments[0].BlockedReason.Contains("STRANDED CARGO"),
                "The failure names the quarantined cargo: " + world.Freight.TrackedShipments[0].BlockedReason);

            world.RefuseReceiving = false;
            string retry = service.ExecuteRetailDelivery(
                sale, MakeRequest(world, sale), MakeExecution(world), DeliveryDay + 1, world.Diagnostics);
            Assert.IsNotNull(retry);
            Assert.IsTrue(retry.Contains("ydship-0") && retry.Contains("reconcile"),
                "The retry refuses loudly, naming the failed shipment: " + retry);
            Assert.AreEqual(1, world.Freight.TrackedShipments.Count, "No re-shipment of accounted-for goods.");
            Assert.AreEqual(0, world.ReceivedUnits);
        }

        [Test]
        public void C6_ZeroMiles_Refused()
        {
            World world = NewWorld();
            StockYard(world);
            LumberYardSaleRecord sale = SellRetail(world, 28);

            string refusal = NewService(world).ExecuteRetailDelivery(
                sale, MakeRequest(world, sale, 0), MakeExecution(world), DeliveryDay, world.Diagnostics);

            Assert.IsNotNull(refusal);
            Assert.IsTrue(refusal.Contains("positive distance") || refusal.Contains("zero miles"), refusal);
            Assert.IsNull(OrderForSale(world, sale), "A refused delivery leaves no order.");
            Assert.AreEqual(0, world.Freight.TrackedShipments.Count);
            Assert.AreEqual(10000, world.BuyerCash.BalanceCents);
        }
    }
}
