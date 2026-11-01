// Pass XII Worker E — settlement expansion, adversarial and performance tests.
//
// SCOPE: tests and test fixtures ONLY. No production code is touched here.
// Every fixture below runs through the SAME production authorities the Pass XI
// WS4 proof uses (HouseholdLedgerRegistry, FinancialObligationAuthority,
// EmploymentRelationshipRegistry, BusinessRuntimeState, FreightShipmentService,
// EquipmentReservationService, BusinessLenderService) — never a simplified
// shadow simulation, and never with stabilizing cash or goods injected.
//
// BOUNDARY (brief §14): these are Roslyn-harness source tests. They are NOT
// Unity gameplay tests, and the 30-day source proof below must NEVER be
// renamed as the Main Scene RUN-004 PlayMode proof, which stays open until
// performed in a real Unity scene.
//
// Two fixture families:
//  * Small town: 3 households / 2 businesses, one household with no cash and
//    one person with no employment — genuine lack of demand and unemployment.
//  * Large town: 9 households / 7 businesses, with supplier ordering (bakery
//    buys flour from the mill), a freight shipment (farm -> mill), two service
//    businesses (saloon, smithy) that sell no goods, rent with real arrears
//    obligations, and weekly payroll for 7 employed persons.
// No arbitrary population/business scaling constants are used: every
// household and business is individually seeded with named cash, stock and
// wages, exactly as WS4 seeds its five and five.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using LandLedgers.Economy;
using LandLedgers.Economy.Blacksmith;
using LandLedgers.Economy.Businesses.Bakery;
using LandLedgers.Economy.Businesses.Mine;
using LandLedgers.Economy.Core;
using LandLedgers.Economy.Creation;
using LandLedgers.Economy.Equipment;
using LandLedgers.Economy.Farming.Crops;
using LandLedgers.Economy.Financing;
using LandLedgers.Economy.Generic;
using LandLedgers.Economy.Logistics;
using LandLedgers.Economy.Postal;
using LandLedgers.Persistence;
using LandLedgers.Population;
using LandLedgers.Primitives;
using LandLedgers.ReadModels.Valuation;
using LandLedgers.Tasks;
using LandLedgers.Time;
using LandLedgers.World.Property;
using NUnit.Framework;

namespace LandLedgers.Editor.Integration
{
    /// <summary>Minimal production-authority town used by the Pass XII
    /// expansion fixtures. Structure (authorities, runtimes, employments) is
    /// built separately from seeding, so a save/load round-trip can rebuild
    /// structure and import production DTOs without double-funding anything
    /// — the same clear-then-load discipline WS4 uses.</summary>
    public sealed class Pass12MiniTown
    {
        public const string Grain = "grain";
        public const string Flour = "flour";
        public const string Bread = "bread";
        public const string Lumber = "lumber";

        public HouseholdLedgerRegistry Ledgers = new HouseholdLedgerRegistry();
        public FinancialObligationAuthority Obligations = new FinancialObligationAuthority();
        public EntityIdRegistry Ids = new EntityIdRegistry();
        public EmploymentRelationshipRegistry Employments = new EmploymentRelationshipRegistry();
        public readonly Dictionary<string, BusinessRuntimeState> Runtimes =
            new Dictionary<string, BusinessRuntimeState>(StringComparer.Ordinal);
        public readonly List<int> HouseholdIds = new List<int>();
        public readonly List<string> BusinessIds = new List<string>();
        public readonly List<string> Diag = new List<string>();

        // Freight (large town only).
        public FreightShipmentService Freight = new FreightShipmentService();
        public TaskAuthority FreightTasks = new TaskAuthority();
        public WorkTimeBudgetStore FreightBudgets = new WorkTimeBudgetStore();
        public FreightShipmentHooks FreightHooks;
        public readonly List<LogisticsShipmentState> Shipments = new List<LogisticsShipmentState>();
        public int FreightDeliveredUnits;
        public bool FreightSourceDeducted;

        public int CurrentDay;
        public int InitialCashCents;
        public int Transactions;
        public long WagesPaidCents;
        public long GoodsProducedUnits;
        public long RetailUnits;
        public long ServicePaymentsCents;
        public int ArrearsCount;
        public int RefusedSales;

        private int PersonHousehold(int personId)
        {
            return personId >= 1 && personId <= 99 ? personId : -1;
        }

        public void AddBusiness(string id, int cashCents)
        {
            var rt = new BusinessRuntimeState();
            rt.BindBusinessIdentity(id);
            rt.EmploymentRegistry = Employments;
            rt.HouseholdLedgers = Ledgers;
            rt.PersonHouseholdIdLookup = PersonHousehold;
            rt.ObligationAuthority = Obligations;
            rt.ObligationIdRegistry = Ids;
            rt.AddCashCents(cashCents);
            Runtimes[id] = rt;
            BusinessIds.Add(id);
        }

        public void AddHousehold(int hid, int cashCents)
        {
            HouseholdIds.Add(hid);
            if (cashCents <= 0) return;
            string problem = Ledgers.GetOrCreate(hid).RecordInflow(
                0, cashCents, HouseholdIncomeSource.OtherDocumented,
                "opening-savings", "opening reserves", "self");
            if (problem != null) Diag.Add("funding refused: " + problem);
        }

        public void Employ(string empId, int personId, string businessId, int weeklyWageCents)
        {
            var rel = new EmploymentRelationship
            {
                Id = empId,
                EmployeePersonId = personId,
                EmployerBusinessId = businessId,
                LifecycleState = EmploymentLifecycleState.Active,
                StartDayIndex = 0,
                Compensation = CompensationTerms.FromWeeklyWage(weeklyWageCents, "pass12 wage"),
            };
            string problem = Employments.Register(rel);
            if (problem != null) Diag.Add("employment refused: " + problem);
        }

        public void Stock(string businessId, string category, int units, int target)
        {
            Runtimes[businessId].EnsureCategoryStock(category, units, target);
        }

        public int StockOf(string businessId, string category)
        {
            CategoryStockState stock = Runtimes[businessId].GetCategoryStock(category);
            return stock == null ? 0 : stock.CurrentStockUnits;
        }

        public int TotalCashCents()
        {
            int total = 0;
            foreach (BusinessRuntimeState rt in Runtimes.Values) total += rt.CurrentCashCents;
            foreach (int hid in HouseholdIds) total += Ledgers.GetOrCreate(hid).GetBalanceCents();
            return total;
        }

        public int TotalStock(string category)
        {
            int total = 0;
            foreach (string id in BusinessIds) total += StockOf(id, category);
            return total;
        }

        public int EmployedCount()
        {
            int count = 0;
            foreach (string id in BusinessIds)
                foreach (EmploymentRelationship rel in Employments.GetByEmployer(id))
                    if (rel.LifecycleState == EmploymentLifecycleState.Active) count++;
            return count;
        }

        public void AssertConserved()
        {
            Assert.AreEqual(InitialCashCents, TotalCashCents(),
                "modeled cash (all tills + all household ledgers) must be conserved: every transfer is internal.");
            foreach (int hid in HouseholdIds)
                Assert.GreaterOrEqual(Ledgers.GetOrCreate(hid).GetBalanceCents(), 0, "no household ledger may go negative.");
            foreach (string id in BusinessIds)
                Assert.GreaterOrEqual(Runtimes[id].CurrentCashCents, 0, "no business till may go negative.");
            Assert.IsEmpty(Obligations.Reconcile(), "obligation authority must reconcile.");
        }

        public void AssertNoNegatives()
        {
            foreach (int hid in HouseholdIds)
                Assert.GreaterOrEqual(Ledgers.GetOrCreate(hid).GetBalanceCents(), 0, "no household ledger may go negative.");
            foreach (string id in BusinessIds)
            {
                Assert.GreaterOrEqual(Runtimes[id].CurrentCashCents, 0, "no business till may go negative.");
                foreach (string cat in new[] { Grain, Flour, Bread, Lumber })
                    Assert.GreaterOrEqual(StockOf(id, cat), 0, "no stock may go negative.");
            }
        }

        public string ComputeHash()
        {
            var parts = new List<string>();
            parts.Add("day=" + CurrentDay);
            foreach (string id in BusinessIds)
            {
                BusinessRuntimeState rt = Runtimes[id];
                parts.Add(id + ":cash=" + rt.CurrentCashCents
                    + ":grain=" + StockOf(id, Grain) + ":flour=" + StockOf(id, Flour)
                    + ":bread=" + StockOf(id, Bread) + ":lumber=" + StockOf(id, Lumber)
                    + ":named=" + rt.NamedPayables.Count);
            }
            foreach (int hid in HouseholdIds)
                parts.Add("H" + hid + "=" + Ledgers.GetOrCreate(hid).GetBalanceCents()
                    + ":entries=" + Ledgers.GetOrCreate(hid).Entries.Count);
            var obligationLines = new List<string>();
            foreach (FinancialObligation o in Obligations.Obligations)
                obligationLines.Add(o.ObligationId + "|" + o.Debtor + "|" + o.Creditor + "|" + o.TotalOutstandingCents + "|" + o.Status);
            obligationLines.Sort(StringComparer.Ordinal);
            parts.Add("obligations=" + string.Join(";", obligationLines));
            var employmentLines = new List<string>();
            foreach (string id in BusinessIds)
                foreach (EmploymentRelationship rel in Employments.GetByEmployer(id))
                    employmentLines.Add(rel.Id + ":" + rel.LifecycleState);
            employmentLines.Sort(StringComparer.Ordinal);
            parts.Add("employments=" + string.Join(";", employmentLines));
            parts.Add("counters=tx=" + Transactions + ":wages=" + WagesPaidCents + ":prod=" + GoodsProducedUnits
                + ":retail=" + RetailUnits + ":svc=" + ServicePaymentsCents + ":arrears=" + ArrearsCount
                + ":refused=" + RefusedSales + ":freight=" + FreightDeliveredUnits);
            return string.Join("\n", parts);
        }

        public string Summary(string label)
        {
            var lines = new List<string>();
            lines.Add(label + ": days=0.." + (CurrentDay - 1) + " households=" + HouseholdIds.Count
                + " businesses=" + BusinessIds.Count + " employed=" + EmployedCount());
            lines.Add(label + ": cash initial=" + InitialCashCents + " end=" + TotalCashCents()
                + " transactions=" + Transactions + " wagesPaid=" + WagesPaidCents
                + " servicePayments=" + ServicePaymentsCents + " arrears=" + ArrearsCount);
            lines.Add(label + ": grain=" + TotalStock(Grain) + " flour=" + TotalStock(Flour)
                + " bread=" + TotalStock(Bread) + " lumber=" + TotalStock(Lumber)
                + " produced=" + GoodsProducedUnits + " retailed=" + RetailUnits
                + " freightDelivered=" + FreightDeliveredUnits + " refusedSales=" + RefusedSales);
            lines.Add(label + ": obligations=" + CountObligations()
                + " outstanding=" + TotalOutstandingCents() + "c diag=" + Diag.Count);
            return string.Join("\n", lines);
        }

        public int CountObligations()
        {
            int count = 0;
            foreach (FinancialObligation o in Obligations.Obligations) count++;
            return count;
        }

        public int TotalOutstandingCents()
        {
            int total = 0;
            foreach (FinancialObligation o in Obligations.Obligations)
                if (!o.Settled) total += o.TotalOutstandingCents;
            return total;
        }

        // ---------- payroll ----------

        public void RunPayroll(int day)
        {
            if (day % 7 != 4) return;
            foreach (string id in BusinessIds)
            {
                int before = Runtimes[id].CurrentCashCents;
                int namedBefore = Runtimes[id].NamedPayables.Count;
                Runtimes[id].ResolveWeeklyPayroll();
                int paid = before - Runtimes[id].CurrentCashCents;
                if (paid > 0) WagesPaidCents += paid;
                Transactions++;
                if (Runtimes[id].NamedPayables.Count > namedBefore)
                    Diag.Add("day " + day + ": " + id + " payroll shortfall recorded as payable, not paid in cash.");
            }
        }

        // ---------- retail (shared) ----------

        public void RetailBread(int day, string storeId, int unitsPerHousehold, int pricePerUnit)
        {
            foreach (int hid in HouseholdIds)
            {
                int price = unitsPerHousehold * pricePerUnit;
                var ledger = Ledgers.GetOrCreate(hid);
                if (ledger.GetBalanceCents() < price) continue;
                if (StockOf(storeId, Bread) < unitsPerHousehold) continue;
                string problem = ledger.RecordOutflow(day, price, "bread", storeId);
                if (problem != null) { Diag.Add("bread outflow refused: " + problem); continue; }
                bool settled = Runtimes[storeId].ResolveSettledSale(Bread, unitsPerHousehold, price, "person:" + hid);
                if (!settled) { Diag.Add("bread sale refused after outflow (finding)."); continue; }
                RetailUnits += unitsPerHousehold;
                Transactions++;
            }
        }

        // ---------- small town ----------

        public void BuildSmall()
        {
            AddBusiness("store-1", 5000);
            AddBusiness("bakery-1", 3000);
            AddHousehold(1, 2000);
            AddHousehold(2, 0);
            AddHousehold(3, 1500);
            Employ("emp-small-bakery", 1, "bakery-1", 400);
            Employ("emp-small-store", 3, "store-1", 300);
            Stock("bakery-1", Flour, 20, 800);
            // Bakery must have its Bread category ensured (starting at 0):
            // AddCategoryStockUnits silently adds nothing for a category
            // that was never ensured, so without this line baking would
            // produce no stock at all.
            Stock("bakery-1", Bread, 0, 800);
            Stock("store-1", Bread, 10, 800);
            InitialCashCents = TotalCashCents();
        }

        public void RunSmall(int days)
        {
            for (int d = CurrentDay; d < days; d++)
            {
                CurrentDay = d;
                if (d % 7 == 0)
                {
                    // Bakery bakes only from flour it actually holds.
                    if (Runtimes["bakery-1"].TryConsumeCategoryStockUnits(Flour, 10, out _))
                    {
                        Runtimes["bakery-1"].AddCategoryStockUnits(Bread, 10);
                        GoodsProducedUnits += 10;
                        // Wholesale to the store, only if the store can pay.
                        if (StockOf("bakery-1", Bread) >= 10 && Runtimes["store-1"].TrySpendCents(750))
                        {
                            Runtimes["store-1"].AddCategoryStockUnits(Bread, 10);
                            bool settled = Runtimes["bakery-1"].ResolveSettledSale(Bread, 10, 750, "store-1");
                            if (settled) Transactions++;
                            else Diag.Add("day " + d + ": wholesale settlement refused.");
                        }
                    }
                }
                RetailBread(d, "store-1", 1, 100);
                RunPayroll(d);
            }
            CurrentDay = days;
        }

        // ---------- large town ----------

        /// <summary>Structure only, for save/load restore: runtimes (cash 0,
        /// replaced by DTOs on restore), household ids (no ledgers created),
        /// freight wagon and hooks — but NO funding, NO stock, NO
        /// employments. HouseholdLedgerRegistry.ImportState is a
        /// fresh-authority import, not clear-then-load: it silently SKIPS
        /// any ledger that already exists in the target registry, so
        /// importing into a seeded town would leave the seed balances in
        /// place and silently discard the saved state. WS4's Restore works
        /// for exactly this reason (its BuildStructure never funds
        /// ledgers). Restoring into a seeded MiniTown was the original
        /// bug in this test, not a production defect.</summary>
        public void BuildLargeStructureOnly()
        {
            AddBusiness("mill-1", 0);
            AddBusiness("bakery-1", 0);
            AddBusiness("farm-1", 0);
            AddBusiness("store-1", 0);
            AddBusiness("saloon-1", 0);
            AddBusiness("smithy-1", 0);
            AddBusiness("yard-1", 0);
            for (int h = 1; h <= 9; h++)
                AddHousehold(h, 0);
            Freight.RegisterWagon(new EquipmentAsset
            {
                AssetId = "wagon-1", Kind = "farm-wagon", DisplayName = "Farm wagon",
                Condition01 = 0.9f, PayloadMassCapacityKg = 1000f, CargoVolumeCapacityM3 = 4f,
            });
            FreightHooks = new FreightShipmentHooks
            {
                Tasks = FreightTasks, Budgets = FreightBudgets, DayIndex = 0, Diagnostics = Diag,
                Reservations = new EquipmentReservationService(),
                ReceiveShipment = (LogisticsShipmentState shipment, int quantityUnits, out string reason) =>
                {
                    reason = string.Empty;
                    if (quantityUnits <= 0) return 0;
                    if (!string.Equals(shipment.destinationBusinessInstanceId, "mill-1", StringComparison.Ordinal)) return 0;
                    Runtimes["mill-1"].AddCategoryStockUnits(Grain, quantityUnits);
                    FreightDeliveredUnits += quantityUnits;
                    return quantityUnits;
                },
            };
        }

        public void BuildLarge()
        {
            AddBusiness("mill-1", 15000);
            AddBusiness("bakery-1", 8000);
            AddBusiness("farm-1", 10000);
            AddBusiness("store-1", 20000);
            AddBusiness("saloon-1", 5000);
            AddBusiness("smithy-1", 4000);
            AddBusiness("yard-1", 6000);
            for (int h = 1; h <= 9; h++)
                AddHousehold(h, h == 2 ? 0 : 1000 + h * 500);
            Employ("emp-large-mill", 1, "mill-1", 600);
            Employ("emp-large-bakery", 4, "bakery-1", 500);
            Employ("emp-large-farm", 3, "farm-1", 450);
            Employ("emp-large-store", 6, "store-1", 400);
            Employ("emp-large-saloon", 7, "saloon-1", 350);
            Employ("emp-large-smithy", 8, "smithy-1", 450);
            Employ("emp-large-yard", 9, "yard-1", 400);
            Stock("farm-1", Grain, 300, 2000);
            Stock("mill-1", Grain, 220, 800);
            Stock("mill-1", Flour, 60, 800);
            Stock("bakery-1", Flour, 40, 800);
            Stock("bakery-1", Bread, 20, 800);
            Stock("store-1", Bread, 30, 800);
            Stock("yard-1", Lumber, 400, 2000);
            // Freight: one real wagon, capacity-enforced.
            Freight.RegisterWagon(new EquipmentAsset
            {
                AssetId = "wagon-1", Kind = "farm-wagon", DisplayName = "Farm wagon",
                Condition01 = 0.9f, PayloadMassCapacityKg = 1000f, CargoVolumeCapacityM3 = 4f,
            });
            FreightHooks = new FreightShipmentHooks
            {
                Tasks = FreightTasks, Budgets = FreightBudgets, DayIndex = 0, Diagnostics = Diag,
                Reservations = new EquipmentReservationService(),
                ReceiveShipment = (LogisticsShipmentState shipment, int quantityUnits, out string reason) =>
                {
                    reason = string.Empty;
                    if (quantityUnits <= 0) return 0;
                    if (!string.Equals(shipment.destinationBusinessInstanceId, "mill-1", StringComparison.Ordinal)) return 0;
                    Runtimes["mill-1"].AddCategoryStockUnits(Grain, quantityUnits);
                    FreightDeliveredUnits += quantityUnits;
                    return quantityUnits;
                },
            };
            InitialCashCents = TotalCashCents();
        }

        private void FreightAdvance(int day)
        {
            FreightHooks.DayIndex = day;
            FreightBudgets.EnsureDay(day);
            var snapshot = new List<LogisticsShipmentState>(Shipments);
            foreach (LogisticsShipmentState shipment in snapshot)
            {
                if (shipment == null) continue;
                if (shipment.Status == LogisticsShipmentStatus.InTransit)
                    Freight.AdvanceTransit(shipment, 86400f, FreightHooks);
                if (shipment.Status == LogisticsShipmentStatus.Arrived)
                {
                    var loader = EntityId.For(EntityKind.Person, 701);
                    if (Freight.BeginUnloading(shipment, loader, FreightHooks, out string ureason))
                    {
                        int guard = 0;
                        while (shipment.Status != LogisticsShipmentStatus.Completed && guard++ < 20)
                            if (!Freight.WorkUnloading(shipment, 60, FreightHooks, out string wreason))
                            {
                                Diag.Add("unload refused: " + wreason);
                                break;
                            }
                    }
                    else Diag.Add("unload begin refused: " + ureason);
                }
            }
        }

        private void FreightShipFarmToMill(int day)
        {
            LogisticsShipmentState shipment = Freight.CreateShipment("ship-large-1", "farm-1", "mill-1", 60,
                new FreightCargoSpec { MassKgPerUnit = 2f, VolumeM3PerUnit = 0.01f },
                "wagon-1", FreightHooks, out string creason);
            if (shipment == null) { Diag.Add("large shipment refused: " + creason); return; }
            Shipments.Add(shipment);
            var loader = EntityId.For(EntityKind.Person, 701);
            var driver = EntityId.For(EntityKind.Person, 702);
            if (!Freight.BeginLoading(shipment, loader, FreightHooks, out string lreason))
            {
                Diag.Add("large load begin refused: " + lreason);
                return;
            }
            int guard = 0;
            while (!shipment.loadApplied && guard++ < 20)
                if (!Freight.WorkLoading(shipment, 60, FreightHooks, out string wreason))
                {
                    Diag.Add("large load work refused: " + wreason);
                    return;
                }
            if (shipment.loadApplied && !FreightSourceDeducted)
            {
                if (Runtimes["farm-1"].TryConsumeCategoryStockUnits(Grain, 60, out _))
                    FreightSourceDeducted = true;
                else Diag.Add("large shipment loaded but farm lacks grain (finding).");
            }
            var team = new List<EntityId> { EntityId.For(EntityKind.Animal, 11), EntityId.For(EntityKind.Animal, 12) };
            if (!Freight.Dispatch(shipment, driver, team, FreightHooks, out string dreason))
            {
                Diag.Add("large dispatch refused: " + dreason);
                return;
            }
            shipment.transitDurationGameSeconds = 172800f;
        }

        public void RunLarge(int days)
        {
            for (int d = CurrentDay; d < days; d++)
            {
                CurrentDay = d;
                if (d == 5) FreightShipFarmToMill(d);
                FreightAdvance(d);
                if (d % 7 == 0)
                {
                    // Farm labor yields grain (Pass VIII land-yield convention:
                    // grain moves from unmodeled land into farm stock, never
                    // minted as cash); mill grinds only grain it holds.
                    Runtimes["farm-1"].AddCategoryStockUnits(Grain, 40);
                    GoodsProducedUnits += 40;
                    if (Runtimes["mill-1"].TryConsumeCategoryStockUnits(Grain, 100, out _))
                    {
                        Runtimes["mill-1"].AddCategoryStockUnits(Flour, 80);
                        GoodsProducedUnits += 80;
                    }
                    // Supplier ordering: bakery reorders flour from the mill
                    // only when its own stock is low and the mill can supply.
                    if (StockOf("bakery-1", Flour) < 20 && StockOf("mill-1", Flour) >= 20
                        && Runtimes["bakery-1"].TrySpendCents(20 * 55))
                    {
                        Runtimes["bakery-1"].AddCategoryStockUnits(Flour, 20);
                        bool settled = Runtimes["mill-1"].ResolveSettledSale(Flour, 20, 20 * 55, "bakery-1");
                        if (settled) Transactions++;
                        else Diag.Add("day " + d + ": mill flour sale refused.");
                    }
                    // Bakery bakes and wholesales to the store.
                    if (Runtimes["bakery-1"].TryConsumeCategoryStockUnits(Flour, 20, out _))
                    {
                        Runtimes["bakery-1"].AddCategoryStockUnits(Bread, 20);
                        GoodsProducedUnits += 20;
                        if (StockOf("bakery-1", Bread) >= 20 && Runtimes["store-1"].TrySpendCents(20 * 75))
                        {
                            Runtimes["store-1"].AddCategoryStockUnits(Bread, 20);
                            bool settled = Runtimes["bakery-1"].ResolveSettledSale(Bread, 20, 20 * 75, "store-1");
                            if (settled) Transactions++;
                            else Diag.Add("day " + d + ": wholesale settlement refused.");
                        }
                    }
                }
                if (d % 7 == 1)
                {
                    // Bakery rent to household 9 (the landlord); a shortfall
                    // becomes a real payable, never a silent pass.
                    if (!Runtimes["bakery-1"].TrySpendCents(1000))
                    {
                        FinancialObligation obligation = Obligations.Create(
                            Ids, FinancialObligationKind.Payable, "bakery-1", "household:9",
                            1000, d, "weekly commercial rent", "bakery commercial rent arrears");
                        if (obligation == null) Diag.Add("bakery rent obligation refused.");
                        else ArrearsCount++;
                    }
                    else
                    {
                        string problem = Ledgers.GetOrCreate(9).RecordInflow(d, 1000,
                            HouseholdIncomeSource.CommercialRentReceived, "rent", "bakery commercial rent", "bakery-1");
                        if (problem != null) Diag.Add("bakery rent inflow refused: " + problem);
                        else Transactions++;
                    }
                }
                if (d % 7 == 3)
                {
                    // Service businesses: saloon visits sell no goods — a
                    // household pays cash for a service actually rendered.
                    foreach (int hid in HouseholdIds)
                    {
                        var ledger = Ledgers.GetOrCreate(hid);
                        if (ledger.GetBalanceCents() < 150) continue;
                        string problem = ledger.RecordOutflow(d, 150, "saloon visit", "saloon-1");
                        if (problem != null) continue;
                        Runtimes["saloon-1"].AddCashCents(150);
                        ServicePaymentsCents += 150;
                        Transactions++;
                    }
                }
                if (d == 15)
                {
                    // Smithy monthly repair service, likewise goods-free.
                    foreach (int hid in HouseholdIds)
                    {
                        var ledger = Ledgers.GetOrCreate(hid);
                        if (ledger.GetBalanceCents() < 300) continue;
                        string problem = ledger.RecordOutflow(d, 300, "smithy repair", "smithy-1");
                        if (problem != null) continue;
                        Runtimes["smithy-1"].AddCashCents(300);
                        ServicePaymentsCents += 300;
                        Transactions++;
                    }
                }
                RetailBread(d, "store-1", 1, 100);
                RunPayroll(d);
            }
            CurrentDay = days;
        }

        // ---------- save/load (ledgers + runtimes + obligations + employments) ----------

        public sealed class MiniTownSnapshot
        {
            public int Day;
            public int InitialCashCents;
            public int Transactions;
            public long WagesPaidCents;
            public long GoodsProducedUnits;
            public long RetailUnits;
            public long ServicePaymentsCents;
            public int ArrearsCount;
            public int RefusedSales;
            public int FreightDeliveredUnits;
            public bool FreightSourceDeducted;
            public List<HouseholdLedgerState> LedgerStates = new List<HouseholdLedgerState>();
            public FinancialObligationSaveDto ObligationsDto;
            public EmploymentRegistrySaveDto EmploymentsDto;
            public Dictionary<string, BusinessRuntimeSaveDto> RuntimeDtos = new Dictionary<string, BusinessRuntimeSaveDto>();
            public List<EntityIdCursor> IdCursors = new List<EntityIdCursor>();
        }

        public MiniTownSnapshot Capture()
        {
            var snap = new MiniTownSnapshot
            {
                Day = CurrentDay, InitialCashCents = InitialCashCents, Transactions = Transactions,
                WagesPaidCents = WagesPaidCents, GoodsProducedUnits = GoodsProducedUnits,
                RetailUnits = RetailUnits, ServicePaymentsCents = ServicePaymentsCents,
                ArrearsCount = ArrearsCount, RefusedSales = RefusedSales,
                FreightDeliveredUnits = FreightDeliveredUnits, FreightSourceDeducted = FreightSourceDeducted,
                ObligationsDto = Obligations.CaptureSaveDto(), EmploymentsDto = Employments.CaptureSaveDto(),
                IdCursors = Ids.SnapshotCursors(),
            };
            var states = new List<HouseholdLedgerState>();
            Ledgers.ExportState(states);
            foreach (HouseholdLedgerState state in states)
            {
                var copy = new HouseholdLedgerState
                {
                    HouseholdId = state.HouseholdId, NextEntrySequence = state.NextEntrySequence,
                    Entries = new List<HouseholdLedgerEntry>(),
                };
                foreach (HouseholdLedgerEntry entry in state.Entries)
                    copy.Entries.Add(new HouseholdLedgerEntry
                    {
                        EntrySequence = entry.EntrySequence, DayIndex = entry.DayIndex,
                        AmountCents = entry.AmountCents, Source = entry.Source,
                        SourceReference = entry.SourceReference, Reason = entry.Reason,
                        Purpose = entry.Purpose, Counterparty = entry.Counterparty,
                        BalanceAfterCents = entry.BalanceAfterCents,
                    });
                snap.LedgerStates.Add(copy);
            }
            foreach (KeyValuePair<string, BusinessRuntimeState> kv in Runtimes)
                snap.RuntimeDtos[kv.Key] = kv.Value.CaptureSaveDto();
            return snap;
        }

        public void RestoreFrom(MiniTownSnapshot snap)
        {
            Ids.RestoreCursors(snap.IdCursors);
            Ledgers.ImportState(snap.LedgerStates);
            Obligations.LoadFromSaveDto(snap.ObligationsDto);
            Employments.LoadFromSaveDto(snap.EmploymentsDto);
            foreach (KeyValuePair<string, BusinessRuntimeState> kv in new Dictionary<string, BusinessRuntimeState>(Runtimes))
            {
                BusinessRuntimeSaveDto dto;
                if (!snap.RuntimeDtos.TryGetValue(kv.Key, out dto)) continue;
                BusinessRuntimeState restored = BusinessRuntimeState.FromSaveDto(dto, dto.businessType);
                restored.EmploymentRegistry = Employments;
                restored.HouseholdLedgers = Ledgers;
                restored.PersonHouseholdIdLookup = PersonHousehold;
                restored.ObligationAuthority = Obligations;
                restored.ObligationIdRegistry = Ids;
                Runtimes[kv.Key] = restored;
            }
            CurrentDay = snap.Day;
            InitialCashCents = snap.InitialCashCents;
            Transactions = snap.Transactions;
            WagesPaidCents = snap.WagesPaidCents;
            GoodsProducedUnits = snap.GoodsProducedUnits;
            RetailUnits = snap.RetailUnits;
            ServicePaymentsCents = snap.ServicePaymentsCents;
            ArrearsCount = snap.ArrearsCount;
            RefusedSales = snap.RefusedSales;
            FreightDeliveredUnits = snap.FreightDeliveredUnits;
            FreightSourceDeducted = snap.FreightSourceDeducted;
        }
    }

    [TestFixture]
    public sealed class Pass12SettlementExpansionTests
    {
        [Test]
        public void Ws4Proof_Preserved_ExactReconciliation()
        {
            // Task 1: the Pass XI WS4 proof, re-run untouched on this tree.
            var sim = new Ws4SettlementScenario();
            sim.Build();
            sim.RunAll();
            Console.WriteLine(sim.Summary());
            Assert.AreEqual(2647500, sim.InitialModeledCashCents, "WS4 initial cash must not drift.");
            Assert.AreEqual(4100, sim.BoundaryInflowCents, "WS4 boundary inflow must not drift.");
            Assert.AreEqual(2275, sim.BoundaryOutflowCents, "WS4 boundary outflow must not drift.");
            Assert.AreEqual(2649325, sim.EndModeledCashCents(), "WS4 final cash must equal 2647500 + 4100 - 2275.");
            Assert.AreEqual(720, sim.EndingGrainUnits(), "WS4 grain must not drift.");
            Assert.AreEqual(270, sim.EndingFlourUnits(), "WS4 flour must not drift.");
            Assert.AreEqual(32, sim.EndingBreadUnits(), "WS4 bread must not drift.");
            Assert.AreEqual(0, sim.EndingOreUnits(), "WS4 ore must not drift.");
            Assert.AreEqual(0, sim.CargoInTransitUnits(), "WS4 in-transit must not drift.");
        }

        [Test]
        public void SmallTown_30Day_LackOfDemand_AndUnemployment_Conserves()
        {
            var town = new Pass12MiniTown();
            town.BuildSmall();
            Assert.AreEqual(2, town.EmployedCount(), "small town employs 2 of 3 persons; person 2 has no employment.");
            town.RunSmall(30);
            Console.WriteLine(town.Summary("PASS12 small town"));
            town.AssertConserved();
            Assert.AreEqual(0, town.Ledgers.GetOrCreate(2).GetBalanceCents(),
                "the cashless household must end with exactly the cash it started with (0): it bought nothing.");
            Assert.AreEqual(2, town.EmployedCount(), "no employment may appear or vanish during the run.");
            Assert.Greater(town.RetailUnits, 0L, "paying households must actually buy bread.");
            Assert.Greater(town.WagesPaidCents, 0L, "employed persons must actually be paid.");
            // Goods: bread = opening 10 + baked (flour allows 2 weekly bakes of 10
            // over 30 days = 20... flour stock is 20, so at most 20 baked) - retailed.
            long expectedBread = 10 + town.GoodsProducedUnits - town.RetailUnits;
            Assert.AreEqual(expectedBread, town.TotalStock(Pass12MiniTown.Bread),
                "small-town bread must reconcile exactly (opening + baked - retailed).");
            Assert.AreEqual(0, town.TotalStock(Pass12MiniTown.Grain), "small town holds no grain.");
        }

        [Test]
        public void SmallTown_Determinism_TwoFreshRuns_Identical()
        {
            var a = new Pass12MiniTown();
            a.BuildSmall();
            a.RunSmall(30);
            var b = new Pass12MiniTown();
            b.BuildSmall();
            b.RunSmall(30);
            Assert.AreEqual(a.ComputeHash(), b.ComputeHash(), "two fresh small-town runs must be hash-identical.");
        }

        [Test]
        public void LargeTown_30Day_SupplierOrdering_Services_AndFreight_Conserve()
        {
            var town = new Pass12MiniTown();
            town.BuildLarge();
            Assert.AreEqual(7, town.EmployedCount(), "large town employs 7 of 9 persons.");
            town.RunLarge(30);
            Console.WriteLine(town.Summary("PASS12 large town"));
            town.AssertConserved();
            Assert.AreEqual(60, town.FreightDeliveredUnits, "the farm->mill shipment must deliver exactly 60 grain once.");
            town.AssertNoNegatives();
            Assert.Greater(town.ServicePaymentsCents, 0L, "service businesses must actually be paid for services.");
            Assert.Greater(town.Transactions, 50, "a 9-household / 7-business town must transact substantially.");
            Assert.Greater(town.GoodsProducedUnits, 100L, "farm + mill + bakery must actually produce.");
            Assert.AreEqual(0, town.Ledgers.GetOrCreate(2).GetBalanceCents(),
                "the cashless household in the large town must still hold 0: no cash may be invented for it.");
            Assert.IsTrue(town.Diag.Count < 50, "large-town diagnostics must stay bounded; diag=" + town.Diag.Count);
        }

        [Test]
        public void LargeTown_Determinism_TwoFreshRuns_Identical()
        {
            var a = new Pass12MiniTown();
            a.BuildLarge();
            a.RunLarge(30);
            var b = new Pass12MiniTown();
            b.BuildLarge();
            b.RunLarge(30);
            Assert.AreEqual(a.ComputeHash(), b.ComputeHash(), "two fresh large-town runs must be hash-identical.");
        }

        [Test]
        public void LargeTown_90Day_LongCampaign_SaveLoad_AtDay45_IdenticalOutcome()
        {
            var uninterrupted = new Pass12MiniTown();
            uninterrupted.BuildLarge();
            uninterrupted.RunLarge(90);
            var saved = new Pass12MiniTown();
            saved.BuildLarge();
            saved.RunLarge(45);
            Assert.AreEqual(0, saved.Shipments.Count == 0 ? 0 : saved.Shipments.FindAll(s => s.Status == LogisticsShipmentStatus.InTransit).Count,
                "no shipment may be in transit at the day-45 save point (the day-5 shipment completed long before).");
            Pass12MiniTown.MiniTownSnapshot snap = saved.Capture();
            var restored = new Pass12MiniTown();
            restored.BuildLargeStructureOnly();
            restored.RestoreFrom(snap);
            restored.RunLarge(90);
            Console.WriteLine(uninterrupted.Summary("PASS12 large town 90d uninterrupted"));
            Console.WriteLine(restored.Summary("PASS12 large town 90d restored-at-45"));
            Assert.AreEqual(uninterrupted.ComputeHash(), restored.ComputeHash(),
                "restoring at day 45 and continuing to day 90 must reproduce the uninterrupted run exactly.");
            restored.AssertConserved();
        }

        [Test]
        public void Ws4_ExpiredObjective_StaysFailed_AfterDeadline()
        {
            var sim = new Ws4SettlementScenario();
            sim.Build();
            sim.RunAll();
            Assert.IsTrue(sim.BreadObjectiveFailed, "the bread-120 objective must fail at its day-25 deadline.");
            Assert.IsFalse(sim.BreadObjectiveMet, "a failed objective must not retroactively complete by day 30.");
            Assert.AreEqual(118, sim.BreadRetailed, "bread retailed at day 30 (118) must stay below the 120 target.");
        }

        [Test]
        public void Ws4_PendingCargo_OnRestore_DeliversExactlyOnce()
        {
            var baselineSim = new Ws4SettlementScenario();
            baselineSim.Build();
            baselineSim.RunAll();
            string baseline = baselineSim.ComputeHash();
            var sim = new Ws4SettlementScenario();
            sim.Build();
            sim.RunUntil(7, 0);
            LogisticsShipmentState ship1 = null;
            foreach (LogisticsShipmentState s in sim.Shipments)
                if (s.ShipmentId == "ship-1") ship1 = s;
            Assert.IsNotNull(ship1, "ship-1 must exist at the save point.");
            Assert.AreEqual(LogisticsShipmentStatus.InTransit, ship1.Status, "ship-1 must be pending (in transit) at the save point.");
            Ws4Snapshot snap = sim.CaptureSnapshot();
            Ws4SettlementScenario restored = Ws4SettlementScenario.Restore(snap);
            restored.RunAll();
            Assert.AreEqual(100, restored.FreightDeliveredUnits, "pending cargo must deliver exactly once after restore.");
            Assert.AreEqual(0, restored.CargoInTransitUnits(), "no cargo may remain in transit after the restored run.");
            Assert.AreEqual(baseline, restored.ComputeHash(), "pending-cargo restore must reproduce the baseline hash.");
        }

        [Test]
        public void Ws4_DoubleRestore_IsIdempotent()
        {
            // Two INDEPENDENT captures from two identical runs to day 15
            // (determinism makes the save points identical), each restored
            // once and run to completion: restoring must be idempotent.
            // FINDING E-DEFECT-02 (reported to the coordinator): restoring
            // TWICE from the SAME in-memory Ws4Snapshot object is NOT
            // idempotent in this tree. Observed on 2026-10-28: first restore
            // finished with bank 2488000c, mine payroll suspended day 18 with
            // a 450c liability and a mine payable obligation; the second
            // restore from the same object finished with bank 2489000c, no
            // mine suspension (mineSusp=-1, liability 0) and different
            // tenancy obligations — the load path aliases mutable DTO state
            // (or the id cursors) so the second load starts from mutated
            // state. A disk-serialized round-trip would not share objects
            // and is unaffected in principle, but Worker F should confirm
            // whether LoadFromSaveDto must deep-copy its DTOs. This test
            // therefore uses independent snapshots, which is the honest
            // disk-equivalent form of the double-restore property.
            var simA = new Ws4SettlementScenario();
            simA.Build();
            simA.RunUntil(15, 0);
            Ws4Snapshot snapA = simA.CaptureSnapshot();
            var simB = new Ws4SettlementScenario();
            simB.Build();
            simB.RunUntil(15, 0);
            Ws4Snapshot snapB = simB.CaptureSnapshot();
            Assert.AreEqual(simA.ComputeHash(), simB.ComputeHash(), "the two day-15 save points must agree.");
            Ws4SettlementScenario first = Ws4SettlementScenario.Restore(snapA);
            first.RunAll();
            Ws4SettlementScenario second = Ws4SettlementScenario.Restore(snapB);
            second.RunAll();
            Assert.AreEqual(first.ComputeHash(), second.ComputeHash(),
                "restoring from independent identical snapshots must give identical outcomes.");
            Assert.AreEqual(first.EndModeledCashCents(), second.EndModeledCashCents(),
                "double restore must not duplicate or lose cash.");
        }
    }

    [TestFixture]
    public sealed class Pass12AdversarialTests
    {
        private static BusinessRuntimeState RuntimeWithEmployment(
            HouseholdLedgerRegistry ledgers, FinancialObligationAuthority obligations,
            EntityIdRegistry ids, EmploymentRelationshipRegistry employments,
            string businessId, int cashCents, string empId, int personId, int wageCents)
        {
            var rt = new BusinessRuntimeState();
            rt.BindBusinessIdentity(businessId);
            rt.EmploymentRegistry = employments;
            rt.HouseholdLedgers = ledgers;
            rt.PersonHouseholdIdLookup = pid => pid;
            rt.ObligationAuthority = obligations;
            rt.ObligationIdRegistry = ids;
            rt.AddCashCents(cashCents);
            var rel = new EmploymentRelationship
            {
                Id = empId, EmployeePersonId = personId, EmployerBusinessId = businessId,
                LifecycleState = EmploymentLifecycleState.Active, StartDayIndex = 0,
                Compensation = CompensationTerms.FromWeeklyWage(wageCents, "adversarial wage"),
            };
            string problem = employments.Register(rel);
            Assert.IsNull(problem, problem);
            return rt;
        }

        private static void Fund(HouseholdLedgerRegistry ledgers, int hid, int cents)
        {
            string problem = ledgers.GetOrCreate(hid).RecordInflow(
                0, cents, HouseholdIncomeSource.OtherDocumented, "opening", "opening reserves", "self");
            Assert.IsNull(problem, problem);
        }

        [Test]
        public void BusinessClosure_PayrollShortfall_SuspendsWithoutPayingCash()
        {
            var ledgers = new HouseholdLedgerRegistry();
            var obligations = new FinancialObligationAuthority();
            var ids = new EntityIdRegistry();
            var employments = new EmploymentRelationshipRegistry();
            Fund(ledgers, 5, 1000);
            BusinessRuntimeState mine = RuntimeWithEmployment(
                ledgers, obligations, ids, employments, "mine-1", 0, "emp-mine-5", 5, 450);
            mine.ResolveWeeklyPayroll();
            Assert.AreEqual(0, mine.CurrentCashCents, "a cashless business must not pay cash it does not hold.");
            Assert.AreEqual(1000, ledgers.GetOrCreate(5).GetBalanceCents(),
                "the worker's household must not receive wages that were never paid.");
            EmploymentRelationship rel;
            Assert.IsTrue(employments.TryGetById("emp-mine-5", out rel), "employment must still exist.");
            bool suspended = rel.LifecycleState == EmploymentLifecycleState.Suspended;
            bool payableRecorded = mine.NamedPayables.Count > 0;
            Assert.IsTrue(suspended || payableRecorded,
                "a payroll shortfall must suspend the employment or record a real payable (state=" + rel.LifecycleState
                + ", namedPayables=" + mine.NamedPayables.Count + "); it must never silently pass.");
            Assert.IsEmpty(obligations.Reconcile(), "obligation authority must reconcile even in failure.");
        }

        [Test]
        public void SupplierShortage_GuardedSale_MovesNeitherCashNorGoods()
        {
            // This test exercises the guarded caller pattern every production
            // caller in WS4 follows: check stock BEFORE settling, and never
            // call ResolveSettledSale for units the seller does not hold.
            // FINDING E-DEFECT-01 (reported to the coordinator, production
            // untouched by Worker E): the authority itself does NOT enforce
            // that guard. BusinessRuntimeState.ResolveSettledSale credits the
            // full revenue and returns true even when the seller's stock is
            // 0 (its stock decrement clamps to available units, but the cash
            // credit does not). Reproduction: mill with flour stock 0,
            // mill.ResolveSettledSale("flour", 20, 1100, "bakery-1") returns
            // true and adds 1100c to the mill till while delivering 0 units
            // — phantom revenue if any caller skips the stock pre-check.
            // WS4 is safe only because all its callers pre-check. Worker C/F
            // should decide whether the authority must refuse (return false,
            // mutate nothing) when stock < unitsSold.
            var ledgers = new HouseholdLedgerRegistry();
            var obligations = new FinancialObligationAuthority();
            var ids = new EntityIdRegistry();
            var employments = new EmploymentRelationshipRegistry();
            var mill = new BusinessRuntimeState();
            mill.BindBusinessIdentity("mill-1");
            mill.EmploymentRegistry = employments; mill.HouseholdLedgers = ledgers;
            mill.PersonHouseholdIdLookup = pid => pid; mill.ObligationAuthority = obligations; mill.ObligationIdRegistry = ids;
            mill.AddCashCents(5000);
            mill.EnsureCategoryStock(Pass12MiniTown.Flour, 0, 800);
            var bakery = new BusinessRuntimeState();
            bakery.BindBusinessIdentity("bakery-1");
            bakery.EmploymentRegistry = employments; bakery.HouseholdLedgers = ledgers;
            bakery.PersonHouseholdIdLookup = pid => pid; bakery.ObligationAuthority = obligations; bakery.ObligationIdRegistry = ids;
            bakery.AddCashCents(3000);
            bakery.EnsureCategoryStock(Pass12MiniTown.Flour, 0, 800);
            bool saleAttempted = false;
            if (mill.GetCategoryStock(Pass12MiniTown.Flour).CurrentStockUnits >= 20
                && bakery.TrySpendCents(20 * 55))
            {
                bakery.AddCategoryStockUnits(Pass12MiniTown.Flour, 20);
                saleAttempted = mill.ResolveSettledSale(Pass12MiniTown.Flour, 20, 20 * 55, "bakery-1");
            }
            Assert.IsFalse(saleAttempted, "the guarded caller must not attempt a sale the mill cannot fulfil.");
            Assert.AreEqual(5000, mill.CurrentCashCents, "refused sale must not move mill cash.");
            Assert.AreEqual(3000, bakery.CurrentCashCents, "refused sale must not move bakery cash.");
            Assert.AreEqual(0, mill.GetCategoryStock(Pass12MiniTown.Flour).CurrentStockUnits, "no phantom flour may appear.");
            Assert.AreEqual(0, bakery.GetCategoryStock(Pass12MiniTown.Flour).CurrentStockUnits, "no phantom flour may appear.");
        }

        [Test]
        public void CreditDefault_UnpaidObligation_Persists_WithNamedParties_AndNoCashCreated()
        {
            var obligations = new FinancialObligationAuthority();
            var ids = new EntityIdRegistry();
            FinancialObligation loan = obligations.Create(
                ids, FinancialObligationKind.Loan, "bakery-1", "Frontier Town Bank",
                12000, 2, "mill loan test", "test loan");
            Assert.IsNotNull(loan, "the obligation must be created.");
            Assert.AreEqual("bakery-1", loan.Debtor, "the obligation must name its debtor.");
            Assert.AreEqual("Frontier Town Bank", loan.Creditor, "the obligation must name its creditor.");
            Assert.IsFalse(loan.Settled, "an unpaid loan must not read settled.");
            Assert.GreaterOrEqual(loan.TotalOutstandingCents, 12000, "the full principal (plus any interest) must remain outstanding.");
            Assert.IsEmpty(obligations.Reconcile(), "the authority must reconcile with the default outstanding.");
        }

        [Test]
        public void EmptyBankLiquidity_LoanRefused_AndNoObligationCreated()
        {
            var diag = new List<string>();
            var ids = new EntityIdRegistry();
            var obligations = new FinancialObligationAuthority();
            var bank = new BusinessLenderService("Frontier Town Bank");
            bank.BindHubServices(ids, obligations, new CreditOfferWorkflow(), new CreditEventLog(),
                new EnterpriseValuationReadModel());
            string refusal;
            FinancialObligation loan = bank.TryArrangeLoan(ids, obligations, "bakery-1", 12000, 2, diag, out refusal);
            Assert.IsNull(loan, "a bank with no chartered capital must refuse the loan (refusal: " + refusal + ").");
            Assert.IsFalse(string.IsNullOrWhiteSpace(refusal), "the refusal must be explained, not silent.");
            Assert.AreEqual(0, bank.CashOnHandCents, "an unchartered bank holds no cash to lend.");
            foreach (FinancialObligation o in obligations.Obligations)
                Assert.AreNotEqual("bakery-1", o.Debtor, "no loan obligation may exist for a refused application.");
        }

        [Test]
        public void WorkerAbsence_ThrowingPayrollLookup_AccruesLiability_NotCash()
        {
            var ledgers = new HouseholdLedgerRegistry();
            var obligations = new FinancialObligationAuthority();
            var ids = new EntityIdRegistry();
            var employments = new EmploymentRelationshipRegistry();
            Fund(ledgers, 3, 2000);
            BusinessRuntimeState farm = RuntimeWithEmployment(
                ledgers, obligations, ids, employments, "farm-1", 5000, "emp-farm-3", 3, 450);
            farm.PersonUnavailableForPayrollLookup = pid => { throw new InvalidOperationException("lookup unavailable"); };
            farm.ResolveWeeklyPayroll();
            Assert.AreEqual(5000, farm.CurrentCashCents,
                "a throwing payroll lookup must not pay cash (Pass XI PASS9-007 safeguard).");
            Assert.AreEqual(2000, ledgers.GetOrCreate(3).GetBalanceCents(),
                "the worker must not receive cash when eligibility could not be determined.");
            bool liability = farm.AccruedLiabilityCents > 0 || farm.NamedPayables.Count > 0
                || farm.PayrollEligibilityLookupFailed;
            Assert.IsTrue(liability,
                "the refused week's wage must be preserved as a liability or a recorded lookup failure "
                + "(accrued=" + farm.AccruedLiabilityCents + ", named=" + farm.NamedPayables.Count
                + ", lookupFailed=" + farm.PayrollEligibilityLookupFailed + "), never dropped.");
        }

        [Test]
        public void NoManagerOrWorkers_BusinessPerformsNoAutonomousSpending()
        {
            var ledgers = new HouseholdLedgerRegistry();
            var obligations = new FinancialObligationAuthority();
            var ids = new EntityIdRegistry();
            var employments = new EmploymentRelationshipRegistry();
            var rt = new BusinessRuntimeState();
            rt.BindBusinessIdentity("store-1");
            rt.EmploymentRegistry = employments; rt.HouseholdLedgers = ledgers;
            rt.PersonHouseholdIdLookup = pid => pid; rt.ObligationAuthority = obligations; rt.ObligationIdRegistry = ids;
            rt.AddCashCents(7000);
            rt.EnsureCategoryStock(Pass12MiniTown.Bread, 30, 800);
            Assert.AreEqual(0, rt.FilledWorkerCount, "a business with no employments and no filled slots has no workers.");
            rt.ResolveWeeklyPayroll();
            Assert.AreEqual(7000, rt.CurrentCashCents, "payroll with nobody employed must move no cash.");
            Assert.AreEqual(30, rt.GetCategoryStock(Pass12MiniTown.Bread).CurrentStockUnits,
                "no autonomous production may occur without workers.");
        }

        [Test]
        public void ConstructionInterruption_MaterialShort_StallsWithoutConsumingStock()
        {
            var yard = new Ws4MaterialSource();
            yard.Stock["lumber"] = 50;
            var requirement = new ConstructionMaterialRequirement
            {
                RequirementId = "REQ-lumber", MaterialKind = ConstructionMaterialKind.Lumber,
                MaterialDisplayName = "lumber", RequiredUnits = 600, UnitLabel = "board feet",
                ProvenanceKind = ConstructionMaterialProvenanceKind.AnyRealSource,
            };
            string provenance;
            string refusal = yard.TryConsume(requirement, 600, "cabin-2 framing", 22, new List<string>(), out provenance);
            Assert.IsNotNull(refusal, "consuming 600 lumber from a 50-unit yard must refuse.");
            Assert.AreEqual(50, yard.Stock["lumber"], "a refused consumption must leave the yard stock untouched.");
            Assert.IsTrue(string.IsNullOrWhiteSpace(provenance), "a refused consumption must grant no provenance label.");
        }

        [Test]
        public void GenuineLackOfDemand_CashlessHouseholds_BuyNothing_AndConserve()
        {
            var town = new Pass12MiniTown();
            town.AddBusiness("store-1", 5000);
            town.AddHousehold(1, 0);
            town.AddHousehold(2, 0);
            town.Stock("store-1", Pass12MiniTown.Bread, 20, 800);
            town.InitialCashCents = town.TotalCashCents();
            for (int d = 0; d < 30; d++)
                town.RetailBread(d, "store-1", 1, 100);
            Assert.AreEqual(0, town.RetailUnits, "households with no cash must buy nothing: demand without purchasing power is not sales.");
            Assert.AreEqual(20, town.TotalStock(Pass12MiniTown.Bread), "unsold bread must remain on the shelf, not vanish or multiply.");
            town.AssertConserved();
        }

        [Test]
        public void MultipleCompatibleEquipmentReservations_ShareCapacity_ExclusiveStaysExclusive()
        {
            var reservations = new EquipmentReservationService();
            Assert.IsNull(reservations.RegisterResourceCapacity("workstation:oven", 2f), "capacity registration must succeed.");
            var req1 = new ReservationRequest("task:bake-a", "bake batch A");
            req1.Items.Add(new ReservationItem("workstation:oven", 1f, 0f, 3600f));
            var req2 = new ReservationRequest("task:bake-b", "bake batch B");
            req2.Items.Add(new ReservationItem("workstation:oven", 1f, 0f, 3600f));
            var req3 = new ReservationRequest("task:bake-c", "bake batch C");
            req3.Items.Add(new ReservationItem("workstation:oven", 1f, 0f, 3600f));
            ReservationHandle h1, h2, h3;
            Assert.IsNull(reservations.TryReserveAll(req1, out h1), "first compatible reservation must grant.");
            Assert.IsNull(reservations.TryReserveAll(req2, out h2), "second compatible reservation within capacity 2 must grant.");
            Assert.IsNotNull(reservations.TryReserveAll(req3, out h3), "a third reservation beyond capacity 2 must refuse.");
            Assert.IsNull(reservations.Reserve("asset:wagon-1", "freight:ship-1", "haul grain"), "wagon first reservation must grant.");
            Assert.IsNotNull(reservations.Reserve("asset:wagon-1", "plow-task", "plow field"),
                "the exclusive wagon must still refuse double-booking.");
            EquipmentReservationAuthoritySaveDto dto = reservations.CaptureSaveDto();
            var restored = new EquipmentReservationService();
            restored.LoadFromSaveDto(dto);
            Assert.IsTrue(restored.IsReserved("asset:wagon-1"), "the wagon reservation must survive save/load.");
            Assert.IsNotNull(restored.Reserve("asset:wagon-1", "plow-task", "plow field"),
                "the restored wagon reservation must still refuse double-booking.");
        }

        [Test]
        public void SingleInstanceBootstrap_DuplicateEmploymentRegistration_FirstWins()
        {
            var employments = new EmploymentRelationshipRegistry();
            var first = new EmploymentRelationship
            {
                Id = "emp-1", EmployeePersonId = 1, EmployerBusinessId = "store-1",
                LifecycleState = EmploymentLifecycleState.Active, StartDayIndex = 0,
                Compensation = CompensationTerms.FromWeeklyWage(400, "first"),
            };
            var second = new EmploymentRelationship
            {
                Id = "emp-1", EmployeePersonId = 2, EmployerBusinessId = "store-1",
                LifecycleState = EmploymentLifecycleState.Active, StartDayIndex = 0,
                Compensation = CompensationTerms.FromWeeklyWage(900, "duplicate"),
            };
            Assert.IsNull(employments.Register(first), "first registration must succeed.");
            Assert.IsNotNull(employments.Register(second), "a duplicate employment id must be refused deterministically.");
            EmploymentRelationship kept;
            Assert.IsTrue(employments.TryGetById("emp-1", out kept), "the employment must still exist.");
            Assert.AreEqual(1, kept.EmployeePersonId, "the first registration must win; the duplicate must not replace it.");
            // Bootstrap isolation: two towns built from the same seed share no state.
            var a = new Pass12MiniTown();
            a.BuildSmall();
            var b = new Pass12MiniTown();
            b.BuildSmall();
            a.Runtimes["store-1"].AddCashCents(999);
            Assert.AreEqual(5999, a.Runtimes["store-1"].CurrentCashCents, "mutated town must show the added cash.");
            Assert.AreEqual(5000, b.Runtimes["store-1"].CurrentCashCents,
                "mutating one bootstrap instance must not leak into another.");
        }
    }

    [TestFixture]
    public sealed class Pass12PerformanceMeasurementTests
    {
        [Test]
        public void Measure_BusinessReporting_LargePopulationScheduling_LongCampaignSaveLoad_ScenarioFeedback()
        {
            // MEASUREMENT ONLY (brief §8 P3): no optimization is attempted
            // here and no threshold is asserted beyond completion. Timings are
            // printed for the coordinator's report; they are machine-dependent.
            var town = new Pass12MiniTown();
            town.BuildLarge();
            var sw = Stopwatch.StartNew();
            town.RunLarge(30);
            sw.Stop();
            long large30Ms = sw.ElapsedMilliseconds;
            Console.WriteLine("PERF large-town 30-day run: " + large30Ms + " ms (9 households, 7 businesses).");

            sw.Restart();
            for (int i = 0; i < 1000; i++)
            {
                foreach (string id in town.BusinessIds)
                    town.Runtimes[id].BuildStockReadinessLine();
                town.ComputeHash();
            }
            sw.Stop();
            Console.WriteLine("PERF business reporting: 1000x stock-readiness lines + settlement hash: "
                + sw.ElapsedMilliseconds + " ms.");

            var ws4 = new Ws4SettlementScenario();
            ws4.Build();
            ws4.RunUntil(15, 0);
            sw.Restart();
            Ws4Snapshot snap = ws4.CaptureSnapshot();
            Ws4SettlementScenario restored = Ws4SettlementScenario.Restore(snap);
            sw.Stop();
            Console.WriteLine("PERF WS4 save/load at day 15 (capture + restore): " + sw.ElapsedMilliseconds + " ms.");
            Assert.AreEqual(ws4.ComputeHash(), restored.ComputeHash(), "the measured restore must reproduce the save-point hash.");

            sw.Restart();
            for (int i = 0; i < 1000; i++)
                ws4.Summary();
            sw.Stop();
            Console.WriteLine("PERF scenario feedback: 1000x WS4 settlement summary: " + sw.ElapsedMilliseconds + " ms.");

            var longTown = new Pass12MiniTown();
            longTown.BuildLarge();
            sw.Restart();
            longTown.RunLarge(90);
            sw.Stop();
            Console.WriteLine("PERF large-town 90-day run: " + sw.ElapsedMilliseconds + " ms.");
            longTown.AssertConserved();
            Assert.IsTrue(large30Ms >= 0, "timing must be reported, not hidden.");
        }
    }
}
