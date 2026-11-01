using System;
using System.Collections.Generic;
using LandLedgers.Economy.Businesses.GeneralStore;
using LandLedgers.Economy.Core;
using LandLedgers.Persistence;
using LandLedgers.Population;

namespace LandLedgers.Orchestration.Player
{
    /// <summary>
    /// Pass XL phase 6: a composed, Roslyn-testable run of the six-person
    /// vertical slice, using ONLY pieces phases 1-5 already proved in
    /// isolation. This is a slice-run orchestrator, NOT a production
    /// driver and NOT a Main Scene tick driver. It calls the steps
    /// explicitly, in order, so a composed run can be asserted in Roslyn.
    /// Codex must still wire a real daily tick driver in Main Scene;
    /// nothing here runs on a schedule, and nothing here teleports,
    /// grants, or auto-completes work.
    ///
    /// WHO IS WHO (fixture labels, not production ids):
    /// The six-person fixture names Kennedy Baldwin-Ooms (35) and Emma
    /// Baldwin-Ooms (32) in household Baldwin-Ooms, Glen Pierce (31) and
    /// Cassidy Pierce (29) in household Pierce, and Jacob Boniecki (34)
    /// and Grace Boniecki (30) in household Boniecki. The run uses person
    /// numbers 1-6 in that order and household numbers 1-3 in that
    /// order as ledger/inventory ids, matching the fixture's person
    /// order (P001, P003, P005 are the three founders). Production
    /// PopulationState ids are NOT proved by this run: PopulationGenerator
    /// is Unity-bound and is not run here, so Codex must confirm the
    /// fixture ids match production ids before claiming this run is the
    /// Main Scene slice.
    ///
    /// CASH IS A RUN INPUT, NOT SLICE EARNINGS (read this first):
    /// The fixture gives the three businesses 0 starting capital and no
    /// granted inventory or cash. This run therefore does NOT claim the
    /// store or the households earned their opening cash inside the
    /// slice. Business cash and household cash are explicit run inputs,
    /// booked as named OtherDocumented inflows with a source reference
    /// that says so, simulating prior earnings. The work-and-pay leg
    /// that would actually earn that cash inside the slice (wage payroll
    /// via EmploymentRelationship for General Store / Bakery / Fuel
    /// Dealer work) has no Roslyn-usable driver in this run, and is
    /// recorded as a seam, not faked. Pass 7 is the place to close it
    /// if a payroll path can be wired safely.
    ///
    /// WHAT RUNS, IN ORDER:
    ///   Day start: off-map flour order (CashOnArrival, seller-paid
    ///     freight surcharge, finite test supplier, transit days).
    ///   Before arrival: no lot, no stock, no sale is possible.
    ///   Arrival day: lot created, business cash debited, duplicate
    ///     receipt creates no second lot.
    ///   Sale day (same day): atomic sell-and-deliver to the Baldwin-Ooms
    ///     household inventory (phase 2 path), cash settled once.
    ///   Save/restore: slice DTO captured (suppress = true, NPC = false),
    ///     restored into a fresh service and a fresh inventory rebuilt
    ///     from saved lots (the phase 2 pattern). Re-delivery of the
    ///     same sale returns AlreadyDelivered and adds nothing.
    ///   Meal days: HouseholdMealService serves the Baldwin-Ooms
    ///     household from the RESTORED inventory, with the default meal
    ///     policy. Flour is a real meal item (2 lb per serving, 5th in
    ///     meal priority), so meals consume real lots, never abstract
    ///     reserve units.
    ///
    /// WHAT STOPS AT A SEAM (recorded in Report.SeamsStopped, never
    /// faked):
    ///   - Bakery production: staged chain has no Main Scene owner/driver,
    ///     no BreadShelf-to-SKU transfer, no flour feed (phase 4 F16/F17).
    ///     No bread is produced, and the Pierce household eats no bakery
    ///     bread in this run.
    ///   - Fuel Dealer production: no Task to tree depletion to wood lot
    ///     to sale path. No fuel is produced or sold.
    ///   - Work and pay: see cash paragraph above.
    ///   - Daily tick driver: run steps are called explicitly here.
    ///   - Residence assignment: meals use a HouseholdState id and member
    ///     list directly, not a HousingResidence record.
    ///   - Postal office: no recruitment letter is posted in this run;
    ///     office registration remains Codex-only (phase 4 F04).
    ///   - Supplier registration: the supplier here is a test
    ///     counterparty, not a production supplier (phase 4 F37).
    ///   - Business cash: the run cash port is in-memory; production
    ///     maps to BusinessRuntimeState and must be registered with
    ///     SaveLoadManager (phase 4 F19).
    ///   - Other households' meals: Pierce and Boniecki households buy
    ///     no flour in this run, so no meals are proven for them. Their
    ///     meal leg stops at "no supply", it is not fed by invented stock.
    ///
    /// All prices, freight, supply, transit, and cash inputs are
    /// caller-supplied test parameters, never canon rates.
    /// </summary>
    public sealed class SixPersonSliceRunReport
    {
        public List<string> PersonNames = new List<string>
        {
            "Kennedy Baldwin-Ooms", "Emma Baldwin-Ooms",
            "Glen Pierce", "Cassidy Pierce",
            "Jacob Boniecki", "Grace Boniecki",
        };
        public List<int> PersonIds = new List<int> { 1, 2, 3, 4, 5, 6 };
        public List<int> HouseholdIds = new List<int> { 1, 2, 3 };
        public List<string> BusinessLabels = new List<string>
        {
            "Baldwin-Ooms General Store", "Pierce Bakery", "Boniecki Fuel Dealer",
        };
        public string OrderId = string.Empty;
        public string ReceiptId = string.Empty;
        public int OrderDay;
        public int ArrivalDay;
        public string StoreLotId = string.Empty;
        public string SaleId = string.Empty;
        public int HouseholdInventoryLotId = -1;
        public int BusinessCashStartCents;
        public int BusinessCashAfterProcurementCents;
        public int BusinessCashAfterSaleCents;
        public int HouseholdCashStartCents;
        public int HouseholdCashAfterSaleCents;
        public int StoreLotRemainingUnits;
        public int SellableUnitsAfterSale;
        public int HouseholdFlourBeforeMeals;
        public int HouseholdFlourAfterMeals;
        public int FlourConsumedByMeals;
        public int MealsEaten;
        public int MealsMissed;
        public List<int> MealsEatenPerDay = new List<int>();
        public int MarketSupplyRemainingUnits;
        public int MarketOutflowCents;
        public bool SuppressMigrantArrivalsRestored;
        public bool TreatPlayerAsNpcRestored;
        public int RestoredOrderCount;
        public int RestoredLotCount;
        public int RestoredSaleCount;
        public int RestoredDeliveredCount;
        public bool RedeliveryWasAlreadyDelivered;
        public int RedeliveryInventoryLotCount;
        public List<string> SeamsStopped = new List<string>();
        public List<string> Diagnostics = new List<string>();

        // Live objects, exposed so tests can probe follow-up steps
        // (e.g. a refused second sale) without re-running the slice.
        public OffMapNamedSkuProcurementService ProcurementService;
        public HouseholdLedger BaldwinOomsLedger;
        public HouseholdInventory BaldwinOomsInventory;
        public OffMapNamedSkuProcurementService RestoredService;
        public HouseholdInventory RestoredInventory;
    }

    public static class SixPersonSliceRun
    {
        private sealed class RunBusinessCash : IBusinessCashPort
        {
            public int Balance;

            public RunBusinessCash(int startingBalance)
            {
                Balance = startingBalance;
            }

            public int BalanceCents => Balance;

            public string TryDebit(int amountCents, string reason)
            {
                if (amountCents < 0 || amountCents > Balance)
                {
                    return "run business cash: insufficient funds";
                }

                Balance -= amountCents;
                return null;
            }

            public string Credit(int amountCents, string reason)
            {
                if (amountCents < 0)
                {
                    return "run business cash: negative credit";
                }

                Balance += amountCents;
                return null;
            }
        }

        private sealed class FakeMigrationHolder : IPassXlMigrationFlagHolder
        {
            public bool SuppressMigrantArrivals { get; set; }
        }

        private sealed class FakePlayerHolder : IPassXlPlayerFlagHolder
        {
            public bool TreatPlayerAsNpc { get; set; }
        }

        /// <summary>
        /// Runs the composed slice. Every numeric input is a caller-supplied
        /// test parameter. Returns null only if a step that must succeed
        /// (order, arrival, sale, restore) fails; diagnostics then say why.
        /// Meal days run on the restored inventory, starting on arrival day.
        /// </summary>
        public static SixPersonSliceRunReport Execute(
            int startDay,
            int businessCashStartCents,
            int householdCashStartCents,
            int orderUnits,
            int unitGoodsCostCents,
            int freightSurchargeCents,
            int marketSupplyUnits,
            int transitDays,
            int saleUnits,
            int saleUnitPriceCents,
            int mealDays)
        {
            var report = new SixPersonSliceRunReport
            {
                OrderDay = startDay,
                BusinessCashStartCents = businessCashStartCents,
                HouseholdCashStartCents = householdCashStartCents,
            };
            report.SeamsStopped.Add("Bakery production: staged chain has no Main Scene owner/driver, no BreadShelf-to-SKU transfer, no flour feed (phase 4 F16/F17); no bread produced in this run.");
            report.SeamsStopped.Add("Fuel Dealer production: no Task to tree depletion to wood lot to sale path; no fuel produced or sold in this run.");
            report.SeamsStopped.Add("Work and pay: no Roslyn-usable wage payroll for General Store / Bakery / Fuel Dealer work in this run; opening cash is an explicit run input, not slice earnings.");
            report.SeamsStopped.Add("Daily tick driver: run steps are called explicitly by this orchestrator; no Main Scene tick driver is proved here.");
            report.SeamsStopped.Add("Residence assignment: meals use HouseholdState id and member list directly, not a HousingResidence record (Codex-only).");
            report.SeamsStopped.Add("Postal office: no recruitment letter is posted in this run; office registration remains Codex-only (phase 4 F04).");
            report.SeamsStopped.Add("Supplier registration: supplier is a test counterparty, not a production supplier (phase 4 F37).");
            report.SeamsStopped.Add("Business cash: run cash port is in-memory; production maps to BusinessRuntimeState and must be registered with SaveLoadManager (phase 4 F19).");
            report.SeamsStopped.Add("Other households' meals: Pierce and Boniecki households buy no flour in this run; no meals are proven for them, and no stock is invented for them.");

            var market = new ExternalMarketAuthority();
            var supplier = new ExternalCounterparty
            {
                CounterpartyId = "test-grain",
                DisplayName = "Test Grain Co.",
                InitialBudgetCents = 50000,
                TransitDays = transitDays,
            };
            supplier.SupplyUnits["flour"] = marketSupplyUnits;
            string registerProblem = market.RegisterCounterparty(supplier);
            if (registerProblem != null)
            {
                report.Diagnostics.Add("Run failed: supplier registration refused: " + registerProblem);
                return null;
            }

            var businessCash = new RunBusinessCash(businessCashStartCents);
            var service = new OffMapNamedSkuProcurementService("general-store-1", market, businessCash);
            report.ProcurementService = service;

            var baldwinLedger = FundedHousehold(1, householdCashStartCents, startDay, report);
            var pierceLedger = FundedHousehold(2, householdCashStartCents, startDay, report);
            var bonieckiLedger = FundedHousehold(3, householdCashStartCents, startDay, report);
            if (baldwinLedger == null || pierceLedger == null || bonieckiLedger == null)
            {
                return null;
            }

            report.BaldwinOomsLedger = baldwinLedger;
            var inventories = new HouseholdInventoryRegistry();
            var baldwinInventory = inventories.GetOrCreate(1);
            inventories.GetOrCreate(2);
            inventories.GetOrCreate(3);
            report.BaldwinOomsInventory = baldwinInventory;

            NamedSkuOrder order = service.PlaceOrder(
                "flour", orderUnits, unitGoodsCostCents, freightSurchargeCents,
                "test-grain", startDay, NamedSkuPaymentMode.CashOnArrival, null);
            if (order == null)
            {
                report.Diagnostics.Add("Run failed: order was refused; see service diagnostics.");
                report.Diagnostics.AddRange(service.Diagnostics);
                return null;
            }

            report.OrderId = order.OrderId;
            report.ReceiptId = order.ReceiptId;
            report.ArrivalDay = order.ExpectedArrivalDayIndex;

            // Before arrival: transit is real, no lot, no stock.
            List<NamedSkuLot> early = service.ReceiveArrivals(startDay, null);
            if (early.Count != 0 || service.SellableUnits("flour") != 0)
            {
                report.Diagnostics.Add("Run failed: stock appeared before the arrival day.");
                return null;
            }

            List<NamedSkuLot> arrived = service.ReceiveArrivals(report.ArrivalDay, null);
            if (arrived.Count != 1)
            {
                report.Diagnostics.Add($"Run failed: expected exactly 1 lot on arrival day, got {arrived.Count}.");
                report.Diagnostics.AddRange(service.Diagnostics);
                return null;
            }

            report.StoreLotId = arrived[0].LotId;
            report.BusinessCashAfterProcurementCents = businessCash.Balance;

            // Duplicate receipt creates no second lot.
            List<NamedSkuLot> duplicate = service.ReceiveArrivals(report.ArrivalDay, null);
            if (duplicate.Count != 0 || service.Lots.Count != 1)
            {
                report.Diagnostics.Add("Run failed: duplicate receipt created a second lot.");
                return null;
            }

            OffMapNamedSkuProcurementService.NamedSkuDeliveryResult delivery =
                service.SellAndDeliverToInventory(
                    baldwinLedger, baldwinInventory, 1, 1, "person:1",
                    "flour", saleUnits, saleUnitPriceCents, report.ArrivalDay);
            if (delivery == null || !delivery.Success || delivery.Sale == null)
            {
                report.Diagnostics.Add("Run failed: atomic sale-and-delivery was refused: " + (delivery != null ? delivery.Diagnostic : "null result"));
                report.Diagnostics.AddRange(service.Diagnostics);
                return null;
            }

            report.SaleId = delivery.Sale.SaleId;
            report.HouseholdInventoryLotId = delivery.HouseholdInventoryLotId;
            report.BusinessCashAfterSaleCents = businessCash.Balance;
            report.HouseholdCashAfterSaleCents = baldwinLedger.GetBalanceCents();
            report.StoreLotRemainingUnits = service.Lots[0].RemainingUnits;
            report.SellableUnitsAfterSale = service.SellableUnits("flour");
            report.HouseholdFlourBeforeMeals = baldwinInventory.GetAvailableUnits("flour");
            report.MarketSupplyRemainingUnits = market.GetCounterparty("test-grain").SupplyUnits["flour"];
            report.MarketOutflowCents = market.TotalOutflowCents;

            // Save / restore (phase 2 coordinator), then re-delivery must
            // be AlreadyDelivered and add nothing.
            var migrationHolder = new FakeMigrationHolder { SuppressMigrantArrivals = true };
            var playerHolder = new FakePlayerHolder { TreatPlayerAsNpc = false };
            PassXlSliceSaveDto dto = PassXlSliceSaveCoordinator.Capture(migrationHolder, playerHolder, service);
            var restoredCash = new RunBusinessCash(report.BusinessCashAfterSaleCents);
            var restoredService = new OffMapNamedSkuProcurementService(
                "general-store-1", market, restoredCash);
            var restoredMigration = new FakeMigrationHolder();
            var restoredPlayer = new FakePlayerHolder();
            List<string> restoreDiagnostics = PassXlSliceSaveCoordinator.Restore(
                dto, restoredMigration, restoredPlayer, restoredService);
            report.Diagnostics.AddRange(restoreDiagnostics);
            report.SuppressMigrantArrivalsRestored = restoredMigration.SuppressMigrantArrivals;
            report.TreatPlayerAsNpcRestored = restoredPlayer.TreatPlayerAsNpc;
            report.RestoredOrderCount = restoredService.Orders.Count;
            report.RestoredLotCount = restoredService.Lots.Count;
            report.RestoredSaleCount = restoredService.Sales.Count;
            report.RestoredDeliveredCount = restoredService.DeliveredSaleIds.Count;
            report.RestoredService = restoredService;

            // Rebuild the restored inventory from saved lots (phase 2
            // pattern): household inventory persistence is via its own
            // state DTOs in production, not via the slice DTO.
            var restoredState = new HouseholdInventoryState
            {
                HouseholdId = 1,
                NextLotSequence = baldwinInventory.State.NextLotSequence,
                Lots = new List<HouseholdItemLot>(),
            };
            for (int i = 0; i < baldwinInventory.Lots.Count; i++)
            {
                HouseholdItemLot lot = baldwinInventory.Lots[i];
                restoredState.Lots.Add(new HouseholdItemLot
                {
                    LotId = lot.LotId,
                    ItemId = lot.ItemId,
                    QuantityUnits = lot.QuantityUnits,
                    UnitLabel = lot.UnitLabel,
                    AcquiredDayIndex = lot.AcquiredDayIndex,
                    SourceLabel = lot.SourceLabel,
                    QualityBand = lot.QualityBand,
                    SourceKind = lot.SourceKind,
                    OwnerPersonId = lot.OwnerPersonId,
                });
            }

            var restoredInventory = new HouseholdInventory(restoredState);
            report.RestoredInventory = restoredInventory;
            NamedSkuSale restoredSale = restoredService.Sales.Count > 0 ? restoredService.Sales[0] : null;
            if (restoredSale == null)
            {
                report.Diagnostics.Add("Run failed: restored service has no sale to re-deliver.");
                return null;
            }

            OffMapNamedSkuProcurementService.NamedSkuDeliveryResult again =
                restoredService.DeliverSaleToInventory(restoredSale, restoredInventory);
            report.RedeliveryWasAlreadyDelivered = again != null && again.AlreadyDelivered;
            report.RedeliveryInventoryLotCount = restoredInventory.Lots.Count;
            if (!report.RedeliveryWasAlreadyDelivered || report.RedeliveryInventoryLotCount != 1)
            {
                report.Diagnostics.Add("Run failed: re-delivery after restore was not AlreadyDelivered with exactly 1 inventory lot.");
                return null;
            }

            // Meals, on the restored inventory, starting on arrival day.
            var mealLog = new HouseholdMealLogRegistry();
            var mealService = new HouseholdMealService(mealLog);
            var household = new HouseholdState { id = 1, memberIds = new List<int> { 1, 2 } };
            for (int dayOffset = 0; dayOffset < mealDays; dayOffset++)
            {
                int day = report.ArrivalDay + dayOffset;
                MealDayResult dayResult = mealService.ServeHouseholdDay(
                    household, new List<int> { 1, 2 }, restoredInventory,
                    MealSchedulingPolicy.Default, day, null);
                report.MealsEaten += dayResult.MealsServed;
                report.MealsMissed += dayResult.MealsMissed;
                report.MealsEatenPerDay.Add(dayResult.MealsServed);
            }

            report.HouseholdFlourAfterMeals = restoredInventory.GetAvailableUnits("flour");
            report.FlourConsumedByMeals = report.HouseholdFlourBeforeMeals - report.HouseholdFlourAfterMeals;
            return report;
        }

        private static HouseholdLedger FundedHousehold(
            int householdId, int balanceCents, int dayIndex, SixPersonSliceRunReport report)
        {
            var ledger = new HouseholdLedger(householdId);
            string problem = ledger.RecordInflow(
                dayIndex, balanceCents, HouseholdIncomeSource.OtherDocumented,
                "passxl-phase6-run-input",
                "run input: prior earnings simulated for the composed run; NOT fixture starting capital (fixture is 0)",
                "run-input");
            if (problem != null)
            {
                report.Diagnostics.Add($"Run failed: funding H{householdId} refused: {problem}");
                return null;
            }

            return ledger;
        }
    }
}
