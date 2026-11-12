using System.Collections.Generic;
using LandLedgers.Economy.Businesses.GeneralStore;
using LandLedgers.Economy.Core;
using LandLedgers.Population;
using NUnit.Framework;

/// <summary>
/// Pass XL vertical slice, Item 2: one named SKU (flour) ordered off-map
/// for the General Store, through ExternalMarketAuthority, with a
/// seller-paid freight surcharge, arrival after transit, duplicate-safe
/// receipt, and a compensating atomic household sale.
///
/// The freight surcharge in these tests (250c on 1000c of goods) is an
/// EXPLICIT TEST PARAMETER, not canon: no freight rate exists in canon
/// or in ExternalMarketAuthority. Kennedy must approve a production
/// rate before any driver wires this service with a fixed rate.
///
/// The supplier here ("test-grain", a test counterparty) is likewise a
/// test fixture, not a canon supplier: no production counterparty is
/// registered in code at 2bd9cf1.
/// </summary>
[TestFixture]
public sealed class OffMapNamedSkuProcurementTests
{
    private sealed class FakeBusinessCash : IBusinessCashPort
    {
        public int Balance;
        public bool FailCredit;
        public bool FailDebit;

        public FakeBusinessCash(int startingBalance)
        {
            Balance = startingBalance;
        }

        public int BalanceCents => Balance;

        public string TryDebit(int amountCents, string reason)
        {
            if (FailDebit)
            {
                return "test: debit refused";
            }

            if (amountCents < 0 || amountCents > Balance)
            {
                return "test: insufficient business cash";
            }

            Balance -= amountCents;
            return null;
        }

        public string Credit(int amountCents, string reason)
        {
            if (FailCredit)
            {
                return "test: credit refused";
            }

            if (amountCents < 0)
            {
                return "test: negative credit";
            }

            Balance += amountCents;
            return null;
        }
    }

    private sealed class FakeTradeCredit : ITradeCreditPort
    {
        public List<string> CreatedIds = new List<string>();
        public List<int> CreatedPrincipals = new List<int>();
        public bool Fail;

        public string CreateTradeCredit(
            string debtor, string creditor, int principalCents, int dayIndex,
            string terms, string purpose, string agreementId, string sourceEconomicEventId)
        {
            if (Fail)
            {
                return null;
            }

            string id = $"test-obligation-{CreatedIds.Count + 1}";
            CreatedIds.Add(id);
            CreatedPrincipals.Add(principalCents);
            return id;
        }
    }

    private static ExternalMarketAuthority MarketWithGrain(int flourUnits, int transitDays)
    {
        var market = new ExternalMarketAuthority();
        var supplier = new ExternalCounterparty
        {
            CounterpartyId = "test-grain",
            DisplayName = "Test Grain Co.",
            InitialBudgetCents = 50000,
            TransitDays = transitDays,
        };
        supplier.SupplyUnits["flour"] = flourUnits;
        Assert.IsNull(market.RegisterCounterparty(supplier));
        return market;
    }

    private static HouseholdLedger FundedHousehold(int householdId, int balanceCents, int dayIndex)
    {
        var ledger = new HouseholdLedger(householdId);
        Assert.IsNull(ledger.RecordInflow(
            dayIndex, balanceCents, HouseholdIncomeSource.OtherDocumented,
            "test-funding", "test funding for procurement tests", "test"));
        return ledger;
    }

    [Test]
    public void Order_CreatesReceipt_DeductsFiniteSupply_AndCreatesNoStockYet()
    {
        var market = MarketWithGrain(200, 3);
        var cash = new FakeBusinessCash(0);
        var service = new OffMapNamedSkuProcurementService("general-store-1", market, cash);

        NamedSkuOrder order = service.PlaceOrder(
            "flour", 100, 10, 250, "test-grain", 10,
            NamedSkuPaymentMode.CashOnArrival, null);

        Assert.IsNotNull(order);
        Assert.IsTrue(order.ReceiptId.StartsWith("EXT-P-"));
        Assert.AreEqual(1250, order.TotalCostCents);
        Assert.AreEqual(1000, order.UnitsOrdered * order.UnitGoodsCostCents);
        Assert.AreEqual(250, order.FreightSurchargeCents);
        Assert.AreEqual(13, order.ExpectedArrivalDayIndex);
        Assert.AreEqual(NamedSkuOrderStage.Ordered, order.Stage);
        Assert.AreEqual(100, market.GetCounterparty("test-grain").SupplyUnits["flour"]);
        Assert.AreEqual(0, service.Lots.Count);
        Assert.AreEqual(0, service.SellableUnits("flour"));
        Assert.AreEqual(1250, market.TotalOutflowCents);
    }

    [Test]
    public void Order_BeyondFiniteSupply_IsRejectedWithNothingCreated()
    {
        var market = MarketWithGrain(200, 3);
        var cash = new FakeBusinessCash(0);
        var service = new OffMapNamedSkuProcurementService("general-store-1", market, cash);

        NamedSkuOrder order = service.PlaceOrder(
            "flour", 300, 10, 250, "test-grain", 10,
            NamedSkuPaymentMode.CashOnArrival, null);

        Assert.IsNull(order);
        Assert.AreEqual(0, service.Orders.Count);
        Assert.AreEqual(200, market.GetCounterparty("test-grain").SupplyUnits["flour"]);
    }

    [Test]
    public void Order_TradeCreditWithoutPort_IsRejectedWithNoObligationFaked()
    {
        var market = MarketWithGrain(200, 3);
        var cash = new FakeBusinessCash(0);
        var service = new OffMapNamedSkuProcurementService("general-store-1", market, cash);

        NamedSkuOrder order = service.PlaceOrder(
            "flour", 10, 10, 25, "test-grain", 10,
            NamedSkuPaymentMode.TradeCredit, null);

        Assert.IsNull(order);
        Assert.AreEqual(0, service.Orders.Count);
    }

    [Test]
    public void Receipt_NoLotBeforeArrivalDay_OneLotOnArrivalDay()
    {
        var market = MarketWithGrain(200, 3);
        var cash = new FakeBusinessCash(5000);
        var service = new OffMapNamedSkuProcurementService("general-store-1", market, cash);
        NamedSkuOrder order = service.PlaceOrder(
            "flour", 100, 10, 250, "test-grain", 10,
            NamedSkuPaymentMode.CashOnArrival, null);

        Assert.AreEqual(0, service.ReceiveArrivals(12, null).Count);
        Assert.AreEqual(0, service.Lots.Count);

        List<NamedSkuLot> created = service.ReceiveArrivals(13, null);
        Assert.AreEqual(1, created.Count);
        Assert.AreEqual(100, created[0].RemainingUnits);
        Assert.AreEqual($"lot-{order.ReceiptId}", created[0].LotId);
        Assert.AreEqual(NamedSkuOrderStage.Received, order.Stage);
        Assert.AreEqual(5000 - 1250, cash.Balance);
        Assert.AreEqual(100, service.SellableUnits("flour"));
    }

    [Test]
    public void Receipt_DuplicateCallCreatesNoSecondLot()
    {
        var market = MarketWithGrain(200, 3);
        var cash = new FakeBusinessCash(5000);
        var service = new OffMapNamedSkuProcurementService("general-store-1", market, cash);
        service.PlaceOrder("flour", 100, 10, 250, "test-grain", 10,
            NamedSkuPaymentMode.CashOnArrival, null);

        Assert.AreEqual(1, service.ReceiveArrivals(13, null).Count);
        Assert.AreEqual(0, service.ReceiveArrivals(13, null).Count);
        Assert.AreEqual(0, service.ReceiveArrivals(20, null).Count);
        Assert.AreEqual(1, service.Lots.Count);
        Assert.AreEqual(100, service.SellableUnits("flour"));
    }

    [Test]
    public void Receipt_WaitingPaymentCreatesNoLot_AndRetriesSuccessfully()
    {
        var market = MarketWithGrain(200, 3);
        var cash = new FakeBusinessCash(0);
        var service = new OffMapNamedSkuProcurementService("general-store-1", market, cash);
        NamedSkuOrder order = service.PlaceOrder(
            "flour", 100, 10, 250, "test-grain", 10,
            NamedSkuPaymentMode.CashOnArrival, null);

        Assert.AreEqual(0, service.ReceiveArrivals(13, null).Count);
        Assert.AreEqual(NamedSkuOrderStage.WaitingPayment, order.Stage);
        Assert.AreEqual(0, service.Lots.Count);

        cash.Balance = 5000;
        Assert.AreEqual(1, service.ReceiveArrivals(14, null).Count);
        Assert.AreEqual(NamedSkuOrderStage.Received, order.Stage);
        Assert.AreEqual(100, service.SellableUnits("flour"));
        Assert.AreEqual(5000 - 1250, cash.Balance);
    }

    [Test]
    public void Receipt_TradeCreditCreatesRealObligationBeforeLot()
    {
        var market = MarketWithGrain(200, 3);
        var cash = new FakeBusinessCash(0);
        var credit = new FakeTradeCredit();
        var service = new OffMapNamedSkuProcurementService("general-store-1", market, cash);
        NamedSkuOrder order = service.PlaceOrder(
            "flour", 100, 10, 250, "test-grain", 10,
            NamedSkuPaymentMode.TradeCredit, credit);

        Assert.AreEqual(1, service.ReceiveArrivals(13, credit).Count);
        Assert.AreEqual(1, credit.CreatedIds.Count);
        Assert.AreEqual(1250, credit.CreatedPrincipals[0]);
        Assert.AreEqual(credit.CreatedIds[0], order.ObligationId);
        Assert.AreEqual(0, cash.Balance);
        Assert.AreEqual(100, service.SellableUnits("flour"));
    }

    [Test]
    public void Receipt_TradeCreditFailureCreatesNoLot()
    {
        var market = MarketWithGrain(200, 3);
        var cash = new FakeBusinessCash(0);
        var credit = new FakeTradeCredit { Fail = true };
        var service = new OffMapNamedSkuProcurementService("general-store-1", market, cash);
        NamedSkuOrder order = service.PlaceOrder(
            "flour", 100, 10, 250, "test-grain", 10,
            NamedSkuPaymentMode.TradeCredit, credit);

        Assert.AreEqual(0, service.ReceiveArrivals(13, credit).Count);
        Assert.AreEqual(NamedSkuOrderStage.WaitingObligation, order.Stage);
        Assert.AreEqual(0, service.Lots.Count);
    }

    [Test]
    public void Sale_ConsumesFifoLots_PreservesLotIds_AndSettlesCash()
    {
        var market = MarketWithGrain(500, 1);
        var cash = new FakeBusinessCash(10000);
        var service = new OffMapNamedSkuProcurementService("general-store-1", market, cash);
        service.PlaceOrder("flour", 100, 10, 250, "test-grain", 10,
            NamedSkuPaymentMode.CashOnArrival, null);
        service.PlaceOrder("flour", 100, 10, 250, "test-grain", 10,
            NamedSkuPaymentMode.CashOnArrival, null);
        service.ReceiveArrivals(11, null);
        Assert.AreEqual(2, service.Lots.Count);

        var household = FundedHousehold(7, 10000, 11);
        NamedSkuSale sale = service.SellToHousehold(
            household, 7, 1, "household:7", "flour", 150, 20, 11);

        Assert.IsNotNull(sale);
        Assert.AreEqual(2, sale.ConsumedLotIds.Count);
        Assert.AreEqual(service.Lots[0].LotId, sale.ConsumedLotIds[0]);
        Assert.AreEqual(service.Lots[1].LotId, sale.ConsumedLotIds[1]);
        Assert.AreEqual(0, service.Lots[0].RemainingUnits);
        Assert.AreEqual(50, service.Lots[1].RemainingUnits);
        Assert.AreEqual(50, service.SellableUnits("flour"));
        Assert.AreEqual(10000 - 3000, household.GetBalanceCents());
        Assert.AreEqual(10000 - 2500 + 3000, cash.Balance);
        Assert.IsTrue(sale.HouseholdLotId.StartsWith("hh-lot-"));
        Assert.IsTrue(sale.Provenance.Contains(service.Lots[0].LotId));
    }

    [Test]
    public void Sale_InsufficientHouseholdCashMovesNothing()
    {
        var market = MarketWithGrain(200, 1);
        var cash = new FakeBusinessCash(10000);
        var service = new OffMapNamedSkuProcurementService("general-store-1", market, cash);
        service.PlaceOrder("flour", 100, 10, 250, "test-grain", 10,
            NamedSkuPaymentMode.CashOnArrival, null);
        service.ReceiveArrivals(11, null);

        var household = FundedHousehold(7, 100, 11);
        NamedSkuSale sale = service.SellToHousehold(
            household, 7, 1, "household:7", "flour", 50, 20, 11);

        Assert.IsNull(sale);
        Assert.AreEqual(100, service.SellableUnits("flour"));
        Assert.AreEqual(100, household.GetBalanceCents());
        Assert.AreEqual(10000 - 1250, cash.Balance);
    }

    [Test]
    public void Sale_BusinessCreditFailureMovesNothing()
    {
        var market = MarketWithGrain(200, 1);
        var cash = new FakeBusinessCash(10000) { FailCredit = true };
        var service = new OffMapNamedSkuProcurementService("general-store-1", market, cash);
        service.PlaceOrder("flour", 100, 10, 250, "test-grain", 10,
            NamedSkuPaymentMode.CashOnArrival, null);
        service.ReceiveArrivals(11, null);

        var household = FundedHousehold(7, 10000, 11);
        NamedSkuSale sale = service.SellToHousehold(
            household, 7, 1, "household:7", "flour", 50, 20, 11);

        Assert.IsNull(sale);
        Assert.AreEqual(100, service.SellableUnits("flour"));
        Assert.AreEqual(10000, household.GetBalanceCents());
    }

    [Test]
    public void Sale_InsufficientStockMovesNothing()
    {
        var market = MarketWithGrain(200, 1);
        var cash = new FakeBusinessCash(10000);
        var service = new OffMapNamedSkuProcurementService("general-store-1", market, cash);
        service.PlaceOrder("flour", 100, 10, 250, "test-grain", 10,
            NamedSkuPaymentMode.CashOnArrival, null);
        service.ReceiveArrivals(11, null);

        var household = FundedHousehold(7, 10000, 11);
        NamedSkuSale sale = service.SellToHousehold(
            household, 7, 1, "household:7", "flour", 101, 20, 11);

        Assert.IsNull(sale);
        Assert.AreEqual(100, service.SellableUnits("flour"));
        Assert.AreEqual(10000, household.GetBalanceCents());
    }

    [Test]
    public void OffMapDeliveredUnitPriceExceedsOnMapByExactlyTheFreightSurcharge()
    {
        // On-map unit price = goods unit price (no freight). Off-map
        // delivered unit price = (goods + freight) / units. The 250c
        // freight on 100u is a test parameter, not canon.
        var market = MarketWithGrain(200, 3);
        var cash = new FakeBusinessCash(0);
        var service = new OffMapNamedSkuProcurementService("general-store-1", market, cash);
        NamedSkuOrder order = service.PlaceOrder(
            "flour", 100, 10, 250, "test-grain", 10,
            NamedSkuPaymentMode.CashOnArrival, null);

        Assert.AreEqual(10, order.UnitGoodsCostCents);
        Assert.AreEqual(12, order.TotalCostCents / order.UnitsOrdered);
        Assert.AreEqual(250, order.TotalCostCents - order.UnitsOrdered * order.UnitGoodsCostCents);
        Assert.IsTrue(order.Provenance.Contains("on-map unit price 10c"));
        Assert.IsTrue(order.Provenance.Contains("off-map delivered unit price 12c"));
    }

    [Test]
    public void RestoredMarketPreservesPendingArrivalFiniteStockAndReceiptIdentity()
    {
        var cash = new FakeBusinessCash(10000);
        var service = new OffMapNamedSkuProcurementService("store", MarketWithGrain(200, 14), cash);
        NamedSkuOrder first = service.PlaceOrder("flour", 10, 10, 0, "test-grain", 1,
            NamedSkuPaymentMode.CashOnArrival, null);
        Assert.AreEqual(1, service.ReceiveArrivals(15, null).Count);
        NamedSkuOrder pending = service.PlaceOrder("flour", 10, 10, 0, "test-grain", 16,
            NamedSkuPaymentMode.CashOnArrival, null);
        var dto = UnityEngine.JsonUtility.FromJson<NamedSkuProcurementSaveDto>(
            UnityEngine.JsonUtility.ToJson(service.ExportState()));
        var restoredMarket = MarketWithGrain(200, 14);
        var restored = new OffMapNamedSkuProcurementService("store", restoredMarket, cash);
        restored.ImportState(dto);
        Assert.AreEqual(180, restoredMarket.GetCounterparty("test-grain").SupplyUnits["flour"],
            "Loading must not reset finite stock already committed to purchases.");
        Assert.AreEqual(0, restored.ReceiveArrivals(29, null).Count);
        Assert.AreEqual(1, restored.ReceiveArrivals(30, null).Count);
        Assert.AreEqual(9800, cash.Balance);
        Assert.AreEqual(20, restored.SellableUnits("flour"));
        Assert.AreEqual(0, restored.ReceiveArrivals(30, null).Count);
        Assert.AreEqual(9800, cash.Balance, "Retry must not pay for the arrival twice.");
        NamedSkuOrder next = restored.PlaceOrder("flour", 10, 10, 0, "test-grain", 31,
            NamedSkuPaymentMode.CashOnArrival, null);
        Assert.IsNotNull(next);
        Assert.AreNotEqual(first.ReceiptId, next.ReceiptId);
        Assert.AreNotEqual(pending.ReceiptId, next.ReceiptId);
        Assert.AreEqual(170, restoredMarket.GetCounterparty("test-grain").SupplyUnits["flour"]);
    }

    [Test]
    public void ReboundProcurementDebitsOnlyTheRestoredBusinessCash()
    {
        var oldCash = new FakeBusinessCash(1000);
        var restoredCash = new FakeBusinessCash(1000);
        var service = new OffMapNamedSkuProcurementService("store", MarketWithGrain(20, 1), oldCash);
        service.PlaceOrder("flour", 10, 10, 0, "test-grain", 1, NamedSkuPaymentMode.CashOnArrival, null);
        service.RebindBusinessCash(restoredCash);
        Assert.AreEqual(1, service.ReceiveArrivals(2, null).Count);
        Assert.AreEqual(1000, oldCash.Balance);
        Assert.AreEqual(900, restoredCash.Balance);
        Assert.AreEqual(0, service.ReceiveArrivals(2, null).Count);
        Assert.AreEqual(900, restoredCash.Balance);
    }

    [Test]
    public void LegacyOrderReservesReceiptWithoutInventingItsMissingMarketArrival()
    {
        var cash = new FakeBusinessCash(10000);
        var service = new OffMapNamedSkuProcurementService("store", MarketWithGrain(200, 14), cash);
        NamedSkuOrder old = service.PlaceOrder("flour", 10, 10, 0, "test-grain", 1,
            NamedSkuPaymentMode.CashOnArrival, null);
        NamedSkuProcurementSaveDto legacy = service.ExportState();
        legacy.Market = null;
        var restored = new OffMapNamedSkuProcurementService("store", MarketWithGrain(200, 14), cash);
        restored.ImportState(legacy);
        Assert.AreEqual(0, restored.ReceiveArrivals(15, null).Count);
        Assert.AreEqual(10000, cash.Balance);
        Assert.AreEqual(0, restored.SellableUnits("flour"));
        NamedSkuOrder next = restored.PlaceOrder("flour", 10, 10, 0, "test-grain", 16,
            NamedSkuPaymentMode.CashOnArrival, null);
        Assert.IsNotNull(next);
        Assert.AreNotEqual(old.ReceiptId, next.ReceiptId);
        Assert.That(restored.Diagnostics, Has.Some.Contains("legacy procurement save"));
    }

    [Test]
    public void ExportImport_IsIdempotentAndPreservesLotsAndSales()
    {
        var market = MarketWithGrain(200, 1);
        var cash = new FakeBusinessCash(10000);
        var service = new OffMapNamedSkuProcurementService("general-store-1", market, cash);
        service.PlaceOrder("flour", 100, 10, 250, "test-grain", 10,
            NamedSkuPaymentMode.CashOnArrival, null);
        service.ReceiveArrivals(11, null);
        var household = FundedHousehold(7, 10000, 11);
        service.SellToHousehold(household, 7, 1, "household:7", "flour", 10, 20, 11);

        NamedSkuProcurementSaveDto dto = service.ExportState();
        var restored = new OffMapNamedSkuProcurementService("general-store-1", market, cash);
        restored.ImportState(dto);
        restored.ImportState(dto);

        Assert.AreEqual(1, restored.Orders.Count);
        Assert.AreEqual(1, restored.Lots.Count);
        Assert.AreEqual(1, restored.Sales.Count);
        Assert.AreEqual(1, restored.ReceivedReceiptIds.Count);
        Assert.AreEqual(90, restored.SellableUnits("flour"));
    }
}
