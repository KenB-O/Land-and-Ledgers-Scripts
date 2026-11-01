using System.Collections.Generic;
using LandLedgers.Economy.Businesses.GeneralStore;
using LandLedgers.Orchestration.Player;
using LandLedgers.Population;
using NUnit.Framework;

/// <summary>
/// Pass XL phase 6: the composed six-person slice run, not isolated pieces.
/// All numbers below are explicit test parameters for the run, never canon:
/// 100u flour at 10c goods + 250c seller-paid freight = 1250c, transit 3 days,
/// 40u sold at 20c = 800c, business cash 5000c input, household cash 10000c
/// input (prior earnings simulated; the fixture itself is 0 capital).
/// Meal actuals follow the default policy exactly: day 1 has 3.33 staple
/// days of supply, so midday is served (6 meals, 12 lb); days 2-3 fall below
/// the 3-day midday threshold, so only morning + evening are served
/// (4 meals, 8 lb each). Total: 14 meals, 0 missed, 28 lb eaten, 12 lb left.
/// </summary>
[TestFixture]
public sealed class PassXlPhase6SliceRunTests
{
    private static SixPersonSliceRunReport Run()
    {
        return SixPersonSliceRun.Execute(
            startDay: 0,
            businessCashStartCents: 5000,
            householdCashStartCents: 10000,
            orderUnits: 100,
            unitGoodsCostCents: 10,
            freightSurchargeCents: 250,
            marketSupplyUnits: 200,
            transitDays: 3,
            saleUnits: 40,
            saleUnitPriceCents: 20,
            mealDays: 3);
    }

    [Test]
    public void Run_EndToEnd_FlourToMeals_WithSaveRestore_NoDuplication()
    {
        SixPersonSliceRunReport report = Run();
        Assert.IsNotNull(report, "Composed run must succeed; see report diagnostics.");

        // Roster, in the fixture's order.
        CollectionAssert.AreEqual(new[] { 1, 2, 3, 4, 5, 6 }, report.PersonIds);
        CollectionAssert.AreEqual(new[] { 1, 2, 3 }, report.HouseholdIds);
        Assert.AreEqual("Kennedy Baldwin-Ooms", report.PersonNames[0]);
        Assert.AreEqual("Jacob Boniecki", report.PersonNames[4]);

        // Order, transit, arrival: no stock before arrival, one lot on day 3.
        Assert.IsTrue(report.OrderId.StartsWith("sku-po-general-store-1-0-"));
        Assert.IsTrue(report.ReceiptId.StartsWith("EXT-P-"));
        Assert.AreEqual(3, report.ArrivalDay);
        Assert.IsTrue(report.StoreLotId.StartsWith("lot-EXT-P-"));
        Assert.AreEqual(100, report.MarketSupplyRemainingUnits);
        Assert.AreEqual(1250, report.MarketOutflowCents);

        // Cash: business 5000 - 1250 procurement + 800 sale = 4550;
        // household 10000 - 800 sale = 9200. No double charge anywhere.
        Assert.AreEqual(5000, report.BusinessCashStartCents);
        Assert.AreEqual(3750, report.BusinessCashAfterProcurementCents);
        Assert.AreEqual(4550, report.BusinessCashAfterSaleCents);
        Assert.AreEqual(10000, report.HouseholdCashStartCents);
        Assert.AreEqual(9200, report.HouseholdCashAfterSaleCents);

        // Lots: store 100 - 40 sold = 60 remaining; household got 40.
        Assert.AreEqual(60, report.StoreLotRemainingUnits);
        Assert.AreEqual(60, report.SellableUnitsAfterSale);
        Assert.AreEqual(40, report.HouseholdFlourBeforeMeals);
        Assert.IsTrue(report.SaleId.StartsWith("sku-sale-general-store-1-3-"));
        Assert.GreaterOrEqual(report.HouseholdInventoryLotId, 0);

        // Save/restore: same counts, no duplication, re-delivery refused.
        Assert.IsTrue(report.SuppressMigrantArrivalsRestored);
        Assert.IsFalse(report.TreatPlayerAsNpcRestored);
        Assert.AreEqual(1, report.RestoredOrderCount);
        Assert.AreEqual(1, report.RestoredLotCount);
        Assert.AreEqual(1, report.RestoredSaleCount);
        Assert.AreEqual(1, report.RestoredDeliveredCount);
        Assert.IsTrue(report.RedeliveryWasAlreadyDelivered);
        Assert.AreEqual(1, report.RedeliveryInventoryLotCount);

        // Meals, on the restored inventory: 6 + 4 + 4 = 14 eaten, 0 missed.
        CollectionAssert.AreEqual(new[] { 6, 4, 4 }, report.MealsEatenPerDay);
        Assert.AreEqual(14, report.MealsEaten);
        Assert.AreEqual(0, report.MealsMissed);
        Assert.AreEqual(28, report.FlourConsumedByMeals);
        Assert.AreEqual(12, report.HouseholdFlourAfterMeals);
    }

    [Test]
    public void Run_Seams_AreNamed_AndOtherHouseholdsEatNothing()
    {
        SixPersonSliceRunReport report = Run();
        Assert.IsNotNull(report);

        // Every leg that cannot run in Roslyn is named, not silently skipped.
        string seams = string.Join(" | ", report.SeamsStopped);
        Assert.IsTrue(seams.Contains("Bakery production"));
        Assert.IsTrue(seams.Contains("Fuel Dealer production"));
        Assert.IsTrue(seams.Contains("Work and pay"));
        Assert.IsTrue(seams.Contains("Daily tick driver"));
        Assert.IsTrue(seams.Contains("Postal office"));
        Assert.IsTrue(seams.Contains("Supplier registration"));

        // Pierce and Boniecki households bought nothing in this run, so
        // the run proves no meals for them and invents no stock for them.
        // Their inventories are separate from the Baldwin-Ooms inventory
        // that the run actually delivered to.
        Assert.AreEqual(40, report.HouseholdFlourBeforeMeals);
        Assert.AreEqual(12, report.HouseholdFlourAfterMeals);
    }

    [Test]
    public void Run_SecondSaleBeyondRemainingStock_Refused_NothingMoves()
    {
        SixPersonSliceRunReport report = Run();
        Assert.IsNotNull(report);

        // Store has 60u left. A 61u sale must be refused, moving nothing:
        // no new sale, no cash movement, no lot change.
        int salesBefore = report.ProcurementService.Sales.Count;
        int businessBefore = report.ProcurementService.Lots[0].RemainingUnits;
        int householdBefore = report.BaldwinOomsLedger.GetBalanceCents();
        NamedSkuSale refused = report.ProcurementService.SellToHousehold(
            report.BaldwinOomsLedger, 1, 1, "person:1", "flour", 61, 20, report.ArrivalDay);
        Assert.IsNull(refused);
        Assert.AreEqual(salesBefore, report.ProcurementService.Sales.Count);
        Assert.AreEqual(businessBefore, report.ProcurementService.Lots[0].RemainingUnits);
        Assert.AreEqual(householdBefore, report.BaldwinOomsLedger.GetBalanceCents());
    }

    [Test]
    public void Run_ZeroMealDays_FlourRemains_AndCashStillReconciles()
    {
        SixPersonSliceRunReport report = SixPersonSliceRun.Execute(
            startDay: 0,
            businessCashStartCents: 5000,
            householdCashStartCents: 10000,
            orderUnits: 100,
            unitGoodsCostCents: 10,
            freightSurchargeCents: 250,
            marketSupplyUnits: 200,
            transitDays: 3,
            saleUnits: 40,
            saleUnitPriceCents: 20,
            mealDays: 0);
        Assert.IsNotNull(report);
        Assert.AreEqual(0, report.MealsEaten);
        Assert.AreEqual(0, report.MealsMissed);
        Assert.AreEqual(40, report.HouseholdFlourAfterMeals);
        Assert.AreEqual(0, report.FlourConsumedByMeals);
        Assert.AreEqual(4550, report.BusinessCashAfterSaleCents);
        Assert.AreEqual(9200, report.HouseholdCashAfterSaleCents);
    }
}
