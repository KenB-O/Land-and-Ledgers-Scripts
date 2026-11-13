using EntityId = LandLedgers.Primitives.EntityId;

using System;
using System.Collections.Generic;
using LandLedgers.Economy;
using LandLedgers.Economy.Businesses.Bakery;
using LandLedgers.Economy.Businesses.GeneralStore;
using LandLedgers.Economy.Businesses.Logging;
using LandLedgers.Economy.Businesses.Sawmill;
using LandLedgers.Economy.Blacksmith;
using LandLedgers.Economy.BreadDrivers;
using LandLedgers.Economy.Core;
using LandLedgers.Economy.Equipment;
using LandLedgers.Economy.Equipment.Workstations;
using LandLedgers.Economy.Farming.Timber;
using LandLedgers.Persistence;
using LandLedgers.Population;
using LandLedgers.Primitives;
using LandLedgers.Tasks;
using LandLedgers.Time;
using LandLedgers.World.Journeys;
using UnityEngine;

namespace LandLedgers.Orchestration.Player
{
    /// <summary>
    /// Pass XL phase 8: the three-business slice economy. Pass 6 proved
    /// General Store flour to household meals; pass 7 proved work and
    /// pay. This bridge proves the other two businesses actually
    /// producing and selling, and the other two households actually
    /// eating, with cash reconciling at every step.
    ///
    /// WHAT IS REAL HERE
    ///   - Bakery production runs through the real BakeryShopRuntime
    ///     (PlanBatch, WorkDay, SellBread), NOT through the staged
    ///     chain. See StagedTransferSeam below for why the staged
    ///     chain is not converted.
    ///   - Bakery flour comes from the real General Store lot ledger
    ///     through OffMapNamedSkuProcurementService.ConsumeLots, and
    ///     the bakery PAYS the store for it (an inter-business sale
    ///     at a caller-supplied price). Flour is never gifted to the
    ///     bakery, and a consumed unit can never also be sold to a
    ///     household: ConsumeLots removes it from sellable stock.
    ///   - Fuel comes from the real logging chain: authored stand,
    ///     explicit timber-rights grant, felling, journey-planned
    ///     haul, sawmill sawing, byproduct custody transfer to the
    ///     named FuelDealer intake, then a paid sale from that intake.
    ///     The sawmill is an upstream supplier outside the six-person
    ///     fixture; its lumber stays at the mill and is not sold here.
    ///   - Bread and fuel sales to households settle through the real
    ///     HouseholdLedger (outflow) and BusinessRuntimeState cash
    ///     (credit), and land in HouseholdInventory through AddLot.
    ///     A failed inventory write restores shelf/intake units and
    ///     reverses cash, mirroring the phase 2 atomic pattern.
    ///   - Founders are operators, never wage employees (TECH 4.1):
    ///     Glen and Jacob earn through sale margin and explicit
    ///     day-to-day owner draws (SliceWorkAndPay.DrawDaily), never
    ///     through payroll. The composed run refuses a founder wage
    ///     registration as a control.
    ///
    /// STAGED TRANSFER SEAM (NOT DONE, NOT FAKED)
    /// BakeryStagedProductionChain is a separate nine-step production
    /// method (BakeryStagedProductionChain.cs): its shelf lots carry a
    /// string LotId (bread-&lt;batch&gt;) and its batches run through a
    /// BakeryChainContext (TaskAuthority, WorkTimeBudgetStore,
    /// IDueEventScheduler, IUnifiedReservationAuthority, game clock).
    /// BakeryShopRuntime has NO public Receive/Add API for finished
    /// bread, and the staged chain has NO delivered-mark set, so a
    /// transfer built by hand-copying staged lots into shop lots
    /// would create a second custody record for the same bread and
    /// could transfer one staged lot twice. A safe transfer needs,
    /// in Codex/production work: a ReceiveTransferredBreadLot API on
    /// BakeryShopRuntime with a transferred-staged-lot-id set in its
    /// save DTO, a staged-lot consumption mark on the chain side, a
    /// decision on baked-day versus transfer-day aging (the shop lot
    /// ages from BakedDayIndex; a transfer day later would sell
    /// day-old bread as fresh unless the transfer carries the baked
    /// day through), and a Main Scene owner/driver for the chain.
    /// Until then the slice bakery IS the shop runtime: it produces
    /// real bread with real flour provenance, real fuel, and real
    /// prices, and the staged chain stays unused by the slice.
    ///
    /// NOTIONS SEAM (LABELLED RUN INPUT, NOT SLICE EARNINGS)
    /// A bread batch needs 1 notion unit (yeast/soda/salt/lard) as
    /// well as 12 lb flour, and the slice has no notions supply:
    /// the General Store stocks flour only (single-SKU guard intact),
    /// and no miller or notions-import path exists for the bakery.
    /// The composed run therefore supplies notions as an explicit,
    /// labelled run input through BakeryFlourSupply.ReceiveImportArrival
    /// with a named import order id. Bread baking is proved GIVEN
    /// notions on hand; a notions procurement path is a remaining
    /// gap for pass 9 / Codex, not something this pass pretends the
    /// slice already earns.
    ///
    /// OTHER SEAMS (same as phases 6-7, unchanged)
    /// Daily/weekly tick driver, residence assignment, postal office
    /// registration, production supplier registration, Main Scene
    /// wiring, and fixture-to-production id confirmation are all
    /// Codex-only. Equipment gates are NOT proved here: the fuel
    /// chain runs with gate null (no axe/wagon/saw-line possession
    /// proved); a production driver must supply real gates.
    /// All prices, wages, draws, reserves, freight, and supply numbers
    /// in the composed run are caller-supplied test parameters, never
    /// canon rates.
    /// </summary>
    public sealed class ThreeBusinessFuelSaleResult
    {
        public bool Success;
        public int Units;
        public int IntakeUnits;
        public int TotalCents;
        public string BakeryLotId = string.Empty;
        public List<string> ConsumedIntakeLotIds = new List<string>();
        public string Diagnostic = string.Empty;
    }

    /// <summary>
    /// Paid, live-intake supplier used by Bakery production. This is a
    /// source adapter over the existing inter-business sale authority;
    /// it does not hold stock or cash of its own.
    /// </summary>
    public sealed class Series2PaidDealerBakeryFuelSource : ISeries2BakeryFuelSource
    {
        private readonly FuelYardByproductIntake intake;
        private readonly BusinessRuntimeState dealerRuntime;
        private readonly BakeryShopRuntime bakery;
        private readonly BusinessRuntimeState bakeryRuntime;
        private readonly EntityIdRegistry ids;
        private readonly string dealerBusinessId;
        private readonly int intakeUnitsPerBakeryUnit;
        private readonly int priceCentsPerBakeryUnit;

        public Series2PaidDealerBakeryFuelSource(
            FuelYardByproductIntake intake, string dealerBusinessId,
            BusinessRuntimeState dealerRuntime, BakeryShopRuntime bakery,
            BusinessRuntimeState bakeryRuntime, EntityIdRegistry ids,
            int intakeUnitsPerBakeryUnit, int priceCentsPerBakeryUnit)
        {
            this.intake = intake ?? throw new ArgumentNullException(nameof(intake));
            this.dealerBusinessId = string.IsNullOrWhiteSpace(dealerBusinessId)
                ? throw new ArgumentException("A registered dealer id is required.", nameof(dealerBusinessId))
                : dealerBusinessId;
            this.dealerRuntime = dealerRuntime ?? throw new ArgumentNullException(nameof(dealerRuntime));
            this.bakery = bakery ?? throw new ArgumentNullException(nameof(bakery));
            this.bakeryRuntime = bakeryRuntime ?? throw new ArgumentNullException(nameof(bakeryRuntime));
            this.ids = ids ?? throw new ArgumentNullException(nameof(ids));
            if (intakeUnitsPerBakeryUnit <= 0)
                throw new ArgumentOutOfRangeException(nameof(intakeUnitsPerBakeryUnit), "An authored positive conversion is required.");
            if (priceCentsPerBakeryUnit <= 0)
                throw new ArgumentOutOfRangeException(nameof(priceCentsPerBakeryUnit), "A configured positive price is required.");
            this.intakeUnitsPerBakeryUnit = intakeUnitsPerBakeryUnit;
            this.priceCentsPerBakeryUnit = priceCentsPerBakeryUnit;
        }

        public string DealerBusinessId => dealerBusinessId;
        public int IntakeUnitsPerBakeryUnit => intakeUnitsPerBakeryUnit;
        public int PriceCentsPerBakeryUnit => priceCentsPerBakeryUnit;

        public int FuelUnitsOnHand()
        {
            return intake.TotalFuelWoodUnitsFor(dealerBusinessId) / intakeUnitsPerBakeryUnit;
        }

        public string SupplyTo(BakeryShopRuntime target, int unitsNeeded, int dayIndex, List<string> diag)
        {
            diag ??= new List<string>();
            if (target == null || target != bakery || target.FuelStock == null)
                return "Series2PaidDealerBakeryFuelSource: target is not the registered Bakery; no sale attempted.";
            if (unitsNeeded <= 0)
                return "Series2PaidDealerBakeryFuelSource: positive Bakery fuel need required; no sale attempted.";

            int available = FuelUnitsOnHand();
            int onHand = target.FuelStock.UnitsOnHand(BakeryBreadCatalog.FuelItemId);
            int stillNeeded = Math.Max(0, unitsNeeded - onHand);
            if (stillNeeded == 0)
            {
                diag.Add($"Series2PaidDealerBakeryFuelSource: Bakery store already holds {onHand}u; no order or payment needed.");
                return null;
            }
            if (available < stillNeeded)
                return $"Series2PaidDealerBakeryFuelSource: dealer '{dealerBusinessId}' can cover {available}u of {stillNeeded}u needed; shortfall left blocked, no payment or stock moved.";

            ThreeBusinessFuelSaleResult sale = ThreeBusinessSlice.SellFuelToBakery(
                intake, dealerBusinessId, dealerRuntime, target, bakeryRuntime, ids,
                stillNeeded, priceCentsPerBakeryUnit, intakeUnitsPerBakeryUnit, dayIndex, diag);
            if (sale == null || !sale.Success)
                return sale?.Diagnostic ?? "Series2PaidDealerBakeryFuelSource: purchase failed; no production supply confirmed.";

            diag.Add($"Bakery fuel procurement: dealer '{dealerBusinessId}' sold {sale.IntakeUnits} intake unit(s) for {sale.TotalCents}c; Bakery received {sale.Units}u in lot {sale.BakeryLotId}.");
            return null;
        }
    }

    public sealed class ThreeBusinessBreadSaleResult
    {
        public bool Success;
        public int Units;
        public int TotalCents;
        public List<BakerySaleRecord> Records = new List<BakerySaleRecord>();
        public string Diagnostic = string.Empty;
    }

    public static class ThreeBusinessSlice
    {
        /// <summary>
        /// Feeds the bakery from General Store flour, as a PAID
        /// inter-business sale. The store lot consumption goes through
        /// ConsumeLots (those units leave sellable stock forever), the
        /// bakery pays the store at unitPriceCents per store unit, and
        /// the flour lands in the bakery bin through the real
        /// BakeryFlourSupply.ReceiveImportArrival, with the store
        /// order's supplier as origin and the consumption id as the
        /// import order id. UNIT SEMANTICS (Kennedy decision, flagged):
        /// a store lot unit is treated as lbPerStoreUnit lb of flour;
        /// the composed run uses 1. If Kennedy rules otherwise, this
        /// parameter, not the code, changes. On any failure after
        /// consumption, the consumption is undone and cash is restored.
        /// </summary>
        public static string FeedBakeryFromStore(
            OffMapNamedSkuProcurementService storeService,
            BusinessRuntimeState storeRuntime,
            BakeryShopRuntime bakery, BusinessRuntimeState bakeryRuntime,
            EntityIdRegistry ids,
            string skuId, int storeUnits, int lbPerStoreUnit,
            int unitPriceCents, int dayIndex, List<string> diag)
        {
            diag = diag ?? new List<string>();
            if (storeService == null || storeRuntime == null || bakery == null || bakeryRuntime == null || ids == null)
            {
                return "FeedBakeryFromStore: store service, store runtime, bakery, bakery runtime, and id registry are required.";
            }

            if (storeUnits <= 0 || lbPerStoreUnit <= 0 || unitPriceCents < 0)
            {
                return "FeedBakeryFromStore: positive store units, positive lb per store unit, and a non-negative price are required.";
            }

            NamedSkuLotConsumptionRecord consumption =
                storeService.ConsumeLots(skuId, storeUnits, "bakery-feed", dayIndex);
            if (consumption == null)
            {
                return "FeedBakeryFromStore: store lot consumption refused; see store diagnostics.";
            }

            int totalPrice = storeUnits * unitPriceCents;
            if (!bakeryRuntime.TrySpendCents(totalPrice))
            {
                storeService.UndoConsumption(consumption);
                return "FeedBakeryFromStore: bakery cash cannot pay the feed price; consumption undone, nothing moved.";
            }

            storeRuntime.AddCashCents(totalPrice);
            string supplierName = "unknown supplier";
            for (int i = 0; i < storeService.Orders.Count; i++)
            {
                if (storeService.Orders[i] != null && !string.IsNullOrWhiteSpace(storeService.Orders[i].SupplierName))
                {
                    supplierName = storeService.Orders[i].SupplierName;
                    break;
                }
            }

            string problem = BakeryFlourSupply.ReceiveImportArrival(
                bakery.FlourStock, BakeryBreadCatalog.FlourItemId,
                storeUnits * lbPerStoreUnit, consumption.ConsumptionId,
                $"General Store {storeService.BusinessId} via {supplierName}",
                dayIndex, ids, diag);
            if (problem != null)
            {
                storeService.UndoConsumption(consumption);
                storeRuntime.TrySpendCents(totalPrice);
                bakeryRuntime.AddCashCents(totalPrice);
                return "FeedBakeryFromStore: bakery bin refused the flour (" + problem + "); consumption undone, cash restored.";
            }

            diag.Add($"FeedBakeryFromStore: {storeUnits}u '{skuId}' from {storeService.BusinessId} " +
                $"became {storeUnits * lbPerStoreUnit} lb bakery flour for {totalPrice}c " +
                $"(consumption {consumption.ConsumptionId}).");
            return null;
        }

        /// <summary>
        /// Sells fuel from the FuelDealer intake to the bakery as a
        /// business-to-business sale. The intake has NO dispense API,
        /// so this bridge deducts FIFO in place from intake lots and
        /// records the consumed lot ids; a lot at 0 units is never
        /// re-sold because TotalFuelWoodUnitsFor counts Max(0, units).
        /// Cash: bakery runtime pays, dealer runtime receives. On a
        /// refused bakery-bin write, intake units and cash are restored.
        /// </summary>
        public static ThreeBusinessFuelSaleResult SellFuelToBakery(
            FuelYardByproductIntake intake, string dealerBusinessId,
            BusinessRuntimeState dealerRuntime, BakeryShopRuntime bakery,
            BusinessRuntimeState bakeryRuntime, EntityIdRegistry ids,
            int units, int unitPriceCents, int dayIndex, List<string> diag)
        {
            return SellFuelToBakery(intake, dealerBusinessId, dealerRuntime, bakery,
                bakeryRuntime, ids, units, unitPriceCents, 1, dayIndex, diag);
        }

        /// <summary>
        /// Paid fuel transfer with an explicit caller-authored intake-to-Bakery
        /// conversion. Price is per resulting Bakery fuel unit. Stock and funds
        /// are checked before movement, and all legs roll back on a rejected
        /// Bakery lot receipt.
        /// </summary>
        public static ThreeBusinessFuelSaleResult SellFuelToBakery(
            FuelYardByproductIntake intake, string dealerBusinessId,
            BusinessRuntimeState dealerRuntime, BakeryShopRuntime bakery,
            BusinessRuntimeState bakeryRuntime, EntityIdRegistry ids,
            int units, int priceCentsPerBakeryUnit, int intakeUnitsPerBakeryUnit,
            int dayIndex, List<string> diag)
        {
            var result = new ThreeBusinessFuelSaleResult();
            diag = diag ?? new List<string>();
            if (intake == null || dealerRuntime == null || bakery == null || bakeryRuntime == null || ids == null
                || string.IsNullOrWhiteSpace(dealerBusinessId) || units <= 0
                || priceCentsPerBakeryUnit < 0 || intakeUnitsPerBakeryUnit <= 0)
            {
                result.Diagnostic = "SellFuelToBakery: intake, dealer, bakery, runtimes, positive units, non-negative price, and positive authored conversion are required.";
                return result;
            }

            long intakeUnitsLong = (long)units * intakeUnitsPerBakeryUnit;
            long totalPriceLong = (long)units * priceCentsPerBakeryUnit;
            if (intakeUnitsLong > int.MaxValue || totalPriceLong > int.MaxValue)
            {
                result.Diagnostic = "SellFuelToBakery: requested quantity or price exceeds supported transaction limits; nothing moved.";
                return result;
            }
            int intakeUnits = (int)intakeUnitsLong;
            int totalPrice = (int)totalPriceLong;

            if (intake.TotalFuelWoodUnitsFor(dealerBusinessId) < intakeUnits)
            {
                result.Diagnostic = $"SellFuelToBakery: intake holds {intake.TotalFuelWoodUnitsFor(dealerBusinessId)}u for '{dealerBusinessId}', requested {intakeUnits}u; nothing moved.";
                return result;
            }

            if (!bakeryRuntime.TrySpendCents(totalPrice))
            {
                result.Diagnostic = $"SellFuelToBakery: bakery cash {bakeryRuntime.CurrentCashCents}c cannot pay {totalPrice}c; nothing moved.";
                return result;
            }

            var taken = new List<KeyValuePair<SlabOffcutFuelLot, int>>();
            int remaining = intakeUnits;
            foreach (var lot in intake.LotsFor(dealerBusinessId))
            {
                if (remaining <= 0) break;
                if (lot == null || lot.FuelWoodUnits <= 0) continue;
                int take = Math.Min(lot.FuelWoodUnits, remaining);
                lot.FuelWoodUnits -= take;
                taken.Add(new KeyValuePair<SlabOffcutFuelLot, int>(lot, take));
                result.ConsumedIntakeLotIds.Add(lot.LotId.ToString());
                remaining -= take;
            }

            if (remaining > 0)
            {
                foreach (var entry in taken) entry.Key.FuelWoodUnits += entry.Value;
                bakeryRuntime.AddCashCents(totalPrice);
                result.Diagnostic = "SellFuelToBakery rolled back: intake deduction failed after validation; units and cash restored.";
                return result;
            }

            dealerRuntime.AddCashCents(totalPrice);
            string firstLotId = taken.Count > 0 ? taken[0].Key.LotId.ToString() : string.Empty;
            EntityId bakeryLotId = ids.Allocate(EntityKind.Lot);
            string problem = bakery.FuelStock.ReceiveLot(new BakeryFuelLot
            {
                LotId = bakeryLotId,
                FuelName = BakeryBreadCatalog.FuelItemId,
                Units = units,
                AcquiredDayIndex = dayIndex,
                FuelDealerBusinessId = dealerBusinessId,
                SourceFuelLotId = firstLotId,
                OriginName = $"FuelDealer {dealerBusinessId} town fuel yard",
                SupplierNote = $"sawmill byproduct fuel, intake lots [{string.Join(", ", result.ConsumedIntakeLotIds)}]",
                IsBootstrapEndowment = false,
            }, diag);
            if (problem != null)
            {
                foreach (var entry in taken) entry.Key.FuelWoodUnits += entry.Value;
                dealerRuntime.TrySpendCents(totalPrice);
                bakeryRuntime.AddCashCents(totalPrice);
                result.Diagnostic = "SellFuelToBakery: bakery fuel bin refused the lot (" + problem + "); intake units and cash restored.";
                result.ConsumedIntakeLotIds.Clear();
                return result;
            }

            result.Success = true;
            result.Units = units;
            result.IntakeUnits = intakeUnits;
            result.TotalCents = totalPrice;
            result.BakeryLotId = bakeryLotId.ToString();
            result.Diagnostic = $"SellFuelToBakery: {units}u Bakery fuel from '{dealerBusinessId}' ({intakeUnits} intake units) for {totalPrice}c.";
            return result;
        }

        /// <summary>
        /// Sells fuel from the FuelDealer intake to a household: the
        /// same FIFO in-place deduction, household outflow through
        /// HouseholdLedger, dealer cash credit, and a fuel_wood lot in
        /// HouseholdInventory. Any failed leg restores intake units
        /// and cash. Fuel is bought, not eaten: it never appears in
        /// meal service (fuel_wood is food false in the catalog).
        /// </summary>
        public static ThreeBusinessFuelSaleResult SellFuelToHousehold(
            FuelYardByproductIntake intake, string dealerBusinessId,
            BusinessRuntimeState dealerRuntime,
            HouseholdLedger householdLedger, HouseholdInventory inventory,
            int householdId, int actingPersonId,
            int units, int unitPriceCents, int dayIndex, List<string> diag)
        {
            var result = new ThreeBusinessFuelSaleResult();
            diag = diag ?? new List<string>();
            if (intake == null || dealerRuntime == null || householdLedger == null || inventory == null
                || householdLedger.HouseholdId != householdId || inventory.HouseholdId != householdId
                || string.IsNullOrWhiteSpace(dealerBusinessId) || units <= 0 || unitPriceCents < 0)
            {
                result.Diagnostic = "SellFuelToHousehold: intake, dealer runtime, matching household ledger and inventory, positive units, and a non-negative price are required.";
                return result;
            }

            if (intake.TotalFuelWoodUnitsFor(dealerBusinessId) < units)
            {
                result.Diagnostic = $"SellFuelToHousehold: intake holds {intake.TotalFuelWoodUnitsFor(dealerBusinessId)}u for '{dealerBusinessId}', requested {units}u; nothing moved.";
                return result;
            }

            int totalPrice = units * unitPriceCents;
            if (totalPrice > householdLedger.GetBalanceCents())
            {
                result.Diagnostic = $"SellFuelToHousehold: household H{householdId} balance {householdLedger.GetBalanceCents()}c cannot pay {totalPrice}c; nothing moved.";
                return result;
            }

            var taken = new List<KeyValuePair<SlabOffcutFuelLot, int>>();
            int remaining = units;
            foreach (var lot in intake.LotsFor(dealerBusinessId))
            {
                if (remaining <= 0) break;
                if (lot == null || lot.FuelWoodUnits <= 0) continue;
                int take = Math.Min(lot.FuelWoodUnits, remaining);
                lot.FuelWoodUnits -= take;
                taken.Add(new KeyValuePair<SlabOffcutFuelLot, int>(lot, take));
                result.ConsumedIntakeLotIds.Add(lot.LotId.ToString());
                remaining -= take;
            }

            if (remaining > 0)
            {
                foreach (var entry in taken) entry.Key.FuelWoodUnits += entry.Value;
                result.Diagnostic = "SellFuelToHousehold rolled back: intake deduction failed after validation; units restored.";
                result.ConsumedIntakeLotIds.Clear();
                return result;
            }

            dealerRuntime.AddCashCents(totalPrice);
            string outflowProblem = householdLedger.RecordOutflow(
                dayIndex, totalPrice, $"purchase {units}u fuel_wood from {dealerBusinessId}", dealerBusinessId);
            if (outflowProblem != null)
            {
                foreach (var entry in taken) entry.Key.FuelWoodUnits += entry.Value;
                dealerRuntime.TrySpendCents(totalPrice);
                result.Diagnostic = "SellFuelToHousehold: household debit refused (" + outflowProblem + "); intake units and dealer cash restored.";
                result.ConsumedIntakeLotIds.Clear();
                return result;
            }

            string addProblem = inventory.AddLot(
                HouseholdItemCatalog.FuelWoodId, units, dayIndex,
                $"fuel_wood from {dealerBusinessId}, intake lots [{string.Join(", ", result.ConsumedIntakeLotIds)}]",
                qualityBand: string.Empty, sourceKind: "fuel-dealer-sale", ownerPersonId: actingPersonId);
            if (addProblem != null)
            {
                foreach (var entry in taken) entry.Key.FuelWoodUnits += entry.Value;
                dealerRuntime.TrySpendCents(totalPrice);
                householdLedger.RecordInflow(dayIndex, totalPrice, HouseholdIncomeSource.SaleProceeds,
                    dealerBusinessId, $"refund: reversed fuel sale ({units}u fuel_wood)", dealerBusinessId);
                result.Diagnostic = "SellFuelToHousehold: household inventory refused the lot (" + addProblem + "); sale rolled back.";
                result.ConsumedIntakeLotIds.Clear();
                return result;
            }

            result.Success = true;
            result.Units = units;
            result.TotalCents = totalPrice;
            result.Diagnostic = $"SellFuelToHousehold: H{householdId} bought {units}u fuel_wood from '{dealerBusinessId}' for {totalPrice}c.";
            return result;
        }

        /// <summary>
        /// Sells bakery bread to a household. Pre-checks the household
        /// against the fresh price (the highest the shelf can charge),
        /// then settles at the actual SellBread prices (day-old lots
        /// discount automatically). A partial fill is refused as a
        /// whole: shelf units are restored and no cash moves, so a
        /// household never pays for bread it did not get. A failed
        /// inventory write restores shelf units and reverses cash.
        /// </summary>
        public static ThreeBusinessBreadSaleResult SellBreadToHousehold(
            BakeryShopRuntime bakery, BusinessRuntimeState bakeryRuntime, string bakeryBusinessId,
            HouseholdLedger householdLedger, HouseholdInventory inventory,
            int householdId, int actingPersonId,
            string productId, int units, int dayIndex, List<string> diag)
        {
            var result = new ThreeBusinessBreadSaleResult();
            diag = diag ?? new List<string>();
            if (bakery == null || bakeryRuntime == null || householdLedger == null || inventory == null
                || householdLedger.HouseholdId != householdId || inventory.HouseholdId != householdId
                || string.IsNullOrWhiteSpace(productId) || units <= 0)
            {
                result.Diagnostic = "SellBreadToHousehold: bakery, bakery runtime, matching household ledger and inventory, product, and positive units are required.";
                return result;
            }

            int freshPrice = BakeryBreadCatalog.GetFreshPriceCents(productId, bakery.Prices);
            if (freshPrice <= 0 || units * freshPrice > householdLedger.GetBalanceCents())
            {
                result.Diagnostic = $"SellBreadToHousehold: household H{householdId} balance {householdLedger.GetBalanceCents()}c cannot cover {units}u {productId} at the fresh price {freshPrice}c; nothing moved.";
                return result;
            }

            var shelfBefore = new Dictionary<EntityId, int>();
            foreach (var lot in bakery.BreadShelf)
            {
                if (lot != null) shelfBefore[lot.LotId] = lot.UnitCount;
            }

            List<BakerySaleRecord> records = bakery.SellBread(productId, units, dayIndex, diag);
            int sold = 0;
            int total = 0;
            foreach (var record in records)
            {
                if (record == null) continue;
                sold += record.Units;
                total += record.TotalCents;
            }

            if (sold < units)
            {
                foreach (var lot in bakery.BreadShelf)
                {
                    if (lot != null && shelfBefore.TryGetValue(lot.LotId, out int before)) lot.UnitCount = before;
                }

                result.Diagnostic = $"SellBreadToHousehold: only {sold}/{units}u {productId} on the shelf; partial sale refused, shelf restored, no cash moved.";
                return result;
            }

            bakeryRuntime.AddCashCents(total);
            string outflowProblem = householdLedger.RecordOutflow(
                dayIndex, total, $"purchase {units}u {productId} from bakery", bakeryBusinessId);
            if (outflowProblem != null)
            {
                foreach (var lot in bakery.BreadShelf)
                {
                    if (lot != null && shelfBefore.TryGetValue(lot.LotId, out int before)) lot.UnitCount = before;
                }

                bakeryRuntime.TrySpendCents(total);
                result.Diagnostic = "SellBreadToHousehold: household debit refused (" + outflowProblem + "); shelf and cash restored.";
                return result;
            }

            string addProblem = inventory.AddLot(
                HouseholdItemCatalog.BreadId, units, dayIndex,
                $"bread from bakery, sale lots [{string.Join(", ", records.ConvertAll(r => r.LotId.ToString()))}]",
                qualityBand: string.Empty, sourceKind: "bakery-sale", ownerPersonId: actingPersonId);
            if (addProblem != null)
            {
                foreach (var lot in bakery.BreadShelf)
                {
                    if (lot != null && shelfBefore.TryGetValue(lot.LotId, out int before)) lot.UnitCount = before;
                }

                bakeryRuntime.TrySpendCents(total);
                householdLedger.RecordInflow(dayIndex, total, HouseholdIncomeSource.SaleProceeds,
                    bakeryBusinessId, $"refund: reversed bread sale ({units}u {productId})", bakeryBusinessId);
                result.Diagnostic = "SellBreadToHousehold: household inventory refused the lot (" + addProblem + "); sale rolled back.";
                return result;
            }

            result.Success = true;
            result.Units = sold;
            result.TotalCents = total;
            result.Records = records;
            result.Diagnostic = $"SellBreadToHousehold: H{householdId} bought {sold}u {productId} for {total}c.";
            return result;
        }
    }

    public sealed class ThreeBusinessSliceRunReport
    {
        public int StoreCashFinalCents;
        public int BakeryCashFinalCents;
        public int FuelCashFinalCents;
        public int BaldwinCashFinalCents;
        public int PierceCashFinalCents;
        public int BonieckiCashFinalCents;
        public int StoreLotRemainingUnits;
        public int BakeryLoavesBaked;
        public int BakeryLoavesRemaining;
        public int BakeryFuelRemaining;
        public int FuelIntakeRemaining;
        public int FuelWoodUnitsProduced;
        public int MealsBaldwin;
        public int MealsPierce;
        public int MealsBoniecki;
        public int MealsMissedTotal;
        public bool FounderWageRefused;
        public bool ReserveBreakingDrawRefused;
        public bool OverSaleFlourRefused;
        public bool OverSaleBreadRefused;
        public bool OverSaleFuelRefused;
        public bool ConsumptionRestoredCountMatches;
        public bool SuppressMigrantArrivalsRestored;
        public List<string> SeamsStopped = new List<string>();
        public List<string> Diagnostics = new List<string>();
    }

    /// <summary>
    /// Composed three-business run. All numbers are caller-supplied
    /// test parameters. Business seeds are labelled owner-equity run
    /// inputs (seed amounts are caller-supplied test inputs, variable
    /// between runs, not canon); households start at 0 in this run and
    /// earn only through draws and sales in this run. Returns null
    /// only when a step that must succeed fails; diagnostics say why.
    /// </summary>
    public static class ThreeBusinessSliceRun
    {
        private sealed class FakeMigrationHolder : IPassXlMigrationFlagHolder
        {
            public bool SuppressMigrantArrivals { get; set; }
        }

        private sealed class FakePlayerHolder : IPassXlPlayerFlagHolder
        {
            public bool TreatPlayerAsNpc { get; set; }
        }

        private static Func<string, WorkstationComponentView?> OvenFinder()
        {
            var kinds = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                { "oven-chamber-1", "oven-chamber" },
                { "kneading-table-1", "kneading-table" },
                { "proofing-rack-1", "proofing-rack" },
                { "bake-peels-1", "bake-peels" },
            };
            return assetId => kinds.TryGetValue(assetId, out string kind)
                ? (WorkstationComponentView?)new WorkstationComponentView
                {
                    AssetId = assetId,
                    Kind = kind,
                    Condition01 = 0.9f,
                    IsUsable = true,
                }
                : null;
        }

        public static ThreeBusinessSliceRunReport Execute(
            int startDay,
            int storeSeedCents, int bakerySeedCents, int fuelSeedCents,
            int storeReserveCents, int bakeryReserveCents, int fuelReserveCents,
            int orderUnits, int unitGoodsCostCents, int freightSurchargeCents,
            int marketSupplyUnits, int transitDays,
            int storeFlourSaleUnits, int storeFlourSalePriceCents,
            int feedStoreUnits, int feedPriceCents, int notionsInputUnits,
            int bakeryFuelUnits, int bakeryFuelPriceCents,
            int breadSaleUnitsPerHousehold,
            int fuelHouseholdUnits, int fuelHouseholdPriceCents,
            int kennedyDrawCents, int glenDrawCents, int jacobDrawCents,
            int finalDrawCents)
        {
            var report = new ThreeBusinessSliceRunReport();
            report.SeamsStopped.Add("Staged bakery chain: no transfer API, no delivered-mark set, chain context is Codex-only; slice bakery is the shop runtime (see ThreeBusinessSlice header).");
            report.SeamsStopped.Add("Notions supply: notions arrive as a labelled run input through a named import order; no store SKU or miller path exists for notions yet.");
            report.SeamsStopped.Add("Equipment gates: fuel chain runs with gate null; axe, wagon, and saw-line possession are NOT proved and must be supplied by a production driver.");
            report.SeamsStopped.Add("Daily/weekly tick driver, residence assignment, postal office, production supplier registration, Main Scene wiring: Codex-only, as in phases 6-7.");
            report.SeamsStopped.Add("First-cycle business seeds: stores, bakery, and fuel dealer start with caller-supplied owner equity, not slice earnings; a business with no cash cannot order, bake fuel, or sell before it has cash.");
            report.SeamsStopped.Add("Sawmill: the mill is an upstream supplier outside the six-person fixture; its lumber stays at the mill and is not sold in this run.");

            var diag = new List<string>();
            var ids = new EntityIdRegistry();
            int arrivalDay = startDay + transitDays;

            // Business runtimes (cash holders) and households (0 cash in this run).
            var store = new BusinessRuntimeState();
            store.BindBusinessIdentity("general-store-1");
            store.AddCashCents(Math.Max(0, storeSeedCents));
            var bakeryRuntime = new BusinessRuntimeState();
            bakeryRuntime.BindBusinessIdentity("pierce-bakery-1");
            bakeryRuntime.AddCashCents(Math.Max(0, bakerySeedCents));
            var fuelRuntime = new BusinessRuntimeState();
            fuelRuntime.BindBusinessIdentity("boniecki-fuel-dealer-1");
            fuelRuntime.AddCashCents(Math.Max(0, fuelSeedCents));
            var ledgers = new HouseholdLedgerRegistry();
            HouseholdLedger baldwin = ledgers.GetOrCreate(1);
            HouseholdLedger pierce = ledgers.GetOrCreate(2);
            HouseholdLedger boniecki = ledgers.GetOrCreate(3);
            var inventories = new HouseholdInventoryRegistry();
            HouseholdInventory baldwinInv = inventories.GetOrCreate(1);
            HouseholdInventory pierceInv = inventories.GetOrCreate(2);
            HouseholdInventory bonieckiInv = inventories.GetOrCreate(3);

            // Owner labour, no wages (TECH 4.1).
            if (SliceWorkAndPay.RecordOwnerLabour(1, "general-store-1", startDay, 7).WageCents != 0
                || SliceWorkAndPay.RecordOwnerLabour(3, "pierce-bakery-1", startDay, 7).WageCents != 0
                || SliceWorkAndPay.RecordOwnerLabour(5, "boniecki-fuel-dealer-1", startDay, 7).WageCents != 0)
            {
                report.Diagnostics.Add("Run failed: owner labour carried a wage.");
                return null;
            }

            // Fuel chain, day 0: stand, rights, fell, haul, saw, transfer.
            var registry = new LoggingStandRegistry();
            string standProblem = registry.RegisterStand(new TimberStand
            {
                StandId = "stand-slice-1",
                DisplayName = "Slice Test Pines",
                LocationId = "landing-1",
                Species = "white pine",
                StandingTimberUnits = 10,
            });
            if (standProblem != null) { report.Diagnostics.Add("Run failed: stand registration refused: " + standProblem); return null; }
            string grantProblem = registry.GrantTimberRights("stand-slice-1", "boniecki-fuel-dealer-1", startDay, "town-land-office");
            if (grantProblem != null) { report.Diagnostics.Add("Run failed: timber rights grant refused: " + grantProblem); return null; }
            var logging = new LoggingOperationRuntime("boniecki-fuel-dealer-1", ids);
            var fellingTools = new EquipmentTaskGate
            {
                AssetsSupplier = () => new List<EquipmentAsset>
                {
                    new EquipmentAsset
                    {
                        AssetId = "slice-felling-axe",
                        Kind = "felling-axe",
                        OwnerKind = "business",
                        OwnerId = "boniecki-fuel-dealer-1",
                        Condition01 = 1f,
                    },
                },
            };
            LogLot felled = logging.RunFelling(registry, "stand-slice-1", "boniecki-fuel-dealer-1", 4,
                EntityId.For(EntityKind.Person, 5), startDay, diag, fellingTools);
            if (felled == null) { report.Diagnostics.Add("Run failed: felling refused."); report.Diagnostics.AddRange(diag); return null; }
            var journey = new JourneyModel();
            journey.RegisterLocation(new JourneyLocation("landing-1", JourneyLocationKind.Waypoint, "North Landing", 2f, 0f));
            journey.RegisterLocation(new JourneyLocation("mill-1-yard", JourneyLocationKind.Mill, "Local Sawmill", 0f, 0f) { BusinessId = "slice-mill-1" });
            journey.AddEdge("landing-1", "mill-1-yard", 2f, "river road");
            LogHaul haul = logging.DispatchLogHaul(felled.LotId, "slice-mill-1", "mill-1-yard", journey, "landing-1", startDay, diag);
            if (haul == null) { report.Diagnostics.Add("Run failed: log haul refused."); report.Diagnostics.AddRange(diag); return null; }
            logging.AdvanceHauls(1000, diag);
            var mill = new SawmillShopRuntime("slice-mill-1", ids);
            logging.DeliverArrivedHauls(mill.LogStock, diag);
            if (mill.LogStock.TotalLogUnits != 4) { report.Diagnostics.Add($"Run failed: mill log stock is {mill.LogStock.TotalLogUnits}, expected 4."); report.Diagnostics.AddRange(diag); return null; }
            EntityId sawyer = EntityId.For(EntityKind.Person, 5);
            var sawTasks = new TaskAuthority();
            Sawmill.RegisterTaskDefinitions(sawTasks);
            var sawBudgets = new WorkTimeBudgetStore();
            sawBudgets.EnsureDay(startDay);
            var sawTask = sawTasks.CreateTaskWithPlannedMinutes(Sawmill.SawLogsTaskId,
                EntityId.For(EntityKind.Business, 6), startDay, 4 * SawmillConversionData.DefaultForSpecies("white pine").SawMinutesPerLog);
            string sawAssignFailure = null;
            string sawStartFailure = null;
            string sawWorkFailure = null;
            if (!sawTasks.AssignTask(sawTask.TaskId, sawyer, sawBudgets, startDay, out sawAssignFailure)
                || !sawTasks.StartTask(sawTask.TaskId, startDay, out sawStartFailure)
                || !sawTasks.RecordWorkProgress(sawTask.TaskId, sawTask.PlannedMinutes, sawBudgets, startDay,
                    out bool sawWorkComplete, out sawWorkFailure)
                || !sawWorkComplete)
            {
                report.Diagnostics.Add("Run failed: saw-line labor did not complete: "
                    + (sawAssignFailure ?? sawStartFailure ?? sawWorkFailure ?? "planned simulated work remains."));
                return null;
            }
            var sawStations = new BusinessWorkstations("slice-mill-1");
            var sawStation = sawStations.GetOrCreate("sawmill-saw-line");
            sawStation.SpaceId = "slice-mill-saw-floor";
            sawStation.InstallComponent("slice-saw-line");
            sawStation.InstallComponent("slice-log-carriage");
            sawStation.InstallComponent("slice-drive-system");
            sawStation.MotivePowerSources.Add(new MotivePowerSource(MotivePowerKind.Steam, "recorded steam engine"));
            var sawGate = new EquipmentTaskGate
            {
                WorkstationsFor = (kind, id) => sawStations,
                FindWorkstationDefinition = id => id == "sawmill-saw-line" ? WorkstationCatalog.SawmillSawLine : null,
                FindComponent = id => id == "slice-saw-line"
                    ? new WorkstationComponentView { AssetId = id, Kind = "saw-line", Condition01 = 1f, IsUsable = true }
                    : id == "slice-log-carriage"
                        ? new WorkstationComponentView { AssetId = id, Kind = "log-carriage", Condition01 = 1f, IsUsable = true }
                        : id == "slice-drive-system"
                            ? new WorkstationComponentView { AssetId = id, Kind = "drive-system", Condition01 = 1f, IsUsable = true }
                            : (WorkstationComponentView?)null,
                SupportSupplier = () =>
                {
                    var support = new SupportContext();
                    support.ConsumableUnits[SupportContext.Key("consumable", "mill-lubrication")] = 1;
                    support.AvailableRepairCapabilities.Add("saw-filing");
                    return support;
                },
            };
            SawmillSawingResult sawing = mill.RunSawing(4, sawyer, startDay, diag,
                sawTasks, sawTask.TaskId, sawBudgets, sawGate);
            if (sawing == null || sawing.ByproductLot == null) { report.Diagnostics.Add("Run failed: sawing refused."); report.Diagnostics.AddRange(diag); return null; }
            report.FuelWoodUnitsProduced = sawing.ByproductLot.FuelWoodUnits;
            var intake = new FuelYardByproductIntake();
            int transferred = mill.TransferByproductsToFuelDealer("boniecki-fuel-dealer-1", intake, diag);
            if (transferred != 1 || intake.TotalFuelWoodUnitsFor("boniecki-fuel-dealer-1") != report.FuelWoodUnitsProduced)
            {
                report.Diagnostics.Add($"Run failed: byproduct transfer moved {transferred} lot(s), intake holds {intake.TotalFuelWoodUnitsFor("boniecki-fuel-dealer-1")}u, produced {report.FuelWoodUnitsProduced}u.");
                report.Diagnostics.AddRange(diag);
                return null;
            }

            // Fuel dealer -> bakery (business to business, paid).
            var bakery = new BakeryShopRuntime("pierce-bakery-1");
            ThreeBusinessFuelSaleResult fuelToBakery = ThreeBusinessSlice.SellFuelToBakery(
                intake, "boniecki-fuel-dealer-1", fuelRuntime, bakery, bakeryRuntime, ids,
                bakeryFuelUnits, bakeryFuelPriceCents, startDay, diag);
            if (!fuelToBakery.Success) { report.Diagnostics.Add("Run failed: fuel to bakery refused: " + fuelToBakery.Diagnostic); return null; }

            // General Store order, arrival day 3.
            var market = new ExternalMarketAuthority();
            var supplier = new ExternalCounterparty
            {
                CounterpartyId = "test-grain",
                DisplayName = "Test Grain Co.",
                InitialBudgetCents = 50000,
                TransitDays = transitDays,
            };
            supplier.SupplyUnits["flour"] = marketSupplyUnits;
            string supplierProblem = market.RegisterCounterparty(supplier);
            if (supplierProblem != null) { report.Diagnostics.Add("Run failed: supplier registration refused: " + supplierProblem); return null; }
            var storeCash = new BusinessRuntimeCashPort(store);
            var service = new OffMapNamedSkuProcurementService("general-store-1", market, storeCash);
            NamedSkuOrder order = service.PlaceOrder("flour", orderUnits, unitGoodsCostCents, freightSurchargeCents,
                "test-grain", startDay, NamedSkuPaymentMode.CashOnArrival, null);
            if (order == null) { report.Diagnostics.Add("Run failed: flour order refused."); report.Diagnostics.AddRange(service.Diagnostics); return null; }
            List<NamedSkuLot> arrived = service.ReceiveArrivals(order.ExpectedArrivalDayIndex, null);
            if (arrived.Count != 1) { report.Diagnostics.Add($"Run failed: expected 1 flour lot on arrival, got {arrived.Count}."); return null; }

            // Day-to-day owner draws fund the households; no household input.
            SliceOwnerDrawResult kennedyDraw = SliceWorkAndPay.DrawDaily(store, baldwin, arrivalDay, kennedyDrawCents, storeReserveCents);
            if (!kennedyDraw.Success) { report.Diagnostics.Add("Run failed: Kennedy day-draw refused: " + kennedyDraw.Diagnostic); return null; }
            OffMapNamedSkuProcurementService.NamedSkuDeliveryResult flourSale =
                service.SellAndDeliverToInventory(baldwin, baldwinInv, 1, 1, "person:1",
                    "flour", storeFlourSaleUnits, storeFlourSalePriceCents, arrivalDay);
            if (flourSale == null || !flourSale.Success) { report.Diagnostics.Add("Run failed: Baldwin-Ooms flour sale refused: " + (flourSale != null ? flourSale.Diagnostic : "null")); return null; }

            // Bakery feed: paid inter-business sale, then notions input, then bake.
            string feedProblem = ThreeBusinessSlice.FeedBakeryFromStore(
                service, store, bakery, bakeryRuntime, ids,
                "flour", feedStoreUnits, 1, feedPriceCents, arrivalDay, diag);
            if (feedProblem != null) { report.Diagnostics.Add("Run failed: bakery feed refused: " + feedProblem); return null; }
            string notionsProblem = BakeryFlourSupply.ReceiveImportArrival(
                bakery.FlourStock, BakeryBreadCatalog.NotionsItemId, notionsInputUnits,
                "passxl-phase8-notions-input", "off-map notions supplier (test parameter, not a registered supplier)",
                arrivalDay, ids, diag);
            if (notionsProblem != null) { report.Diagnostics.Add("Run failed: notions input refused: " + notionsProblem); return null; }
            SliceOwnerDrawResult glenDraw = SliceWorkAndPay.DrawDaily(bakeryRuntime, pierce, arrivalDay, glenDrawCents, bakeryReserveCents);
            if (!glenDraw.Success) { report.Diagnostics.Add("Run failed: Glen day-draw refused: " + glenDraw.Diagnostic); return null; }
            SliceOwnerDrawResult jacobDraw = SliceWorkAndPay.DrawDaily(fuelRuntime, boniecki, arrivalDay, jacobDrawCents, fuelReserveCents);
            if (!jacobDraw.Success) { report.Diagnostics.Add("Run failed: Jacob day-draw refused: " + jacobDraw.Diagnostic); return null; }
            string ovenProblem = bakery.AddOven("bakehouse", new List<string>
                { "oven-chamber-1", "kneading-table-1", "proofing-rack-1", "bake-peels-1" }, diag);
            if (ovenProblem != null) { report.Diagnostics.Add("Run failed: bakery oven install refused: " + ovenProblem); return null; }
            List<string> batches = bakery.PlanBatch(BakeryBreadCatalog.BreadLoafId, 1, arrivalDay, diag);
            if (batches == null || batches.Count != 1) { report.Diagnostics.Add("Run failed: bakery batch planning refused."); report.Diagnostics.AddRange(diag); return null; }
            int baked = bakery.WorkDay(arrivalDay, true, WorkstationCatalog.BakeOven, OvenFinder(), diag, ids);
            if (baked != 1 || bakery.BreadShelf.Count != 1) { report.Diagnostics.Add($"Run failed: bake produced {baked} batch(es), shelf has {bakery.BreadShelf.Count} lot(s)."); report.Diagnostics.AddRange(diag); return null; }
            report.BakeryLoavesBaked = bakery.BreadShelf[0].UnitCount;

            // Bread sales to all three households.
            ThreeBusinessBreadSaleResult breadPierce = ThreeBusinessSlice.SellBreadToHousehold(
                bakery, bakeryRuntime, "pierce-bakery-1", pierce, pierceInv, 2, 3,
                BakeryBreadCatalog.BreadLoafId, breadSaleUnitsPerHousehold, arrivalDay, diag);
            if (!breadPierce.Success) { report.Diagnostics.Add("Run failed: Pierce bread sale refused: " + breadPierce.Diagnostic); return null; }
            ThreeBusinessBreadSaleResult breadBoniecki = ThreeBusinessSlice.SellBreadToHousehold(
                bakery, bakeryRuntime, "pierce-bakery-1", boniecki, bonieckiInv, 3, 5,
                BakeryBreadCatalog.BreadLoafId, breadSaleUnitsPerHousehold, arrivalDay, diag);
            if (!breadBoniecki.Success) { report.Diagnostics.Add("Run failed: Boniecki bread sale refused: " + breadBoniecki.Diagnostic); return null; }
            ThreeBusinessBreadSaleResult breadBaldwin = ThreeBusinessSlice.SellBreadToHousehold(
                bakery, bakeryRuntime, "pierce-bakery-1", baldwin, baldwinInv, 1, 1,
                BakeryBreadCatalog.BreadLoafId, breadSaleUnitsPerHousehold, arrivalDay, diag);
            if (!breadBaldwin.Success) { report.Diagnostics.Add("Run failed: Baldwin-Ooms bread sale refused: " + breadBaldwin.Diagnostic); return null; }
            report.BakeryLoavesRemaining = 0;
            foreach (var lot in bakery.BreadShelf) report.BakeryLoavesRemaining += Math.Max(0, lot.UnitCount);

            // Fuel sale to a household (fuel is bought, not eaten).
            ThreeBusinessFuelSaleResult fuelSale = ThreeBusinessSlice.SellFuelToHousehold(
                intake, "boniecki-fuel-dealer-1", fuelRuntime, baldwin, baldwinInv, 1, 1,
                fuelHouseholdUnits, fuelHouseholdPriceCents, arrivalDay, diag);
            if (!fuelSale.Success) { report.Diagnostics.Add("Run failed: household fuel sale refused: " + fuelSale.Diagnostic); return null; }

            // Meals, day 3, all three households, from real inventory.
            var mealLog = new HouseholdMealLogRegistry();
            var mealService = new HouseholdMealService(mealLog);
            MealDayResult baldwinMeals = mealService.ServeHouseholdDay(
                new HouseholdState { id = 1, memberIds = new List<int> { 1, 2 } }, new List<int> { 1, 2 },
                baldwinInv, MealSchedulingPolicy.Default, arrivalDay, null);
            MealDayResult pierceMeals = mealService.ServeHouseholdDay(
                new HouseholdState { id = 2, memberIds = new List<int> { 3, 4 } }, new List<int> { 3, 4 },
                pierceInv, MealSchedulingPolicy.Default, arrivalDay, null);
            MealDayResult bonieckiMeals = mealService.ServeHouseholdDay(
                new HouseholdState { id = 3, memberIds = new List<int> { 5, 6 } }, new List<int> { 5, 6 },
                bonieckiInv, MealSchedulingPolicy.Default, arrivalDay, null);
            report.MealsBaldwin = baldwinMeals.MealsServed;
            report.MealsPierce = pierceMeals.MealsServed;
            report.MealsBoniecki = bonieckiMeals.MealsServed;
            report.MealsMissedTotal = baldwinMeals.MealsMissed + pierceMeals.MealsMissed + bonieckiMeals.MealsMissed;

            // Controls: founder wage refusal, reserve-breaking draw, over-sales.
            var payrollSetup = SliceWorkAndPay.CreateBusiness("pierce-bakery-1", 0,
                new Dictionary<int, int> { { 3, 2 }, { 4, 2 } });
            string wageRefusal = SliceWorkAndPay.RegisterEmployee(payrollSetup, "emp-glen", 3, "pierce-bakery-1", 320, arrivalDay, true);
            report.FounderWageRefused = wageRefusal != null;
            SliceOwnerDrawResult breakingDraw = SliceWorkAndPay.DrawDaily(bakeryRuntime, pierce, arrivalDay + 1, 100000, bakeryReserveCents);
            report.ReserveBreakingDrawRefused = !breakingDraw.Success;
            NamedSkuSale overFlour = service.SellToHousehold(baldwin, 1, 1, "person:1", "flour", service.SellableUnits("flour") + 1, storeFlourSalePriceCents, arrivalDay);
            report.OverSaleFlourRefused = overFlour == null;
            ThreeBusinessBreadSaleResult overBread = ThreeBusinessSlice.SellBreadToHousehold(
                bakery, bakeryRuntime, "pierce-bakery-1", baldwin, baldwinInv, 1, 1,
                BakeryBreadCatalog.BreadLoafId, report.BakeryLoavesRemaining + 1, arrivalDay, diag);
            report.OverSaleBreadRefused = !overBread.Success;
            ThreeBusinessFuelSaleResult overFuel = ThreeBusinessSlice.SellFuelToHousehold(
                intake, "boniecki-fuel-dealer-1", fuelRuntime, baldwin, baldwinInv, 1, 1,
                intake.TotalFuelWoodUnitsFor("boniecki-fuel-dealer-1") + 1, fuelHouseholdPriceCents, arrivalDay, diag);
            report.OverSaleFuelRefused = !overFuel.Success;

            // Final day-to-day draws, day 4: leaving money in is also
            // exercised by the reserve itself (draws are capped, the
            // remainder stays in each business for its next order).
            SliceOwnerDrawResult kennedyFinal = SliceWorkAndPay.DrawDaily(store, baldwin, arrivalDay + 1, finalDrawCents, storeReserveCents);
            SliceOwnerDrawResult glenFinal = SliceWorkAndPay.DrawDaily(bakeryRuntime, pierce, arrivalDay + 1, finalDrawCents, bakeryReserveCents);
            SliceOwnerDrawResult jacobFinal = SliceWorkAndPay.DrawDaily(fuelRuntime, boniecki, arrivalDay + 1, finalDrawCents, fuelReserveCents);
            if (!kennedyFinal.Success || !glenFinal.Success || !jacobFinal.Success)
            {
                report.Diagnostics.Add("Run failed: a final day-draw was refused.");
                return null;
            }

            // Slice gate survives in the save DTO (phase 2 pattern).
            var migrationHolder = new FakeMigrationHolder { SuppressMigrantArrivals = true };
            var playerHolder = new FakePlayerHolder { TreatPlayerAsNpc = false };
            LandLedgers.Persistence.PassXlSliceSaveDto dto = LandLedgers.Persistence.PassXlSliceSaveCoordinator.Capture(migrationHolder, playerHolder, service);
            var restoredMigration = new FakeMigrationHolder();
            var restoredPlayer = new FakePlayerHolder();
            var restoredService = new OffMapNamedSkuProcurementService("general-store-1", market, new BusinessRuntimeCashPort(store));
            LandLedgers.Persistence.PassXlSliceSaveCoordinator.Restore(dto, restoredMigration, restoredPlayer, restoredService);
            report.SuppressMigrantArrivalsRestored = restoredMigration.SuppressMigrantArrivals;
            report.ConsumptionRestoredCountMatches = restoredService.LotConsumptions.Count == service.LotConsumptions.Count
                && restoredService.Lots.Count > 0 && restoredService.Lots[0].RemainingUnits == service.Lots[0].RemainingUnits;

            report.StoreCashFinalCents = store.CurrentCashCents;
            report.BakeryCashFinalCents = bakeryRuntime.CurrentCashCents;
            report.FuelCashFinalCents = fuelRuntime.CurrentCashCents;
            report.BaldwinCashFinalCents = baldwin.GetBalanceCents();
            report.PierceCashFinalCents = pierce.GetBalanceCents();
            report.BonieckiCashFinalCents = boniecki.GetBalanceCents();
            report.StoreLotRemainingUnits = service.Lots.Count > 0 ? service.Lots[0].RemainingUnits : 0;
            report.BakeryFuelRemaining = bakery.FuelStock.UnitsOnHand(BakeryBreadCatalog.FuelItemId);
            report.FuelIntakeRemaining = intake.TotalFuelWoodUnitsFor("boniecki-fuel-dealer-1");
            report.Diagnostics.AddRange(diag);
            report.Diagnostics.AddRange(service.Diagnostics);
            return report;
        }
    }

}
