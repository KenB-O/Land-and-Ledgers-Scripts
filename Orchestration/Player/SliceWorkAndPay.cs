using System;
using System.Collections.Generic;
using LandLedgers.Economy;
using LandLedgers.Economy.Businesses.GeneralStore;
using LandLedgers.Economy.Core;
using LandLedgers.Economy.Labor;
using LandLedgers.Population;

namespace LandLedgers.Orchestration.Player
{
    /// <summary>
    /// Pass XL phase 7: work and pay inside the six-person slice, using ONLY
    /// the existing payroll authority. This is a bridge and a Roslyn
    /// slice-run orchestrator, NOT a production driver and NOT a Main
    /// Scene tick driver. Codex must still wire a real daily/weekly
    /// payroll driver in Main Scene; nothing here runs on a schedule.
    ///
    /// THE OWNER / EMPLOYEE DISTINCTION (read this first, it is the
    /// whole point of this pass):
    ///   - A founder working his own business is a FarmOrBusinessOperator
    ///     (TECH 4.1). Compensation is profit / equity draw, NEVER
    ///     payroll. Kennedy (General Store), Glen (Bakery), and Jacob
    ///     (Fuel Dealer) are all founders in the six-person fixture, so
    ///     NONE of them can be paid a wage by their own business in
    ///     this pass, and this bridge refuses any attempt to register
    ///     one as a wage employee of his own business.
    ///   - A wage employee is someone else, hired by a business, with a
    ///     real EmploymentRelationship carrying agreed weekly wage
    ///     terms. Wage payment goes through the ONE existing path:
    ///     WageSettlementService + BusinessRuntimeState (weekly payroll
    ///     via ResolveWeeklyPayroll, partial periods via PayAgreedWage),
    ///     with household cash moving ONLY through HouseholdLedger
    ///     (RecordWagePayment). No second payroll authority is created
    ///     here, and no wage is paid for work not done: weekly payroll
    ///     pays only weeks with caller-supplied attendance, and a week
    ///     with no attendance earns nothing and accrues nothing.
    ///   - Owner earnings are therefore proved a different way: a
    ///     business that sells goods keeps the sale margin in business
    ///     cash, and an explicitly labelled owner draw moves caller-
    ///     supplied cents from business cash to the owner's household
    ///     ledger (HouseholdIncomeSource.OwnerDraw, business id as
    ///     source reference), capped by a caller-supplied reserve that
    ///     stays in the business. A draw is NOT a wage, is NOT booked
    ///     as WageEmployment, and creates no cash: business cash falls
    ///     by exactly the draw, household cash rises by exactly the
    ///     draw. The draw amount and reserve are caller-supplied test
    ///     parameters, never a canon draw rule.
    ///
    /// WHAT THE COMPOSED RUN PROVES (SliceWorkAndPayRun):
    /// Phase 6 gave BOTH the business and the household opening cash
    /// as run inputs. This run gives the household ZERO opening cash.
    /// The business starts with caller-supplied owner equity (seed,
    /// labelled as such, NOT wages, NOT slice earnings), draws owner
    /// earnings to the Baldwin-Ooms household, and that household then
    /// buys flour with the drawn cash, twice, with the business cash
    /// reconciling at every step. The second purchase is funded by
    /// earnings drawn from the first cycle, not by a fresh household
    /// run input. The first cycle's business seed is still an input
    /// and is labelled as one: a store with 0 capital cannot order
    /// stock on CashOnArrival, and this pass does not pretend
    /// otherwise (see the report's SeamsStopped).
    ///
    /// WHAT STOPS AT A SEAM (never faked):
    ///   - Bakery and Fuel Dealer owner earnings: no production or
    ///     sale is proved for either in this pass (phase 4 F16/F17,
    ///     Fuel task-to-wood-lot path), so no draw is proved for Glen
    ///     or Jacob. Only the General Store earn-then-spend cycle runs.
    ///   - Employee payroll for slice residents: the six-person slice
    ///     has no non-founder employees (Emma, Cassidy, Grace stay
    ///     home, per the fixture), so the employee wage tests use a
    ///     clearly labelled non-slice test employee and business, to
    ///     prove the mechanism, NOT to claim a slice resident was paid.
    ///   - Daily/weekly tick driver, residence assignment, postal
    ///     office, and production supplier registration: Codex-only,
    ///     as in phase 6.
    ///   - Business cash persistence: the composed run's business cash
    ///     is the BusinessRuntimeState in memory; production maps it
    ///     to its own save path and must register the procurement
    ///     service with SaveLoadManager (phase 4 F19), as in phase 6.
    ///
    /// All wages, draws, prices, freight, supply, and transit are
    /// caller-supplied test parameters, never canon rates.
    /// </summary>
    public sealed class SlicePayrollSetup
    {
        public BusinessRuntimeState Business;
        public EmploymentRelationshipRegistry Employments = new EmploymentRelationshipRegistry();
        public HouseholdLedgerRegistry Ledgers = new HouseholdLedgerRegistry();
        public WageSettlementService Settlement = new WageSettlementService();
        public Dictionary<int, int> PersonHousehold = new Dictionary<int, int>();
        public Dictionary<string, bool> AttendanceByEmploymentWeek = new Dictionary<string, bool>();
        public List<string> Diagnostics = new List<string>();
    }

    public sealed class SliceOwnerLabourRecord
    {
        public int PersonId;
        public string BusinessId = string.Empty;
        public int DayIndex;
        public int DaysWorked;
        public int WageCents;
        public string Note = string.Empty;
    }

    public sealed class SliceOwnerDrawResult
    {
        public bool Success;
        public int AmountCents;
        public int BusinessCashAfterCents;
        public int HouseholdCashAfterCents;
        public string Diagnostic = string.Empty;
    }

    /// <summary>
    /// IBusinessCashPort over a BusinessRuntimeState, so the phase 1/2
    /// procurement service and the real payroll cash holder are the
    /// SAME cash, not two ledgers for one store. TrySpendCents is the
    /// single-writer spend; AddCashCents is the single-writer credit.
    /// </summary>
    public sealed class BusinessRuntimeCashPort : IBusinessCashPort
    {
        private readonly BusinessRuntimeState runtime;

        public BusinessRuntimeCashPort(BusinessRuntimeState runtime)
        {
            this.runtime = runtime;
        }

        public int BalanceCents => runtime != null ? runtime.CurrentCashCents : 0;

        public string TryDebit(int amountCents, string reason)
        {
            if (runtime == null)
            {
                return "business runtime cash port: no runtime";
            }

            if (amountCents < 0)
            {
                return "business runtime cash port: negative debit";
            }

            if (!runtime.TrySpendCents(amountCents))
            {
                return $"business runtime cash port: insufficient funds ({runtime.CurrentCashCents}c for {amountCents}c)";
            }

            return null;
        }

        public string Credit(int amountCents, string reason)
        {
            if (runtime == null)
            {
                return "business runtime cash port: no runtime";
            }

            if (amountCents < 0)
            {
                return "business runtime cash port: negative credit";
            }

            runtime.AddCashCents(amountCents);
            return null;
        }
    }

    public static class SliceWorkAndPay
    {
        /// <summary>
        /// Creates a business runtime wired for real payroll: identity,
        /// opening cash, employment registry, household ledgers, and
        /// the person-to-household lookup. Attendance is caller-
        /// supplied through SetAttendance; with no attendance entry
        /// for a week, that week is treated as not worked.
        /// </summary>
        public static SlicePayrollSetup CreateBusiness(
            string businessId, int openingCashCents,
            Dictionary<int, int> personHousehold)
        {
            var setup = new SlicePayrollSetup();
            if (personHousehold != null)
            {
                foreach (KeyValuePair<int, int> entry in personHousehold)
                {
                    setup.PersonHousehold[entry.Key] = entry.Value;
                }
            }

            var business = new BusinessRuntimeState();
            business.BindBusinessIdentity(businessId);
            business.AddCashCents(Math.Max(0, openingCashCents));
            business.EmploymentRegistry = setup.Employments;
            business.HouseholdLedgers = setup.Ledgers;
            business.PersonHouseholdIdLookup = pid =>
                setup.PersonHousehold.TryGetValue(pid, out int householdId) ? householdId : -1;
            business.WeeklyAttendanceProvider = (employmentId, weekIndex) =>
                setup.AttendanceByEmploymentWeek.TryGetValue(employmentId + "|" + weekIndex, out bool worked) && worked;
            setup.Business = business;
            return setup;
        }

        /// <summary>
        /// Registers a wage employee. Refuses, with no record created,
        /// when the person is the owner of this business: owner labour
        /// is never wage employment (TECH 4.1). Refuses duplicate
        /// employment ids (first wins, per the registry) and non-
        /// positive wages are clamped to 0 by CompensationTerms, so a
        /// 0-wage employment pays nothing and is reported as such.
        /// Returns null on success, or a diagnostic on refusal.
        /// </summary>
        public static string RegisterEmployee(
            SlicePayrollSetup setup, string employmentId, int personId,
            string businessId, int weeklyWageCents, int startDayIndex,
            bool isOwnerOfThisBusiness)
        {
            if (setup == null)
            {
                return "SliceWorkAndPay.RegisterEmployee: no payroll setup";
            }

            if (isOwnerOfThisBusiness)
            {
                return $"SliceWorkAndPay.RegisterEmployee: P{personId} is the owner of '{businessId}' " +
                    "- owner labour is profit/equity draw, never payroll (TECH 4.1 FarmOrBusinessOperator); " +
                    "no wage employment was created.";
            }

            var relationship = new EmploymentRelationship
            {
                Id = employmentId,
                EmployeePersonId = personId,
                EmployerBusinessId = businessId,
                RoleDisplayName = "slice employee",
                Kind = EmploymentKind.Permanent,
                LifecycleState = EmploymentLifecycleState.Active,
                Compensation = CompensationTerms.FromWeeklyWage(weeklyWageCents, "slice test terms, caller-supplied, never canon"),
                StartDayIndex = startDayIndex,
                EndDayIndex = -1,
                Source = EmploymentSource.Manual,
            };
            string problem = setup.Employments.Register(relationship);
            if (problem != null)
            {
                return "SliceWorkAndPay.RegisterEmployee: " + problem;
            }

            return null;
        }

        /// <summary>
        /// Records caller-supplied attendance for one employment-week.
        /// Only weeks marked worked are paid by weekly payroll.
        /// </summary>
        public static void SetAttendance(SlicePayrollSetup setup, string employmentId, int weekIndex, bool worked)
        {
            if (setup == null)
            {
                return;
            }

            setup.AttendanceByEmploymentWeek[employmentId + "|" + weekIndex] = worked;
        }

        /// <summary>
        /// Settles one weekly payroll through the real WageSettlementService.
        /// Stamps PayrollDayIndex first, so the attendance week and the
        /// replay week agree. Returns the service result ("settled",
        /// "settled-arrears", "already-settled", or "refused").
        /// </summary>
        public static string SettleWeek(SlicePayrollSetup setup, int weekIndex, int dayIndex, List<string> diag)
        {
            if (setup == null || setup.Business == null)
            {
                return "refused";
            }

            setup.Business.PayrollDayIndex = dayIndex;
            return setup.Settlement.SettleWeeklyPayroll(setup.Business, setup.Employments, weekIndex, dayIndex, diag);
        }

        /// <summary>
        /// Settles a partial-period wage through the real
        /// PayAgreedWage path (via WageSettlementService). Amount is
        /// caller-supplied (use WageSettlementService.ProrateWeeklyWage
        /// to derive it from a weekly wage); no amount is invented here.
        /// </summary>
        public static string SettlePartial(
            SlicePayrollSetup setup, EmploymentRelationship employment,
            int amountCents, string periodLabel, int dayIndex, List<string> diag)
        {
            if (setup == null)
            {
                return "refused";
            }

            return setup.Settlement.SettlePartialPeriod(setup.Business, employment, amountCents, periodLabel, dayIndex, diag);
        }

        /// <summary>
        /// Records owner labour with NO cash movement and NO employment
        /// record. WageCents is always 0: the owner earns through the
        /// business's sale margin and an explicit owner draw, never
        /// through payroll.
        /// </summary>
        public static SliceOwnerLabourRecord RecordOwnerLabour(
            int personId, string businessId, int dayIndex, int daysWorked)
        {
            return new SliceOwnerLabourRecord
            {
                PersonId = personId,
                BusinessId = businessId,
                DayIndex = dayIndex,
                DaysWorked = Math.Max(0, daysWorked),
                WageCents = 0,
                Note = "Owner labour (TECH 4.1 FarmOrBusinessOperator): profit/equity draw only, never payroll.",
            };
        }

        /// <summary>
        /// Moves an explicit owner draw from business cash to a household
        /// ledger. Refuses when the amount is not positive, when the
        /// business cannot keep the caller-supplied reserve after the
        /// draw, or when the household ledger rejects the inflow, moving
        /// nothing in any refusal case. On success, business cash falls
        /// and household cash rises by exactly the draw, and the draw
        /// is recorded on the runtime for readout.
        /// </summary>
        public static SliceOwnerDrawResult DrawOwnerEarnings(
            BusinessRuntimeState business, HouseholdLedger householdLedger,
            int dayIndex, int amountCents, int reserveCents)
        {
            var result = new SliceOwnerDrawResult();
            if (business == null || householdLedger == null)
            {
                result.Diagnostic = "SliceWorkAndPay.DrawOwnerEarnings: business runtime and household ledger are required.";
                return result;
            }

            if (amountCents <= 0)
            {
                result.Diagnostic = "SliceWorkAndPay.DrawOwnerEarnings: draw must be positive; nothing moved.";
                return result;
            }

            if (business.CurrentCashCents - amountCents < Math.Max(0, reserveCents))
            {
                result.Diagnostic = $"SliceWorkAndPay.DrawOwnerEarnings: draw of {amountCents}c would leave " +
                    $"{business.CurrentCashCents - amountCents}c, below the {Math.Max(0, reserveCents)}c reserve; nothing moved.";
                result.BusinessCashAfterCents = business.CurrentCashCents;
                result.HouseholdCashAfterCents = householdLedger.GetBalanceCents();
                return result;
            }

            string rejection = householdLedger.RecordInflow(
                dayIndex, amountCents, HouseholdIncomeSource.OwnerDraw,
                business.BusinessId,
                $"owner draw from {business.BusinessId}: profit/equity draw, not a wage",
                business.BusinessId);
            if (rejection != null)
            {
                result.Diagnostic = "SliceWorkAndPay.DrawOwnerEarnings: household ledger refused the draw: " + rejection + "; nothing moved.";
                result.BusinessCashAfterCents = business.CurrentCashCents;
                result.HouseholdCashAfterCents = householdLedger.GetBalanceCents();
                return result;
            }

            business.AdjustCashCents(-amountCents, true);
            business.RecordWeeklyOwnerDistribution(amountCents);
            result.Success = true;
            result.AmountCents = amountCents;
            result.BusinessCashAfterCents = business.CurrentCashCents;
            result.HouseholdCashAfterCents = householdLedger.GetBalanceCents();
            return result;
        }
    }

    public sealed class SliceWorkAndPayRunReport
    {
        public int BusinessSeedCents;
        public int ReserveCents;
        public SliceOwnerLabourRecord KennedyLabour;
        public SliceOwnerDrawResult Draw1;
        public SliceOwnerDrawResult Draw2;
        public string OrderId = string.Empty;
        public string Sale1Id = string.Empty;
        public string Sale2Id = string.Empty;
        public int BusinessCashAfterDraw1Cents;
        public int BusinessCashAfterProcurementCents;
        public int BusinessCashAfterSale1Cents;
        public int BusinessCashAfterDraw2Cents;
        public int BusinessCashFinalCents;
        public int HouseholdCashStartCents;
        public int HouseholdCashAfterDraw1Cents;
        public int HouseholdCashAfterSale1Cents;
        public int HouseholdCashAfterDraw2Cents;
        public int HouseholdCashFinalCents;
        public int StoreLotRemainingUnits;
        public int MealsEaten;
        public int MealsMissed;
        public List<string> SeamsStopped = new List<string>();
        public List<string> Diagnostics = new List<string>();
        public BusinessRuntimeState StoreRuntime;
        public HouseholdLedger BaldwinOomsLedger;
        public HouseholdInventory BaldwinOomsInventory;
        public OffMapNamedSkuProcurementService ProcurementService;
    }

    /// <summary>
    /// Composed earn-then-spend run for the General Store only. The
    /// Baldwin-Ooms household starts at 0. Business seed is owner
    /// equity (labelled, NOT wages). Draw 1 funds purchase 1; draw 2
    /// (from cycle-1 sale margin) funds purchase 2. See the file
    /// header for what is NOT proved here.
    /// </summary>
    public static class SliceWorkAndPayRun
    {
        public static SliceWorkAndPayRunReport Execute(
            int startDay, int businessSeedCents, int reserveCents,
            int draw1Cents, int draw2Cents,
            int orderUnits, int unitGoodsCostCents, int freightSurchargeCents,
            int marketSupplyUnits, int transitDays,
            int sale1Units, int sale1UnitPriceCents,
            int sale2Units, int sale2UnitPriceCents)
        {
            var report = new SliceWorkAndPayRunReport
            {
                BusinessSeedCents = businessSeedCents,
                ReserveCents = reserveCents,
                HouseholdCashStartCents = 0,
            };
            report.SeamsStopped.Add("Bakery owner earnings: no production or sale proved for Pierce Bakery (phase 4 F16/F17); no draw proved for Glen.");
            report.SeamsStopped.Add("Fuel Dealer owner earnings: no Task to tree depletion to wood lot to sale path; no draw proved for Jacob.");
            report.SeamsStopped.Add("Employee payroll for slice residents: the six-person slice has no non-founder employees; employee wages are proved separately with a non-slice test employee, not a slice resident.");
            report.SeamsStopped.Add("Daily/weekly tick driver: run steps are called explicitly here; no Main Scene tick driver is proved.");
            report.SeamsStopped.Add("First-cycle business seed: the store starts with caller-supplied owner equity, not slice earnings; a 0-capital store cannot order on CashOnArrival, and this run does not pretend otherwise.");
            report.SeamsStopped.Add("Business cash persistence: runtime cash is in-memory here; production maps it to its own save path and must register the procurement service with SaveLoadManager (phase 4 F19).");

            var personHousehold = new Dictionary<int, int> { { 1, 1 }, { 2, 1 }, { 3, 2 }, { 4, 2 }, { 5, 3 }, { 6, 3 } };
            var payroll = SliceWorkAndPay.CreateBusiness("general-store-1", businessSeedCents, personHousehold);
            var store = payroll.Business;
            report.StoreRuntime = store;
            var ledgers = payroll.Ledgers;
            var baldwinLedger = ledgers.GetOrCreate(1);
            report.BaldwinOomsLedger = baldwinLedger;
            var inventories = new HouseholdInventoryRegistry();
            var baldwinInventory = inventories.GetOrCreate(1);
            report.BaldwinOomsInventory = baldwinInventory;

            report.KennedyLabour = SliceWorkAndPay.RecordOwnerLabour(1, "general-store-1", startDay, 7);
            if (report.KennedyLabour.WageCents != 0)
            {
                report.Diagnostics.Add("Run failed: owner labour carried a wage.");
                return null;
            }

            report.Draw1 = SliceWorkAndPay.DrawOwnerEarnings(store, baldwinLedger, startDay, draw1Cents, reserveCents);
            if (!report.Draw1.Success)
            {
                report.Diagnostics.Add("Run failed: draw 1 refused: " + report.Draw1.Diagnostic);
                return null;
            }

            report.BusinessCashAfterDraw1Cents = store.CurrentCashCents;
            report.HouseholdCashAfterDraw1Cents = baldwinLedger.GetBalanceCents();

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

            var cashPort = new BusinessRuntimeCashPort(store);
            var service = new OffMapNamedSkuProcurementService("general-store-1", market, cashPort);
            report.ProcurementService = service;

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
            List<NamedSkuLot> arrived = service.ReceiveArrivals(order.ExpectedArrivalDayIndex, null);
            if (arrived.Count != 1)
            {
                report.Diagnostics.Add($"Run failed: expected exactly 1 lot on arrival day, got {arrived.Count}.");
                report.Diagnostics.AddRange(service.Diagnostics);
                return null;
            }

            report.BusinessCashAfterProcurementCents = store.CurrentCashCents;
            int arrivalDay = order.ExpectedArrivalDayIndex;

            OffMapNamedSkuProcurementService.NamedSkuDeliveryResult delivery1 =
                service.SellAndDeliverToInventory(
                    baldwinLedger, baldwinInventory, 1, 1, "person:1",
                    "flour", sale1Units, sale1UnitPriceCents, arrivalDay);
            if (delivery1 == null || !delivery1.Success || delivery1.Sale == null)
            {
                report.Diagnostics.Add("Run failed: sale 1 refused: " + (delivery1 != null ? delivery1.Diagnostic : "null result"));
                report.Diagnostics.AddRange(service.Diagnostics);
                return null;
            }

            report.Sale1Id = delivery1.Sale.SaleId;
            report.BusinessCashAfterSale1Cents = store.CurrentCashCents;
            report.HouseholdCashAfterSale1Cents = baldwinLedger.GetBalanceCents();

            report.Draw2 = SliceWorkAndPay.DrawOwnerEarnings(store, baldwinLedger, arrivalDay, draw2Cents, reserveCents);
            if (!report.Draw2.Success)
            {
                report.Diagnostics.Add("Run failed: draw 2 refused: " + report.Draw2.Diagnostic);
                return null;
            }

            report.BusinessCashAfterDraw2Cents = store.CurrentCashCents;
            report.HouseholdCashAfterDraw2Cents = baldwinLedger.GetBalanceCents();

            OffMapNamedSkuProcurementService.NamedSkuDeliveryResult delivery2 =
                service.SellAndDeliverToInventory(
                    baldwinLedger, baldwinInventory, 1, 1, "person:1",
                    "flour", sale2Units, sale2UnitPriceCents, arrivalDay);
            if (delivery2 == null || !delivery2.Success || delivery2.Sale == null)
            {
                report.Diagnostics.Add("Run failed: sale 2 refused: " + (delivery2 != null ? delivery2.Diagnostic : "null result"));
                report.Diagnostics.AddRange(service.Diagnostics);
                return null;
            }

            report.Sale2Id = delivery2.Sale.SaleId;
            report.BusinessCashFinalCents = store.CurrentCashCents;
            report.HouseholdCashFinalCents = baldwinLedger.GetBalanceCents();
            report.StoreLotRemainingUnits = service.Lots.Count > 0 ? service.Lots[0].RemainingUnits : 0;

            var mealLog = new HouseholdMealLogRegistry();
            var mealService = new HouseholdMealService(mealLog);
            var household = new HouseholdState { id = 1, memberIds = new List<int> { 1, 2 } };
            MealDayResult dayResult = mealService.ServeHouseholdDay(
                household, new List<int> { 1, 2 }, baldwinInventory,
                MealSchedulingPolicy.Default, arrivalDay, null);
            report.MealsEaten = dayResult.MealsServed;
            report.MealsMissed = dayResult.MealsMissed;
            return report;
        }
    }
}
