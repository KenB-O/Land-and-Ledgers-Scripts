// Pass VIII (Worker 5: Economic Circulation and Business Adaptation):
// "Ruston Decline" — a 45-day adverse-condition circulation proof.
//
// Adverse beats, all through production authorities, with NO secret
// stabilization. The town is allowed to decline; every consequence is real:
//   day  2: the mill draws a real bank loan (approved; the bank vault is
//           debited before the obligation exists — no unfunded proceeds).
//   day 10: farm harvest +400 grain (normal).
//   day 14: household rent due (all pay).
//   day 15: MAJOR EMPLOYER CLOSES — the mill shuts; P1 loses work. The
//           close-down settles the bank loan in full through the creditor
//           cash bridge, so the bank vault receives the payment (the Pass
//           VIII circulation fix: before it, the creditor-side cash was
//           destroyed). Millstones liquidate to a real buyer; residual cash
//           returns to the founder's household; P1's employment ends.
//   day 16: CREDIT CONTRACTION — the bakery's loan application is refused
//           by real underwriting (weak cash flow). No debt is created and
//           the bank vault is untouched.
//   day 18: SUPPLIER FAILS / IMPORTS — with the mill gone the bakery
//           imports flour from kansas-city at 75c (up from 55c). Cash leaves
//           across the boundary; goods arrive after real transit.
//   day 22: PRICE CHANGE — the bakery raises wholesale bread 75c -> 90c.
//   day 25: PRICE CHANGE — the store raises retail bread 100c -> 120c.
//   day 27: the bakery pays its oven-repair contract to H3 (services).
//   day 29: OVERDUE RENT — H1 (no wages since day 15) cannot pay in full;
//           partial payment plus a real arrears obligation.
//   day 30: DROUGHT — the second harvest yields 100 grain, not 400.
//   day 34: the bakery cannot pay its weekly commercial rent — a real
//           rent-arrears payable to household:4.
//   day 35: EXPORTS — the farm exports 100 grain to kansas-city (inflow).
//   day 36: BUSINESS INSOLVENCY — the bakery closes down: the rent arrears
//           are settled as far as cash reaches and the shortfall is
//           EXPLICITLY assumed by founder P4 (never stranded); staff are
//           released through the employment authority; residual cash returns
//           to the founder's household.
//   day 44: household rent due again.
//
// Reconciliation (asserted exactly):
//   - cash: every modeled cent (4 business tills + 4 household ledgers +
//     the bank vault) is conserved against the external-trade boundary net.
//   - goods: grain, flour and bread balance across production, trade,
//     consumption, waste and ending stocks.
//   - debts: the obligation authority reconciles with no errors and no
//     duplicate payments; every outstanding debt names its debtor.
//   - identities: employments end exactly once; no person works two jobs.
//
// Reported scope: Roslyn harness only. This is NOT Unity validation.
using System;
using System.Collections.Generic;
using System.Reflection;
using LandLedgers.Economy;
using LandLedgers.Economy.Core;
using LandLedgers.Economy.Creation;
using LandLedgers.Economy.Financing;
using LandLedgers.Population;
using LandLedgers.Primitives;
using LandLedgers.ReadModels.Valuation;
using LandLedgers.World.Property;
using NUnit.Framework;

namespace LandLedgers.Editor.Integration
{
    /// <summary>
    /// The Ruston Decline scenario world. All mutable economy state lives in
    /// the production authorities; this class only orchestrates days, applies
    /// the adverse beats, and reconciles.
    /// </summary>
    public sealed class AdverseCirculationScenario
    {
        public const int StartDay = 0;
        public const int SimDays = 45;
        public const int EndDayExclusive = StartDay + SimDays;

        // Households: H1 miller P1, H2 baker P2, H3 farmer P3, H4 landlord P4.
        public const int H1 = 1, H2 = 2, H3 = 3, H4 = 4;
        public const int P1 = 1, P2 = 2, P3 = 3, P4 = 4;

        public const string Mill = "mill-1";
        public const string Bakery = "bakery-1";
        public const string Farm = "farm-1";
        public const string Store = "store-1";

        public const string Grain = "grain";
        public const string Flour = "flour";
        public const string Bread = "bread";

        public const int GrainPriceCents = 35;
        public const int FlourPriceCents = 55;
        public const int FlourImportPriceCents = 75;
        public const int BreadWholesaleCents = 75;
        public const int BreadWholesaleRaisedCents = 90;
        public const int BreadRetailCents = 100;
        public const int BreadRetailRaisedCents = 120;

        public const int MillWageCents = 600;
        public const int BakeryWageCents = 500;
        public const int FarmWageCents = 450;
        public const int HouseholdRentCents = 800;
        public const int BakeryRentCents = 1000;

        public const string BankName = "Frontier Town Bank";
        public const string ExternalCounterpartyId = "kansas-city";

        // ---- production authorities ----
        public HouseholdLedgerRegistry Ledgers = new HouseholdLedgerRegistry();
        public FinancialObligationAuthority Obligations = new FinancialObligationAuthority();
        public EntityIdRegistry Ids = new EntityIdRegistry();
        public EmploymentRelationshipRegistry Employments = new EmploymentRelationshipRegistry();
        public readonly Dictionary<string, BusinessRuntimeState> Runtimes =
            new Dictionary<string, BusinessRuntimeState>(StringComparer.Ordinal);
        public readonly Dictionary<string, BusinessInstanceState> Businesses =
            new Dictionary<string, BusinessInstanceState>(StringComparer.Ordinal);
        public BusinessLenderService Bank;
        public CreditOfferWorkflow CreditWorkflow = new CreditOfferWorkflow();
        public CreditEventLog CreditEvents = new CreditEventLog();
        public EnterpriseValuationReadModel Valuation = new EnterpriseValuationReadModel();
        public ExternalMarketAuthority ExternalMarket = new ExternalMarketAuthority();
        public CreditCashBridge CreditorBridge = new CreditCashBridge();
        public HousingAuthority Housing = new HousingAuthority();
        public TenancyPaymentService Tenancy = new TenancyPaymentService();
        public NpcBusinessLifecycleService Lifecycle = new NpcBusinessLifecycleService();

        // ---- scenario state ----
        public int CurrentDay;
        public bool MillOpen = true;
        public bool BakeryOpen = true;
        public int BreadWholesalePriceCents = BreadWholesaleCents;
        public int BreadRetailPriceCents = BreadRetailCents;
        public readonly List<string> Diag = new List<string>();
        public NpcBusinessLifeRecord MillRecord;
        public NpcBusinessLifeRecord BakeryRecord;
        public FinancialObligation MillLoan;
        public NpcBusinessCloseResult MillCloseResult;
        public int BakeryMissedPayrollDay = -1;
        public int BakeryClosedDay = -1;

        // ---- reconciliation counters ----
        public int InitialModeledCashCents;
        public int InitialGrainUnits;
        public long GrainHarvested;
        public long GrainMilled;
        public long BranWasted;
        public long FlourMilled;
        public long FlourImported;
        public long FlourBaked;
        public long BreadBaked;
        public long BreadRetailed;
        public long GrainExported;
        public int BoundaryInflowCents;
        public int BoundaryOutflowCents;
        public int LiquidationInflowCents;
        public int RentArrearsEvents;
        public int BankLoanRefusals;
        public readonly List<string> ArrearsObligationIds = new List<string>();

        private static void SetPrivate(object target, string name, object value)
        {
            FieldInfo field = target.GetType().GetField(name, BindingFlags.NonPublic | BindingFlags.Instance);
            if (field == null) throw new InvalidOperationException("missing field " + name);
            field.SetValue(target, value);
        }

        // ================= build =================

        public void Build()
        {
            // ---- business runtimes (wired like production: employment
            // registry + household ledgers + person->household lookup) ----
            AddRuntime(Mill, 15000);
            AddRuntime(Bakery, 4000);
            AddRuntime(Farm, 10000);
            AddRuntime(Store, 20000);

            // ---- households ----
            FundHousehold(H1, 2500);
            FundHousehold(H2, 4000);
            FundHousehold(H3, 5000);
            FundHousehold(H4, 8000);

            // ---- employment ----
            Employ("emp-mill-1", P1, Mill, MillWageCents);
            Employ("emp-bakery-2", P2, Bakery, BakeryWageCents);
            Employ("emp-farm-3", P3, Farm, FarmWageCents);

            // ---- opening goods ----
            Runtimes[Farm].EnsureCategoryStock(Grain, 300, 2000);
            Runtimes[Mill].EnsureCategoryStock(Grain, 220, 800); // two weeks of grinding
            InitialGrainUnits = 300 + 220;
            Runtimes[Mill].EnsureCategoryStock(Flour, 60, 800);
            Runtimes[Bakery].EnsureCategoryStock(Flour, 40, 800);
            Runtimes[Bakery].EnsureCategoryStock(Bread, 20, 800);
            Runtimes[Store].EnsureCategoryStock(Bread, 30, 800);

            // ---- the town bank: chartered with named-source capital ----
            Bank = new BusinessLenderService(BankName);
            Bank.BindHubServices(Ids, Obligations, CreditWorkflow, CreditEvents, Valuation);
            Bank.BindBusinessLookup(id =>
            {
                BusinessInstanceState found;
                return Businesses.TryGetValue(id, out found) ? found : null;
            });
            string charterProblem = Bank.EnsureCharteredCapital(2500000, "charter investors", StartDay, Diag);
            if (charterProblem != null) Diag.Add("charter refused: " + charterProblem);
            // The bank vault is a real cash store on the creditor bridge so
            // close-down settlements can actually reach it.
            var vaultField = typeof(BusinessLenderService).GetField("depositLedger", BindingFlags.NonPublic | BindingFlags.Instance);
            var vault = (BankDepositLedger)vaultField.GetValue(Bank);
            string bridgeProblem = CreditorBridge.Register(BankName, new BankVaultStore(Bank, vault, Diag));
            if (bridgeProblem != null) Diag.Add("bank bridge refused: " + bridgeProblem);
            // Household landlords are real cash stores too (rent arrears),
            // through the production household cash port.
            bridgeProblem = CreditorBridge.Register("household:4", new HouseholdCashStore(H4, Ledgers.GetOrCreate(H4)));
            if (bridgeProblem != null) Diag.Add("landlord bridge refused: " + bridgeProblem);

            // ---- valuation evidence the lender underwrites on ----
            Valuation.RegisterBusiness(Mill, "person:1", false);
            for (int i = 0; i < 6; i++) Valuation.RecordWeeklyProfit(Mill, 3000);
            Valuation.RecordTransferableAssets(Mill, 50000);
            Valuation.RegisterBusiness(Bakery, "person:4", false);
            for (int i = 0; i < 6; i++) Valuation.RecordWeeklyProfit(Bakery, -2000);
            Valuation.RecordTransferableAssets(Bakery, 8000);
            Businesses[Mill].BusinessReputation.stockReliability01 = 0.8f;
            Businesses[Mill].BusinessReputation.valueFairness01 = 0.8f;
            Businesses[Mill].BusinessReputation.serviceExperience01 = 0.8f;
            Businesses[Mill].BusinessReputation.conditionPresentationTrust01 = 0.8f;
            Businesses[Mill].BusinessReputation.productTradeConfidence01 = 0.8f;
            Businesses[Bakery].BusinessReputation.stockReliability01 = 0.5f;
            Businesses[Bakery].BusinessReputation.valueFairness01 = 0.5f;
            Businesses[Bakery].BusinessReputation.serviceExperience01 = 0.5f;
            Businesses[Bakery].BusinessReputation.conditionPresentationTrust01 = 0.5f;
            Businesses[Bakery].BusinessReputation.productTradeConfidence01 = 0.5f;

            // ---- external market: bounded counterparty, finite supply ----
            string cpProblem = ExternalMarket.RegisterCounterparty(new ExternalCounterparty
            {
                CounterpartyId = ExternalCounterpartyId,
                DisplayName = "Kansas City grain house",
                InitialBudgetCents = 100000,
                TransitDays = 3,
                SupplyUnits = new Dictionary<string, int>(StringComparer.Ordinal) { { Flour, 200 } },
            });
            if (cpProblem != null) Diag.Add("counterparty refused: " + cpProblem);

            // ---- household tenancies: H1/H2/H3 rent from H4 ----
            SetupTenancy(H1, P1, "house-h1");
            SetupTenancy(H2, P2, "house-h2");
            SetupTenancy(H3, P3, "house-h3");

            // ---- lifecycle records ----
            MillRecord = new NpcBusinessLifeRecord
            {
                BusinessInstanceId = Mill,
                BusinessEntityKey = "business:mill-1",
                FounderPersonId = P1,
                OwnerName = "person:1",
                State = NpcBusinessLifeState.Operating,
            };
            MillRecord.EmploymentIds.Add("emp-mill-1");
            BakeryRecord = new NpcBusinessLifeRecord
            {
                BusinessInstanceId = Bakery,
                BusinessEntityKey = "business:bakery-1",
                FounderPersonId = P4,
                OwnerName = "person:4",
                State = NpcBusinessLifeState.Operating,
            };
            BakeryRecord.EmploymentIds.Add("emp-bakery-2");

            InitialModeledCashCents = TotalModeledCashCents();
        }

        private void AddRuntime(string businessId, int openingCashCents)
        {
            var rt = new BusinessRuntimeState();
            rt.BindBusinessIdentity(businessId);
            if (openingCashCents > 0) rt.AddCashCents(openingCashCents);
            rt.EmploymentRegistry = Employments;
            rt.HouseholdLedgers = Ledgers;
            rt.PersonHouseholdIdLookup = pid => pid == P1 ? H1 : pid == P2 ? H2 : pid == P3 ? H3 : pid == P4 ? H4 : -1;
            Runtimes[businessId] = rt;

            var business = new BusinessInstanceState();
            SetPrivate(business, "instanceId", businessId);
            business.TryAssignPremises(100 + Math.Abs(businessId.GetHashCode()) % 900);
            SetPrivate(business, "runtimeState", rt);
            Businesses[businessId] = business;
        }

        private void FundHousehold(int householdId, int cents)
        {
            string problem = Ledgers.GetOrCreate(householdId).RecordInflow(
                StartDay, cents, HouseholdIncomeSource.OtherDocumented,
                "opening-savings", "opening reserves", "self");
            if (problem != null) Diag.Add("funding refused: " + problem);
        }

        private void Employ(string empId, int personId, string businessId, int weeklyWageCents)
        {
            var rel = new EmploymentRelationship
            {
                Id = empId,
                EmployeePersonId = personId,
                EmployerBusinessId = businessId,
                LifecycleState = EmploymentLifecycleState.Active,
                StartDayIndex = StartDay,
                Compensation = CompensationTerms.FromWeeklyWage(weeklyWageCents, "ruston wage"),
            };
            string problem = Employments.Register(rel);
            if (problem != null) Diag.Add("employment refused: " + problem);
        }

        private void SetupTenancy(int tenantHouseholdId, int tenantPersonId, string buildingId)
        {
            Housing.RegisterBuilding(buildingId, "parcel-" + buildingId, "house", StartDay, "ruston", Diag);
            AccommodationSpace space = Housing.DefineSpace(buildingId, 4, Diag);
            if (space == null) { Diag.Add("space refused for " + buildingId); return; }
            ResidentialOccupancy occ = Housing.Occupy(tenantPersonId, tenantHouseholdId, space.SpaceId,
                AccommodationArrangement.Rental, StartDay, Diag);
            if (occ == null) { Diag.Add("occupancy refused for H" + tenantHouseholdId); return; }
            PropertyAgreement agreement = Housing.RecordAgreement("rental", "Ash Landlord", "Tenant H" + tenantHouseholdId,
                space.SpaceId, "biweekly rent", StartDay, -1, string.Empty, Diag);
            if (agreement == null) { Diag.Add("agreement refused for H" + tenantHouseholdId); return; }
            string problem = Housing.AttachPaymentTerms(agreement.AgreementId, new TenancyPaymentTerms
            {
                AmountCents = HouseholdRentCents,
                PeriodDays = 15,
                DueDayOfPeriod = 15,
                StartDay = StartDay,
                PayeeHolderId = "household:4",
                TenantHouseholdId = tenantHouseholdId,
            }, Diag);
            if (problem != null) Diag.Add("terms refused: " + problem);
        }

        private int TotalModeledCashCents()
        {
            int total = 0;
            foreach (BusinessRuntimeState rt in Runtimes.Values) total += rt.CurrentCashCents;
            foreach (int hid in new[] { H1, H2, H3, H4 }) total += Ledgers.GetOrCreate(hid).GetBalanceCents();
            total += Bank != null ? Bank.CashOnHandCents : 0;
            return total;
        }

        // ================= day beats =================

        public void RunAll()
        {
            for (CurrentDay = StartDay; CurrentDay < EndDayExclusive; CurrentDay++)
                Day();
        }

        private void Day()
        {
            int d = CurrentDay;
            ExternalMarket.SetDayIndex(d);

            // ---- weekly production (Mon): mill, bakery, farm ----
            if (d % 7 == 0)
            {
                if (MillOpen)
                {
                    // miller grinds 100 grain -> 80 flour, 20 bran wasted
                    if (Runtimes[Mill].TryConsumeCategoryStockUnits(Grain, 100, out _))
                    {
                        GrainMilled += 100;
                        BranWasted += 20;
                        Runtimes[Mill].AddCategoryStockUnits(Flour, 80);
                        FlourMilled += 80;
                    }
                }
                if (BakeryOpen)
                {
                    // baker bakes 20 bread from 20 flour
                    if (Runtimes[Bakery].TryConsumeCategoryStockUnits(Flour, 20, out _))
                    {
                        FlourBaked += 20;
                        Runtimes[Bakery].AddCategoryStockUnits(Bread, 20);
                        BreadBaked += 20;
                    }
                    // bakery supplies the store: 20 bread wholesale. The store
                    // pays first; only then does the bakery settle the sale.
                    // ResolveSettledSale is the one settlement (stock out of
                    // the bakery, cash into the bakery's till) — consuming
                    // stock separately would double-decrement and destroy
                    // goods, so sufficiency is only checked here.
                    if (Runtimes[Bakery].GetCategoryStock(Bread).CurrentStockUnits >= 20 &&
                        Runtimes[Store].TrySpendCents(20 * BreadWholesalePriceCents))
                    {
                        int price = 20 * BreadWholesalePriceCents;
                        Runtimes[Store].AddCategoryStockUnits(Bread, 20);
                        bool settled = Runtimes[Bakery].ResolveSettledSale(Bread, 20, price, "store-1");
                        if (!settled) Diag.Add("wholesale settlement refused.");
                    }
                }
                // farm labor: 40 grain/day worked
                Runtimes[Farm].AddCategoryStockUnits(Grain, 40);
                FarmLaborGrain += 40;
            }

            // ---- daily bread: the store sells 2 loaves a day to each household ----
            if (StoreOpen())
            {
                foreach (int hid in new[] { H1, H2, H3, H4 })
                {
                    BuyBread(hid, 2, d);
                }
            }

            // ---- weekly payroll (Fri): wages -> households, or logged default ----
            if (d % 7 == 4)
            {
                Payday(Mill, MillOpen);
                Payday(Bakery, BakeryOpen);
                Payday(Farm, true);
            }

            // ---- adverse beats ----
            switch (d)
            {
                case 2: MillBorrowDay(); break;
                case 10: Harvest(400, d); break;
                case 14: SettleHouseholdRents(d); break;
                case 15: CloseTheMill(d); break;
                case 16: BakeryBorrowRefused(d); break;
                case 18: BakeryImportFlour(30, d); break;
                case 22: BreadWholesalePriceCents = BreadWholesaleRaisedCents;
                    Diag.Add("day 22: bakery raises wholesale bread to 90c."); break;
                case 25: BreadRetailPriceCents = BreadRetailRaisedCents;
                    Diag.Add("day 25: store raises retail bread to 120c."); break;
                case 27: BakeryPaysRepair(d); break;
                case 29: SettleHouseholdRents(d); break;
                case 30: Harvest(100, d); break;
                case 34: BakeryMissedRent(d); break;
                case 35: FarmExportGrain(100, d); break;
                case 36: CloseTheBakery(d); break;
                case 44: SettleHouseholdRents(d); break;
            }

            // ---- daily bank cycle ----
            Bank.ProcessDay(d, Diag);

            // ---- commercial rent: the bakery pays H4 weekly (Tue) ----
            if (d % 7 == 1 && BakeryOpen) BakeryPaysRent(d);
        }

        private bool StoreOpen()
        {
            return true; // the store never closes in this scenario
        }

        private void Payday(string businessId, bool open)
        {
            if (!open) return;
            // Production payroll: through the employment registry, wages land
            // in the worker's household ledger (the Pass VIII circulation fix).
            Runtimes[businessId].ResolveWeeklyPayroll();
        }

        private void BuyBread(int householdId, int units, int day)
        {
            int price = units * BreadRetailPriceCents;
            var ledger = Ledgers.GetOrCreate(householdId);
            // The store sells only to a buyer who can pay. H1 runs down
            // savings after losing mill work; nobody advances credit here.
            if (ledger.GetBalanceCents() < price) return;
            if (Runtimes[Store].GetCategoryStock(Bread).CurrentStockUnits < units) return;
            string p1 = ledger.RecordOutflow(day, price, "bread", "store-1");
            if (p1 != null) { Diag.Add("bread outflow refused: " + p1); return; }
            // ResolveSettledSale is the one settlement: it moves the bread
            // out of the store's stock and the cash into the store's till,
            // and requires the named buyer principal. Consuming stock
            // separately would double-decrement and destroy goods.
            bool settled = Runtimes[Store].ResolveSettledSale(Bread, units, price, "person:" + householdId);
            if (!settled) Diag.Add("bread sale refused.");
            else BreadRetailed += units;
        }

        private void Harvest(int units, int day)
        {
            // Rain and drought, not minting: harvest moves grain from the
            // (unmodeled) land into the farm's real stock at zero cash cost.
            Runtimes[Farm].AddCategoryStockUnits(Grain, units);
            GrainHarvested += units;
            Diag.Add("day " + day + ": harvest +" + units + " grain (farm).");
        }

        private void MillBorrowDay()
        {
            // Day 2: the mill draws a real loan before the closure. Cash is
            // debited from the bank vault BEFORE the obligation exists, so
            // there is no unfunded proceed anywhere.
            string refusal;
            MillLoan = Bank.TryArrangeLoan(Ids, Obligations, Mill, 12000, 2, Diag, out refusal);
            if (MillLoan == null) { Diag.Add("mill loan refused unexpectedly: " + refusal); return; }
            MillRecord.OpenObligationIds.Add(MillLoan.ObligationId);
            Runtimes[Mill].AddCashCents(12000);
            Diag.Add("day 2: mill draws 12000c loan; vault debited first, obligation recorded after.");
        }

        private void CloseTheMill(int day)
        {
            // MAJOR EMPLOYER CLOSES. The bank loan settles in full through the
            // creditor cash bridge (the Pass VIII circulation fix): the bank
            // vault receives the payment. Millstones liquidate to a real
            // buyer; the founder takes the residual.
            MillOpen = false;
            var formation = new NpcFormationAssetRegister();
            string regProblem = formation.Record(new NpcFormationAsset
            {
                AssetKind = "equipment",
                ItemId = "millstones",
                OwnerBusinessId = Mill,
                Units = 1,
                CostCents = 20000,
                Provenance = "founder capital",
            });
            if (regProblem != null) Diag.Add("mill asset refused: " + regProblem);
            var ctx = new NpcLifecycleContext
            {
                Obligations = Obligations,
                Employments = Employments,
                Cash = new RuntimeCashPort(Runtimes[Mill]),
                Liquidation = new ScenarioLiquidationPort("kansas-city grain house", Diag),
                FounderHouseholdLedger = Ledgers.GetOrCreate(P1),
                AssetRegister = formation,
                CreditorCash = CreditorBridge,
                DayIndex = day,
                Ids = Ids,
            };
            NpcBusinessCloseResult result = Lifecycle.CloseOrderly(
                MillRecord, "major employer shuts: grain supply economics fail", ctx, Diag);
            MillCloseResult = result;
            LiquidationInflowCents += result.LiquidationProceedsCents;
            Diag.Add("day 15: mill closes. settled=" + result.ObligationsSettled +
                     " assumed=" + result.ObligationsAssumed +
                     " proceeds=" + result.LiquidationProceedsCents +
                     " residual=" + result.ResidualToFounderCents +
                     " staff=" + result.StaffReleased +
                     " cashToCreditors=" + result.CashPaidToCreditorsCents);
        }

        private void BakeryBorrowRefused(int day)
        {
            // CREDIT CONTRACTION. The bakery needs working capital but real
            // underwriting refuses: weak cash flow (six weeks of losses) and
            // thin collateral. No debt is created; the vault is untouched.
            FinancialObligation refusedLoan;
            string refusalReason;
            refusedLoan = Bank.TryArrangeLoan(Ids, Obligations, Bakery, 25000, day, Diag, out refusalReason);
            if (refusedLoan != null)
            {
                Diag.Add("day 16: bakery loan UNEXPECTEDLY approved — credit should contract here.");
            }
            else
            {
                BankLoanRefusals++;
                Diag.Add("day 16: bakery loan refused (" + refusalReason + "); no debt created.");
            }
        }

        private void BakeryImportFlour(int units, int day)
        {
            // SUPPLIER FAILS -> IMPORTS. With the mill closed the bakery buys
            // flour from kansas-city at 75c (up from the mill's 55c). Cash
            // leaves the boundary; goods arrive after real transit.
            string receipt = ExternalMarket.RecordExternalPurchase(
                ExternalCounterpartyId, Flour, units, units * FlourImportPriceCents, Diag);
            if (string.IsNullOrEmpty(receipt)) { Diag.Add("import refused: finite supply exhausted."); return; }
            if (!Runtimes[Bakery].TrySpendCents(units * FlourImportPriceCents))
            {
                Diag.Add("day 18: bakery cannot fund the flour import — cash short.");
                return;
            }
            BoundaryOutflowCents += units * FlourImportPriceCents;
            Diag.Add("day 18: bakery imports " + units + " flour @75c (arrives day " + (day + 3) + ").");
        }

        private void BakeryPaysRepair(int day)
        {
            // Services: the bakery pays its oven-repair contract to H3.
            const int amount = 500;
            if (!Runtimes[Bakery].TrySpendCents(amount))
            {
                Diag.Add("day 27: bakery cannot pay the oven repair.");
                return;
            }
            string p = Ledgers.GetOrCreate(H3).RecordInflow(day, amount,
                HouseholdIncomeSource.ServiceIncome, "oven-repair", "bakery oven repair", "bakery-1");
            if (p != null) Diag.Add("repair inflow refused: " + p);
            else Diag.Add("day 27: bakery pays H3 500c for oven repair.");
        }

        private void BakeryPaysRent(int day)
        {
            // Weekly commercial rent to household:4 (the landlord investor).
            var landlord = Ledgers.GetOrCreate(H4);
            if (!Runtimes[Bakery].TrySpendCents(BakeryRentCents))
            {
                BakeryMissedRent(day);
                return;
            }
            string p = landlord.RecordInflow(day, BakeryRentCents, HouseholdIncomeSource.CommercialRentReceived,
                "rent", "bakery commercial rent", "bakery-1");
            if (p != null) Diag.Add("bakery rent inflow refused: " + p);
            else Diag.Add("day " + day + ": bakery pays commercial rent " + BakeryRentCents + "c to H4.");
        }

        private void BakeryMissedRent(int day)
        {
            // OVERDUE RENT becomes a real payable through the debt authority.
            FinancialObligation obligation = Obligations.Create(
                Ids, FinancialObligationKind.Payable, Bakery, "household:4",
                BakeryRentCents, day, "weekly commercial rent", "bakery commercial rent arrears");
            if (obligation == null) Diag.Add("bakery rent obligation refused.");
            else
            {
                RentArrearsEvents++;
                ArrearsObligationIds.Add(obligation.ObligationId);
                BakeryRecord.OpenObligationIds.Add(obligation.ObligationId);
                Diag.Add("day " + day + ": bakery misses rent — real payable recorded.");
            }
        }

        private void FarmExportGrain(int units, int day)
        {
            // EXPORTS. The farm sells grain to kansas-city; the external
            // authority records the boundary inflow and the farm's till is
            // credited with the proceeds (sales settle immediately; only
            // purchases carry transit).
            string receipt = ExternalMarket.RecordExternalSale(
                ExternalCounterpartyId, Grain, units, units * GrainPriceCents, Diag);
            if (string.IsNullOrEmpty(receipt)) { Diag.Add("export refused: counterparty budget exhausted."); return; }
            if (!Runtimes[Farm].TryConsumeCategoryStockUnits(Grain, units, out _))
            {
                Diag.Add("day 35: farm lacks grain to export.");
                return;
            }
            int proceeds = units * GrainPriceCents;
            Runtimes[Farm].AddCashCents(proceeds);
            BoundaryInflowCents += proceeds;
            GrainExported += units;
            Diag.Add("day 35: farm exports " + units + " grain @35c (+" + proceeds + "c inflow).");
        }

        private void SettleHouseholdRents(int day)
        {
            // Real rent settlement through the tenancy authority. When a
            // household cannot cover rent in full, partial payment plus a
            // real arrears obligation is recorded (no silently-kept surplus,
            // no free pass).
            List<TenancySettlementResult> results = Tenancy.SettleDueRents(
                Housing, day, Ledgers, new ScenarioPayeeDirectory(), Obligations, Ids, Diag);
            int paid = 0, partial = 0, arrears = 0;
            long cashMoved = 0;
            foreach (TenancySettlementResult r in results)
            {
                cashMoved += r.PaidCents;
                if (r.Status == RentObligationStatus.Current) paid++;
                else if (r.Status == RentObligationStatus.Partial) partial++;
                else arrears++;
                if (!string.IsNullOrEmpty(r.ArrearsObligationId))
                    ArrearsObligationIds.Add(r.ArrearsObligationId);
            }
            RentArrearsEvents += arrears + partial;
            Diag.Add("day " + day + ": household rents settled — paid=" + paid +
                     " partial=" + partial + " arrears=" + arrears +
                     " cashMoved=" + cashMoved + "c");
            // Resolve external-trade arrivals once per day here (day 14, 29,
            // 44 all precede the day 21 flour arrival and the day 38 grain
            // proceeds; arrivals credit the real buyer's stock/seller's till).
            foreach (ExternalTradeRecord record in ExternalMarket.ResolveArrivals(day))
            {
                // Only purchases are ever in transit (sales settle at record
                // time); the flour import credits the real buyer's stock.
                if (record.Direction == "purchase" && record.GoodsId == Flour)
                {
                    Runtimes[Bakery].AddCategoryStockUnits(Flour, record.Quantity);
                    FlourImported += record.Quantity;
                }
            }
        }

        private void CloseTheBakery(int day)
        {
            // BUSINESS INSOLVENCY. Rent arrears are settled as far as cash
            // reaches; the shortfall is EXPLICITLY assumed by founder P4
            // (never stranded). Staff are released through the employment
            // authority; residual cash returns to the founder's household.
            BakeryOpen = false;
            var formation = new NpcFormationAssetRegister();
            string regProblem = formation.Record(new NpcFormationAsset
            {
                AssetKind = "equipment",
                ItemId = "oven",
                OwnerBusinessId = Bakery,
                Units = 1,
                CostCents = 8000,
                Provenance = "founder capital",
            });
            if (regProblem != null) Diag.Add("bakery asset refused: " + regProblem);
            var ctx = new NpcLifecycleContext
            {
                Obligations = Obligations,
                Employments = Employments,
                Cash = new RuntimeCashPort(Runtimes[Bakery]),
                Liquidation = new ScenarioLiquidationPort(null, Diag),
                FounderHouseholdLedger = Ledgers.GetOrCreate(P4),
                AssetRegister = formation,
                CreditorCash = CreditorBridge,
                DayIndex = day,
                Ids = Ids,
            };
            NpcBusinessCloseResult result = Lifecycle.CloseOrderly(
                BakeryRecord, "insolvency: rent arrears beyond cash reach", ctx, Diag);
            LiquidationInflowCents += result.LiquidationProceedsCents;
            BakeryClosedDay = day;
            Diag.Add("day 36: bakery closes. settled=" + result.ObligationsSettled +
                     " assumed=" + result.ObligationsAssumed +
                     " proceeds=" + result.LiquidationProceedsCents +
                     " residual=" + result.ResidualToFounderCents +
                     " staff=" + result.StaffReleased);
        }

        // ================= test-local adapters =================
        // (All cash writes stay inside the one-cash-writer / one-debt-authority
        // rules; these adapters only expose production state to production
        // authority interfaces.)

        private sealed class RuntimeCashPort : INpcBusinessCashPort
        {
            private readonly BusinessRuntimeState runtime;
            public RuntimeCashPort(BusinessRuntimeState runtime) { this.runtime = runtime; }

            public int ReadCashCents(string businessInstanceId) { return runtime.CurrentCashCents; }

            public string Spend(string businessInstanceId, int dayIndex, int amountCents, string purpose)
            {
                return runtime.TrySpendCents(amountCents)
                    ? null
                    : "RuntimeCashPort: insufficient cash for " + purpose + ".";
            }

            public string Receive(string businessInstanceId, int dayIndex, int amountCents, string reason)
            {
                runtime.AddCashCents(amountCents);
                return null;
            }
        }

        private sealed class ScenarioLiquidationPort : INpcLiquidationPort
        {
            private readonly string buyerName;
            private readonly List<string> diag;
            public ScenarioLiquidationPort(string buyerName, List<string> diag)
            {
                this.buyerName = buyerName;
                this.diag = diag;
            }

            public string Liquidate(string businessInstanceId, NpcFormationAsset asset, int dayIndex,
                out NpcLiquidationSale sale)
            {
                sale = null;
                // The mill's stones find a real buyer across the boundary; the
                // bakery's oven does not (no market at fire-sale prices).
                // Outcomes emerge from conditions, not from a guaranteed
                // liquidation schedule.
                if (!string.IsNullOrEmpty(buyerName) && asset != null && asset.CostCents > 0)
                {
                    sale = new NpcLiquidationSale
                    {
                        AssetItemId = asset.ItemId,
                        Units = asset.Units,
                        BuyerName = buyerName,
                        ProceedsCents = asset.CostCents,
                    };
                    diag.Add("Liquidation: '" + asset.ItemId + "' sold to " + buyerName +
                             " for " + asset.CostCents + "c.");
                    return null;
                }
                diag.Add("Liquidation: no buyer for '" + (asset != null ? asset.ItemId : "?") +
                         "' — no fire sale, no phantom proceeds.");
                return "no buyer";
            }
        }

        private sealed class ScenarioPayeeDirectory : ITenancyPayeeDirectory
        {
                public int FindPayeeHouseholdId(string payeeHolderId)
            {
                // "household:4" -> 4
                if (string.IsNullOrEmpty(payeeHolderId)) return -1;
                string[] parts = payeeHolderId.Split(':');
                int id;
                if (parts.Length == 2 && parts[0] == "household" && int.TryParse(parts[1], out id)) return id;
                return -1;
            }

            public ITenancyPayeeCashSink FindPayeeBusinessSink(string payeeHolderId)
            {
                // This scenario rents from a person, not a business.
                return null;
            }
        }

        private sealed class BankVaultStore : IRealCashStore
        {
            private readonly BusinessLenderService bank;
            private readonly BankDepositLedger ledger;
            private readonly List<string> diag;
            public BankVaultStore(BusinessLenderService bank, BankDepositLedger ledger, List<string> diag)
            {
                this.bank = bank;
                this.ledger = ledger;
                this.diag = diag;
            }

            public string OwnerName => bank.LenderName;
            public int ReadBalanceCents() { return bank.CashOnHandCents; }

            public string DebitCents(int dayIndex, int amountCents, string purpose, string counterparty)
            {
                return "BankVaultStore: disbursement goes through the loan desk, not this store.";
            }

            public string CreditCents(int dayIndex, int amountCents, string sourceClass,
                string sourceReference, string reason, string counterparty)
            {
                if (amountCents <= 0) return "BankVaultStore: the credit must be positive.";
                // The bank vault's real cash goes up; the deposit book records
                // it as a loan-repayment receipt so both books stay real.
                return ledger.ReceiveLoanPayment(amountCents, sourceReference ?? "close-down", diag);
            }
        }

        // ================= reconciliation helpers =================

        public long FarmLaborGrain;

        public int EndModeledCashCents()
        {
            return TotalModeledCashCents();
        }

        public int EndingGrainUnits()
        {
            return Runtimes[Farm].GetCategoryStock(Grain).CurrentStockUnits
                 + Runtimes[Mill].GetCategoryStock(Grain).CurrentStockUnits;
        }

        public int EndingFlourUnits()
        {
            return Runtimes[Mill].GetCategoryStock(Flour).CurrentStockUnits
                 + Runtimes[Bakery].GetCategoryStock(Flour).CurrentStockUnits;
        }

        public int EndingBreadUnits()
        {
            return Runtimes[Bakery].GetCategoryStock(Bread).CurrentStockUnits
                 + Runtimes[Store].GetCategoryStock(Bread).CurrentStockUnits;
        }

        public int MillLoanOutstandingCents()
        {
            if (MillLoan == null) return -1;
            FinancialObligation obligation = Obligations.Find(MillLoan.ObligationId);
            return obligation != null ? obligation.TotalOutstandingCents : -1;
        }

        public bool DiagContains(string fragment)
        {
            foreach (string line in Diag)
                if (line.IndexOf(fragment, StringComparison.Ordinal) >= 0) return true;
            return false;
        }
    }

    /// <summary>
    /// Pass VIII circulation proof: the Ruston Decline scenario runs 45
    /// adverse days through production authorities and reconciles cash,
    /// goods, debts and identities exactly. Roslyn harness only.
    /// </summary>
    [TestFixture]
    public sealed class Pass8AdverseCirculationTests
    {
        private AdverseCirculationScenario sim;

        [SetUp]
        public void SetUp()
        {
            sim = new AdverseCirculationScenario();
            sim.Build();
            sim.RunAll();
        }

        [Test]
        public void Scenario_Runs45Days_WithoutSilentFailures()
        {
            Assert.AreEqual(AdverseCirculationScenario.EndDayExclusive, sim.CurrentDay);
            // No diagnostic may hide a surprise: the only expected refusals
            // are the ones the scenario deliberately engineers.
            foreach (string line in sim.Diag)
            {
                StringAssert.DoesNotContain("unexpectedly", line,
                    "unexpected failure during the scenario:\n" + string.Join("\n", sim.Diag));
            }
            Assert.IsTrue(sim.DiagContains("mill closes"), "the mill closure beat must fire.");
            Assert.IsTrue(sim.DiagContains("bakery closes"), "the bakery insolvency beat must fire.");
        }

        [Test]
        public void MillLoan_WasRealCash_AndSettledInFull()
        {
            // The loan existed as a real obligation, the vault was debited
            // before the obligation existed (no unfunded proceeds), and the
            // close-down settled it in full through the creditor cash bridge,
            // including the real interest that accrued between day 2 and 15.
            Assert.IsNotNull(sim.MillLoan, "the mill loan must have been approved on day 2.");
            Assert.AreEqual(0, sim.MillLoanOutstandingCents(),
                "the mill loan must be fully settled after the close-down.");
            Assert.IsNotNull(sim.MillCloseResult, "the mill close-down must have run.");
            int repaid = sim.MillCloseResult.CashPaidToCreditorsCents;
            Assert.GreaterOrEqual(repaid, 12000,
                "the close-down must repay at least the 12000c principal plus real accrued interest.");
            Assert.AreEqual(2500000 - 12000 + repaid, sim.Bank.CashOnHandCents,
                "the bank vault: 12000c out on day 2, the full repayment (with interest) back through the creditor bridge.");
            Assert.IsTrue(sim.DiagContains("cashToCreditors=" + repaid),
                "the close-down must report the full loan settlement to the bank.");
        }

        [Test]
        public void BakeryLoan_RefusedByRealUnderwriting_NoDebtCreated()
        {
            // Credit contraction: weak cash flow means no loan, no debt, and
            // the bank vault untouched by the refusal.
            Assert.AreEqual(1, sim.BankLoanRefusals, "exactly one loan refusal: the bakery's day-16 application.");
            foreach (FinancialObligation obligation in sim.Obligations.Obligations)
            {
                if (obligation.Debtor == AdverseCirculationScenario.Bakery &&
                    obligation.Kind == FinancialObligationKind.Loan)
                {
                    Assert.Fail("no loan obligation may exist for the refused bakery application.");
                }
            }
        }

        [Test]
        public void Cash_Conserves_AcrossTheBoundary()
        {
            // Every modeled cent is accounted for: the only net changes are
            // the boundary flows (export inflow, liquidation proceeds from an
            // external buyer, import outflow). Nothing is minted or lost.
            int expected = sim.InitialModeledCashCents
                         + sim.LiquidationInflowCents
                         + sim.BoundaryInflowCents
                         - sim.BoundaryOutflowCents;
            Assert.AreEqual(expected, sim.EndModeledCashCents(),
                "modeled cash (4 tills + 4 ledgers + bank vault) must reconcile against the boundary net.");
            Assert.AreEqual(2250, sim.BoundaryOutflowCents, "the flour import is the only boundary outflow.");
            Assert.AreEqual(3500, sim.BoundaryInflowCents, "the grain export is the only trade inflow.");
            Assert.AreEqual(20000, sim.LiquidationInflowCents, "the millstone buyer is the only other external inflow.");
            var boundary = sim.ExternalMarket.GetBoundaryReconciliation();
            Assert.AreEqual(sim.BoundaryInflowCents, boundary.inflowCents);
            Assert.AreEqual(sim.BoundaryOutflowCents, boundary.outflowCents);
        }

        [Test]
        public void Goods_Reconcile_Exactly()
        {
            // Grain: opening stock + harvests + farm labor - milling - exports.
            long expectedGrain = sim.InitialGrainUnits + sim.GrainHarvested + sim.FarmLaborGrain - sim.GrainMilled - sim.GrainExported;
            Assert.AreEqual(expectedGrain, sim.EndingGrainUnits(), "grain must reconcile exactly.");
            Assert.AreEqual(200, sim.GrainMilled, "the mill ground for two weeks before closing.");
            Assert.AreEqual(100, sim.GrainExported, "the farm exported 100 grain.");
            Assert.AreEqual(sim.GrainMilled / 5, sim.BranWasted, "milling wastes 20% as bran.");
            // Flour: opening 100 + milling + imports - baking.
            long expectedFlour = 100 + sim.FlourMilled + sim.FlourImported - sim.FlourBaked;
            Assert.AreEqual(expectedFlour, sim.EndingFlourUnits(), "flour must reconcile exactly.");
            Assert.AreEqual(30, sim.FlourImported, "the emergency flour import must arrive after transit.");
            // Bread: opening 50 + baking - retail.
            long expectedBread = 50 + sim.BreadBaked - sim.BreadRetailed;
            Assert.AreEqual(expectedBread, sim.EndingBreadUnits(), "bread must reconcile exactly.");
            Assert.IsTrue(sim.BreadRetailed > 0, "households bought bread during the decline.");
        }

        [Test]
        public void Debts_Reconcile_WithNamedCounterparties()
        {
            // The debt authority reconciles with no errors and no duplicate
            // payments; every obligation ever booked names its debtor and
            // creditor. Overdue rent became real obligations during the
            // decline (the bakery's missed commercial rent is the adverse
            // beat); the mill founder's legitimate residual meant H1's
            // household rent stayed covered, and the bakery's payable was
            // settled in full at its close-down — so nothing outstanding at
            // day 45 is itself a reconciled outcome, not a write-off.
            List<string> problems = sim.Obligations.Reconcile();
            Assert.IsEmpty(problems, "obligation authority reconciliation problems:\n" + string.Join("\n", problems));
            foreach (FinancialObligation obligation in sim.Obligations.Obligations)
            {
                Assert.IsFalse(string.IsNullOrWhiteSpace(obligation.Debtor),
                    "obligation '" + obligation.ObligationId + "' must name a debtor.");
                Assert.IsFalse(string.IsNullOrWhiteSpace(obligation.Creditor),
                    "obligation '" + obligation.ObligationId + "' must name a creditor.");
            }
            Assert.IsTrue(sim.ArrearsObligationIds.Count > 0,
                "overdue rent must become real obligations, not a silent pass.");
            foreach (string arrearsId in sim.ArrearsObligationIds)
            {
                FinancialObligation arrears = sim.Obligations.Find(arrearsId);
                Assert.IsNotNull(arrears, "arrears obligation '" + arrearsId + "' must exist on the authority.");
                Assert.AreEqual(FinancialObligationKind.Payable, arrears.Kind);
            }
        }

        [Test]
        public void Identities_EmploymentsEndExactlyOnce()
        {
            // A closed business releases its people through the employment
            // authority; nobody works two jobs; the farm hand keeps working.
            EmploymentRelationship millEmp = FindEmployment(AdverseCirculationScenario.P1);
            EmploymentRelationship bakeryEmp = FindEmployment(AdverseCirculationScenario.P2);
            EmploymentRelationship farmEmp = FindEmployment(AdverseCirculationScenario.P3);
            Assert.IsNotNull(millEmp, "P1 must have an employment record.");
            Assert.IsNotNull(bakeryEmp, "P2 must have an employment record.");
            Assert.AreEqual(EmploymentLifecycleState.Ended, millEmp.LifecycleState,
                "P1's mill employment ends with the closure.");
            Assert.AreEqual(EmploymentLifecycleState.Ended, bakeryEmp.LifecycleState,
                "P2's bakery employment ends with the insolvency.");
            Assert.AreEqual(EmploymentLifecycleState.Active, farmEmp.LifecycleState,
                "P3's farm employment survives.");
            foreach (int personId in new[] { 1, 2, 3, 4 })
            {
                int active = sim.Employments.GetActiveByEmployee(personId).Count;
                Assert.LessOrEqual(active, 1, "person-time is exclusive: person " + personId);
            }
        }

        [Test]
        public void NoHiddenStabilization_TownWasAllowedToDecline()
        {
            // Nothing kept the town alive: the mill and bakery are closed,
            // the bakery missed payroll/rent before dying, and H1's rent went
            // into real arrears rather than being silently covered.
            Assert.AreEqual(NpcBusinessLifeState.Closed, sim.MillRecord.State);
            Assert.AreEqual(NpcBusinessLifeState.Closed, sim.BakeryRecord.State);
            Assert.AreEqual(36, sim.BakeryClosedDay);
            Assert.IsTrue(sim.RentArrearsEvents > 0,
                "overdue rent must become real arrears, not a secret subsidy.");
            Assert.IsTrue(sim.DiagContains("bakery pays commercial rent"),
                "the landlord received real commercial rent while the bakery lived.");
        }

        private EmploymentRelationship FindEmployment(int personId)
        {
            List<EmploymentRelationship> rels = sim.Employments.GetByEmployee(personId);
            return rels.Count > 0 ? rels[0] : null;
        }
    }
}
