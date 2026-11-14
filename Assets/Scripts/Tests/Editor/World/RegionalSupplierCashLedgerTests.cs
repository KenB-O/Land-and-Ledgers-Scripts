// A1 plan 34 (R39): regional merchants' authored cash becomes a live
// ledger. Each regional supplier's adapter holds a live cash balance,
// seeded once from its authored OpeningCashCents, credited exactly when
// the store pays it (daily chain receipt, weekly planner receipt), and
// saved with the rest of the regional supply state. No sell side is built
// here (R8): no debit method, no merchant buying, no buy prices.
//
// Fixture precedents: StoreRestockServiceTests (chain + fake paid port),
// StoreWeeklyReorderCustodyTests (weekly planner receipt with a real
// adapter), RegionalSupplySaveTests (DTO round trip),
// RegionalBusinessRegistryTests (registry pins).
using System;
using System.Collections.Generic;
using LandLedgers.Economy;
using LandLedgers.Economy.Businesses.GeneralStore;
using LandLedgers.Population;
using LandLedgers.World.Regional;
using NUnit.Framework;

namespace LandLedgers.EditorTests.World
{
    [TestFixture]
    public sealed class RegionalSupplierCashLedgerTests
    {
        // Fake store port: copied from StoreRestockServiceTests (same
        // contract, same cash/stock behaviour), so the daily chain runs
        // against real regional adapters here.
        private class FakeStorePort : IGeneralStoreTradingPort, IPaidStoreReceivingPort
        {
            public string StoreBusinessId { get; set; } = "store-1";
            public string StoreName => "General Store";
            public string LocationId => "store";
            public Dictionary<string, int> Stock = new Dictionary<string, int>();
            public Dictionary<string, int> Prices = new Dictionary<string, int>();
            public Dictionary<string, int> Targets = new Dictionary<string, int>();
            public List<GeneralStoreCategoryOffer> OfferList = new List<GeneralStoreCategoryOffer>();
            public StoreTradingCapability Capability { get; set; } = new StoreTradingCapability();
            public int CashBalance = 50000;

            public IReadOnlyList<GeneralStoreCategoryOffer> CategoryOffers => OfferList;
            public StoreTradingCapability TradingCapability => Capability;
            public int CurrentCashCents => CashBalance;
            public bool OffersDelivery => false;
            public bool OffersTradeCredit => false;
            public int TradeCreditLimitCents => 0;

            public int CategoryStockUnits(string categoryId) => Stock.TryGetValue(categoryId, out int s) ? s : 0;
            public int CategoryTargetStockUnits(string categoryId) => Targets.TryGetValue(categoryId, out int t) ? t : 0;
            public int CategoryPricePerUnitCents(string categoryId) => Prices.TryGetValue(categoryId, out int p) ? p : 0;

            public bool TryConsumeStoreUnits(string categoryId, int units, out int consumedUnits)
            {
                consumedUnits = 0;
                int have = CategoryStockUnits(categoryId);
                int take = System.Math.Min(System.Math.Max(0, units), have);
                if (take <= 0) return false;
                Stock[categoryId] = have - take;
                consumedUnits = take;
                return true;
            }

            public void RecordCashSettlement(string categoryId, int unitsSold, int revenueCents) { }
            public void RecordCreditSettlement(string categoryId, int unitsSold, int amountCents, string obligationId) { }

            public string SpendCash(int amountCents, string purpose)
            {
                int spend = System.Math.Max(0, amountCents);
                if (spend <= 0) return null;
                if (CashBalance < spend) return $"cannot afford {spend}c: cash is {CashBalance}c";
                CashBalance -= spend;
                return null;
            }

            public string ReceivePaidStock(string lotId, string itemId, string categoryId, string unitLabel,
                int units, int costCents, string provenance, int dayIndex)
            {
                if (CurrentCashCents < costCents) return "cannot afford";
                string problem = !string.IsNullOrWhiteSpace(itemId) && this is IIdentifiedRetailStockReceivingPort identified
                    ? identified.ReceiveRetailItemLot(lotId, itemId, categoryId, unitLabel, units, provenance, dayIndex)
                    : ReceiveStock(categoryId, units, provenance);
                return problem ?? SpendCash(costCents, provenance);
            }
            public string ReceiveStock(string categoryId, int units, string provenanceLabel)
            {
                if (units <= 0) return "nothing to receive";
                Stock[categoryId] = CategoryStockUnits(categoryId) + units;
                return null;
            }

            public int CategoryReceivableUnits(string categoryId)
            {
                int target = CategoryTargetStockUnits(categoryId);
                return target > 0 ? System.Math.Max(0, target - CategoryStockUnits(categoryId)) : int.MaxValue;
            }

            public int DeliveryFeeCents(string itemId, int units) => 0;
        }

        // Item-level store port: copied from StoreRestockServiceTests.
        private sealed class FakeIdentifiedSkuStore : FakeStorePort, IItemLevelRestockTradingPort, IIdentifiedRetailStockReceivingPort
        {
            public readonly Dictionary<string, int> ItemStock = new Dictionary<string, int>();
            public readonly Dictionary<string, int> ItemTargets = new Dictionary<string, int>();
            public readonly Dictionary<string, int> ItemLots = new Dictionary<string, int>();
            public readonly Dictionary<string, string> ItemUnits = new Dictionary<string, string>();
            public readonly List<string> ReceivedLotIds = new List<string>();

            public bool UsesItemLevelInventory(string itemId) => true;
            public int ItemStockUnits(string itemId) => ItemStock.TryGetValue(itemId, out int units) ? units : 0;
            public int ItemPricePerUnitCents(string itemId) => Prices.TryGetValue(itemId, out int price) ? price : 0;
            public int ItemRestockStockUnits(string itemId) => ItemStockUnits(itemId);
            public int ItemRetailPricePerStoreUnitCents(string itemId) => ItemPricePerUnitCents(itemId);
            public int ItemReorderTargetUnits(string itemId) => ItemTargets.TryGetValue(itemId, out int target) ? target : 0;
            public int ItemReorderLotSize(string itemId) => ItemLots.TryGetValue(itemId, out int lot) ? lot : 1;
            public string ItemUnitLabel(string itemId) => ItemUnits.TryGetValue(itemId, out string unit) ? unit : string.Empty;
            public bool TryConsumeItemStockUnits(string itemId, int units, out int consumedUnits)
            {
                consumedUnits = 0;
                int have = ItemStockUnits(itemId);
                if (units <= 0 || have < units) return false;
                ItemStock[itemId] = have - units;
                Stock["staple_food"] = CategoryStockUnits("staple_food") - units;
                consumedUnits = units;
                return true;
            }
            public void RecordItemCashSettlement(string categoryId, int unitsSold, int revenueCents) { }
            public string ReceiveRetailItemLot(string lotId, string itemId, string categoryId, string unitLabel,
                int units, string provenance, int dayIndex)
            {
                if (ReceivedLotIds.Contains(lotId)) return "duplicate lot";
                ReceivedLotIds.Add(lotId);
                ItemStock[itemId] = ItemStockUnits(itemId) + units;
                Stock[categoryId] = CategoryStockUnits(categoryId) + units;
                return null;
            }
        }

        // Plain upstream supplier with NO ledger (T11): a settlement
        // against it must complete uncredited, never by crashing.
        private sealed class FakeUpstreamSupplier : IUpstreamGoodsSupplier
        {
            public string SupplierId { get; set; } = "up-1";
            public string SupplierName { get; set; } = "Wholesaler";
            public Dictionary<string, int> Stock = new Dictionary<string, int>();
            public Dictionary<string, int> Quotes = new Dictionary<string, int>();
            public int LeadDays = 1;

            public int QuoteUnitCostCents(string categoryId) => Quotes.TryGetValue(categoryId, out int q) ? q : 0;
            public int LeadTimeDays(string categoryId) => LeadDays;
            public int AvailableUnits(string categoryId) => Stock.TryGetValue(categoryId, out int s) ? s : 0;

            public int Ship(string categoryId, int units, int dayIndex, List<string> diagnostics)
            {
                int have = AvailableUnits(categoryId);
                int shipped = System.Math.Min(System.Math.Max(0, units), have);
                if (shipped > 0) Stock[categoryId] = have - shipped;
                return shipped;
            }
        }

        // Ledger supplier that OVER-SHIPS (T10 only): it returns more than
        // the planner asked for, so the planner's acceptance cap — not the
        // ship quantity — decides what is accepted. The real adapter never
        // over-ships (Ship clamps to the requested units); this fake exists
        // to exercise the planner's accepted-less-than-shipped refund path,
        // which no conforming supplier can reach.
        private sealed class FakeOverShippingLedgerSupplier : IUpstreamGoodsSupplier, ISupplierCashLedger
        {
            public string SupplierId { get; set; } = "over-ship-1";
            public string SupplierName { get; set; } = "Over-Shipper";
            public Dictionary<string, int> Stock = new Dictionary<string, int>();
            public Dictionary<string, int> Quotes = new Dictionary<string, int>();
            public int CashBalance;

            public int CashCents => CashBalance;
            public void CreditCashCents(int amountCents)
            {
                if (amountCents <= 0) return;
                CashBalance += amountCents;
            }

            public int QuoteUnitCostCents(string categoryId) => Quotes.TryGetValue(categoryId, out int q) ? q : 0;
            public int LeadTimeDays(string categoryId) => 1;
            public int AvailableUnits(string categoryId) => Stock.TryGetValue(categoryId, out int s) ? s : 0;

            public int Ship(string categoryId, int units, int dayIndex, List<string> diagnostics)
            {
                int have = AvailableUnits(categoryId);
                int shipped = System.Math.Min(System.Math.Max(0, units) + 10, have);
                if (shipped > 0) Stock[categoryId] = have - shipped;
                return shipped;
            }
        }

        private static FakeStorePort StoreWith(string categoryId, int stock, int target, int price)
        {
            var port = new FakeStorePort();
            port.OfferList.Add(new GeneralStoreCategoryOffer
            {
                CategoryId = categoryId,
                OfferedItemIds = new List<string>(),
                StoreUnitsPerItemUnit = 1,
            });
            port.Stock[categoryId] = stock;
            port.Targets[categoryId] = target;
            port.Prices[categoryId] = price;
            return port;
        }

        private static RegionalSupplierAdapter FindAdapter(List<RegionalSupplierAdapter> adapters, string supplierId)
        {
            for (int i = 0; i < adapters.Count; i++)
                if (string.Equals(adapters[i].SupplierId, supplierId, StringComparison.Ordinal))
                    return adapters[i];
            return null;
        }

        // T0 (runtime red): the weekly receipt status line states the
        // seller revenue is credited to the named supplier's ledger. Base
        // members only (planner + real adapter + store state); fails at
        // base on the "off-map" text, passes after.
        [Test]
        public void T0_WeeklyReceiptStatusLine_StatesLedgerCredit()
        {
            var adapter = new RegionalSupplierAdapter(RegionalBusinessRegistry.AshfordMercantile);
            var state = new BusinessRuntimeState();
            state.EnsureCategoryStock("staple_food", 50, 100);
            state.SetCurrentCashCents(10000);

            StoreWeeklyReorderPlanner.NamedSupplierReceiptResult receipt =
                StoreWeeklyReorderPlanner.ReceiveNamedSupplierShipment(
                    adapter, state, "staple_food", 20, 25, 3, new List<string>());

            Assert.AreEqual(20, receipt.AcceptedUnits);
            StringAssert.Contains("credited", receipt.StatusLine, receipt.StatusLine);
            StringAssert.Contains("ledger", receipt.StatusLine, receipt.StatusLine);
            StringAssert.Contains("Ashford General Mercantile", receipt.StatusLine, receipt.StatusLine);
        }

        // T1 (seed, exact): every fresh adapter's cash equals its authored
        // OpeningCashCents.
        [Test]
        public void T1_FreshAdapterCash_EqualsAuthoredOpeningCash()
        {
            for (int i = 0; i < RegionalBusinessRegistry.All.Length; i++)
            {
                RegionalBusinessRegistry.RegionalBusinessDefinition definition = RegionalBusinessRegistry.All[i];
                var adapter = new RegionalSupplierAdapter(definition);
                Assert.AreEqual(definition.OpeningCashCents, adapter.CashCents,
                    $"Fresh adapter for '{definition.BusinessId}' must seed from its authored OpeningCashCents.");
            }
        }

        // T2 (seed total): the conservation anchor for the future sell side.
        [Test]
        public void T2_AllAdaptersCash_SumsToAuthoredTotal()
        {
            List<RegionalSupplierAdapter> adapters = RegionalSupplierSet.CreateAll();
            Assert.AreEqual(12, adapters.Count);
            int total = 0;
            for (int i = 0; i < adapters.Count; i++)
                total += adapters[i].CashCents;
            Assert.AreEqual(184000, total, "12 authored OpeningCashCents values sum to 184,000c.");
        }

        // T3 (credit): accumulation, and zero/negative refused.
        [Test]
        public void T3_CreditCashCents_AccumulatesAndRefusesNonPositive()
        {
            var adapter = new RegionalSupplierAdapter(RegionalBusinessRegistry.MillbrookGrainCo);
            Assert.AreEqual(20000, adapter.CashCents);
            adapter.CreditCashCents(500);
            Assert.AreEqual(20500, adapter.CashCents);
            adapter.CreditCashCents(0);
            Assert.AreEqual(20500, adapter.CashCents, "A zero credit must be a no-op.");
            adapter.CreditCashCents(-100);
            Assert.AreEqual(20500, adapter.CashCents, "A negative credit must be refused.");
            adapter.CreditCashCents(1500);
            Assert.AreEqual(22000, adapter.CashCents);
        }

        // T4 (DTO round trip): credited cash survives capture -> fresh
        // adapters -> restore, per supplier; stock rows unaffected.
        [Test]
        public void T4_DtoRoundTrip_PreservesCashPerSupplier()
        {
            List<RegionalSupplierAdapter> adapters = RegionalSupplierSet.CreateAll();
            RegionalSupplierAdapter millbrook = FindAdapter(adapters, "millbrook_grain_co");
            RegionalSupplierAdapter mercantile = FindAdapter(adapters, "ashford_general_mercantile");
            Assert.NotNull(millbrook);
            Assert.NotNull(mercantile);
            millbrook.CreditCashCents(1000);
            mercantile.CreditCashCents(250);

            var dto = new RegionalSupplySaveDto();
            for (int i = 0; i < adapters.Count; i++)
                adapters[i].CaptureInto(dto);
            Assert.AreEqual(12, dto.SupplierCash.Count, "One cash row per adapter.");

            List<RegionalSupplierAdapter> restored = RegionalSupplierSet.CreateAll();
            var diagnostics = new List<string>();
            for (int i = 0; i < restored.Count; i++)
                restored[i].RestoreFrom(dto, diagnostics);
            Assert.AreEqual(0, diagnostics.Count);

            for (int i = 0; i < adapters.Count; i++)
            {
                RegionalSupplierAdapter fresh = FindAdapter(restored, adapters[i].SupplierId);
                Assert.NotNull(fresh);
                Assert.AreEqual(adapters[i].CashCents, fresh.CashCents,
                    $"Cash for '{adapters[i].SupplierId}' must survive the round trip.");
            }
            Assert.AreEqual(21000, FindAdapter(restored, "millbrook_grain_co").CashCents);
            Assert.AreEqual(20250, FindAdapter(restored, "ashford_general_mercantile").CashCents);
            Assert.AreEqual(96, FindAdapter(restored, "millbrook_grain_co").OnHandUnits("flour"),
                "Stock rows must survive alongside the cash rows.");

            // Idempotent: restoring the same DTO again reproduces the state.
            for (int i = 0; i < restored.Count; i++)
                restored[i].RestoreFrom(dto, diagnostics);
            Assert.AreEqual(21000, FindAdapter(restored, "millbrook_grain_co").CashCents);
            Assert.AreEqual(20250, FindAdapter(restored, "ashford_general_mercantile").CashCents);
        }

        // T5 (old save): a DTO with no cash rows restores every adapter to
        // its OpeningCashCents (the §3.1 migration rule). Same for a null
        // cash list and a null DTO: reset first, then return.
        [Test]
        public void T5_OldSaveWithoutCashRows_RestoresOpeningCash()
        {
            List<RegionalSupplierAdapter> adapters = RegionalSupplierSet.CreateAll();
            RegionalSupplierAdapter millbrook = FindAdapter(adapters, "millbrook_grain_co");
            millbrook.CreditCashCents(5000);
            Assert.AreEqual(25000, millbrook.CashCents);

            var emptyDto = new RegionalSupplySaveDto();
            var diagnostics = new List<string>();
            for (int i = 0; i < adapters.Count; i++)
                adapters[i].RestoreFrom(emptyDto, diagnostics);
            Assert.AreEqual(0, diagnostics.Count);
            for (int i = 0; i < adapters.Count; i++)
                Assert.AreEqual(RegionalBusinessRegistry.All[i].OpeningCashCents, adapters[i].CashCents,
                    $"An old save with no cash rows must restore '{adapters[i].SupplierId}' to opening cash.");

            millbrook.CreditCashCents(5000);
            var nullListDto = new RegionalSupplySaveDto { SupplierCash = null };
            millbrook.RestoreFrom(nullListDto, diagnostics);
            Assert.AreEqual(20000, millbrook.CashCents, "A null cash list must restore opening cash.");

            millbrook.CreditCashCents(5000);
            millbrook.RestoreFrom(null, diagnostics);
            Assert.AreEqual(20000, millbrook.CashCents, "A null DTO must restore opening cash.");
        }

        // T6 (DTO validation, note 134 C2): an unknown-supplier cash row is
        // silently skipped (no diagnostic — no code sees the whole DTO
        // against the supplier set); a negative cash row for a KNOWN
        // supplier is clamped to 0 with a diagnostic.
        [Test]
        public void T6_DtoValidation_UnknownRowSkippedSilently_NegativeRowClampedWithDiagnostic()
        {
            var unknownDto = new RegionalSupplySaveDto();
            unknownDto.SupplierCash.Add(new RegionalSupplierCashRow
            {
                SupplierId = "no-such-supplier",
                CashCents = 99999,
            });
            List<RegionalSupplierAdapter> adapters = RegionalSupplierSet.CreateAll();
            var diagnostics = new List<string>();
            for (int i = 0; i < adapters.Count; i++)
                adapters[i].RestoreFrom(unknownDto, diagnostics);
            Assert.AreEqual(0, diagnostics.Count, "An unknown-supplier cash row is skipped silently.");
            for (int i = 0; i < adapters.Count; i++)
                Assert.AreEqual(RegionalBusinessRegistry.All[i].OpeningCashCents, adapters[i].CashCents);

            var negativeDto = new RegionalSupplySaveDto();
            negativeDto.SupplierCash.Add(new RegionalSupplierCashRow
            {
                SupplierId = "millbrook_grain_co",
                CashCents = -500,
            });
            List<RegionalSupplierAdapter> fresh = RegionalSupplierSet.CreateAll();
            var negativeDiagnostics = new List<string>();
            for (int i = 0; i < fresh.Count; i++)
                fresh[i].RestoreFrom(negativeDto, negativeDiagnostics);
            Assert.AreEqual(0, FindAdapter(fresh, "millbrook_grain_co").CashCents,
                "A negative cash row for a known supplier is clamped to 0.");
            bool sawNegativeDiagnostic = false;
            for (int i = 0; i < negativeDiagnostics.Count; i++)
                if (negativeDiagnostics[i].Contains("negative")) sawNegativeDiagnostic = true;
            Assert.IsTrue(sawNegativeDiagnostic, "The negative clamp must be diagnosed.");
            Assert.AreEqual(20000, FindAdapter(fresh, "ashford_general_mercantile").CashCents,
                "A row for one supplier must not touch another supplier's cash.");
        }

        // T7 (daily credit): chain + real regional adapters + fake paid
        // port. The store pays units x the order's goods quote and the
        // winning supplier's ledger rises by exactly that amount; the
        // receipt diagnostic names the supplier credit. Includes a second
        // delivery the store cannot afford: no receipt, no credit.
        [Test]
        public void T7_DailyReceipt_CreditsSupplierLedgerExactly_AndRefusedReceiptCreditsNothing()
        {
            List<RegionalSupplierAdapter> adapters = RegionalSupplierSet.CreateAll();
            List<IUpstreamGoodsSupplier> suppliers = RegionalSupplierSet.AsSuppliers(adapters);
            var port = StoreWith("staple_food", 20, 100, 120);
            var service = new StoreRestockService();

            service.EvaluateRestock(port, suppliers, 0, 0.5f);
            Assert.AreEqual(1, service.Orders.Count, "Below-threshold stock with no open order must order.");
            StoreProcurementOrder order = service.Orders[0];
            RegionalSupplierAdapter supplier = FindAdapter(adapters, order.UpstreamSupplierId);
            Assert.NotNull(supplier);
            int openingCash = supplier.CashCents;
            int units = order.UnitsOrdered;
            int unitCost = order.UnitCostCents;
            Assert.IsTrue(units > 0);
            Assert.IsTrue(unitCost > 0);

            var diagnostics = new List<string>();
            service.AdvanceDay(port, suppliers, order.ExpectedArrivalDayIndex, diagnostics);
            Assert.AreEqual(StoreProcurementStage.Received, order.Stage);
            Assert.AreEqual(100, port.CategoryStockUnits("staple_food"));
            Assert.AreEqual(50000 - units * unitCost, port.CashBalance,
                "Receipt pays real cash before stock books.");
            Assert.AreEqual(openingCash + units * unitCost, supplier.CashCents,
                "The supplier's ledger must rise by exactly the goods quote the store paid.");
            bool sawCreditDiagnostic = false;
            for (int i = 0; i < diagnostics.Count; i++)
                if (diagnostics[i].Contains("supplier='" + supplier.SupplierId + "'")
                    && diagnostics[i].Contains("ledger=")) sawCreditDiagnostic = true;
            Assert.IsTrue(sawCreditDiagnostic, "The receipt diagnostic must name the supplier credit.");

            // Second delivery the store cannot afford: it ships (the §8.2b
            // wart — stock leaves at ship time) but is never received, so
            // no credit lands.
            port.Stock["staple_food"] = 20;
            service.EvaluateRestock(port, suppliers, 2, 0.5f);
            Assert.AreEqual(2, service.Orders.Count);
            StoreProcurementOrder secondOrder = service.Orders[1];
            RegionalSupplierAdapter secondSupplier = FindAdapter(adapters, secondOrder.UpstreamSupplierId);
            Assert.NotNull(secondSupplier);
            int cashBeforeRefusal = secondSupplier.CashCents;
            port.CashBalance = 10;
            var refusedDiagnostics = new List<string>();
            service.AdvanceDay(port, suppliers, secondOrder.ExpectedArrivalDayIndex, refusedDiagnostics);
            Assert.AreEqual(StoreProcurementStage.InTransit, secondOrder.Stage,
                "An unaffordable delivery stays in transit, never received.");
            Assert.AreEqual(cashBeforeRefusal, secondSupplier.CashCents,
                "A refused receipt must credit nothing.");
            Assert.AreEqual(10, port.CashBalance, "A refused receipt must move no store cash.");
            Assert.IsTrue(!service.Deliveries[1].Received, "The refused delivery is not received.");
        }

        // T8 (daily partial): a delivery received in two parts credits only
        // its received units each time; the docked remainder is uncredited
        // until received.
        [Test]
        public void T8_DailyPartialReceipt_CreditsOnlyReceivedUnitsEachTime()
        {
            List<RegionalSupplierAdapter> adapters = RegionalSupplierSet.CreateAll();
            List<IUpstreamGoodsSupplier> suppliers = RegionalSupplierSet.AsSuppliers(adapters);
            var port = StoreWith("staple_food", 20, 100, 120);
            var service = new StoreRestockService();

            service.EvaluateRestock(port, suppliers, 0, 0.5f);
            Assert.AreEqual(1, service.Orders.Count);
            StoreProcurementOrder order = service.Orders[0];
            RegionalSupplierAdapter supplier = FindAdapter(adapters, order.UpstreamSupplierId);
            Assert.NotNull(supplier);
            int openingCash = supplier.CashCents;
            int units = order.UnitsOrdered;
            int unitCost = order.UnitCostCents;
            Assert.AreEqual(80, units, "Order fills to target (100 - 20).");

            // The shelf fills from another source before the delivery
            // arrives, so nothing can be received yet: the shipment lands
            // at the dock uncredited.
            port.Targets["staple_food"] = 20;
            service.AdvanceDay(port, suppliers, order.ExpectedArrivalDayIndex, new List<string>());
            Assert.AreEqual(StoreProcurementStage.InTransit, order.Stage);
            Assert.AreEqual(units, service.Deliveries[0].Units);
            Assert.AreEqual(openingCash, supplier.CashCents, "Nothing received yet, nothing credited.");

            // Room for 40u: receive 40, credit 40 x quote.
            port.Targets["staple_food"] = 60;
            service.AdvanceDay(port, suppliers, order.ExpectedArrivalDayIndex + 1, new List<string>());
            Assert.AreEqual(StoreProcurementStage.InTransit, order.Stage, "40u still at the dock.");
            Assert.AreEqual(40, service.Deliveries[0].Units);
            Assert.AreEqual(openingCash + 40 * unitCost, supplier.CashCents,
                "The first partial receipt credits only its received units.");
            Assert.AreEqual(50000 - 40 * unitCost, port.CashBalance);

            // Room for the rest: receive the remaining 40, credit the rest.
            port.Targets["staple_food"] = 100;
            service.AdvanceDay(port, suppliers, order.ExpectedArrivalDayIndex + 2, new List<string>());
            Assert.AreEqual(StoreProcurementStage.Received, order.Stage);
            Assert.AreEqual(openingCash + units * unitCost, supplier.CashCents,
                "Both partial receipts together credit exactly the full order value.");
            Assert.AreEqual(50000 - units * unitCost, port.CashBalance);
            Assert.AreEqual(100, port.CategoryStockUnits("staple_food"));
        }

        // T9 (weekly credit): the named receipt credits accepted x the
        // pre-ship goods quote to the supplier's ledger; the store pays
        // accepted x the landed cost; the status line carries the credit.
        [Test]
        public void T9_WeeklyReceipt_CreditsAcceptedTimesGoodsQuote()
        {
            var adapter = new RegionalSupplierAdapter(RegionalBusinessRegistry.AshfordMercantile);
            int stockBefore = adapter.AvailableUnits("staple_food");
            int goodsQuote = adapter.QuoteUnitCostCents("staple_food");
            Assert.IsTrue(stockBefore >= 20);
            Assert.IsTrue(goodsQuote > 0);

            var state = new BusinessRuntimeState();
            state.EnsureCategoryStock("staple_food", 50, 100);
            state.SetCurrentCashCents(10000);

            int landedCost = 25;
            StoreWeeklyReorderPlanner.NamedSupplierReceiptResult receipt =
                StoreWeeklyReorderPlanner.ReceiveNamedSupplierShipment(
                    adapter, state, "staple_food", 20, landedCost, 3, new List<string>());

            Assert.AreEqual(20, receipt.ShippedUnits);
            Assert.AreEqual(20, receipt.AcceptedUnits);
            Assert.AreEqual(20 * landedCost, receipt.BuyerSpendCents);
            Assert.AreEqual(0, receipt.ShortfallUnits);
            Assert.AreEqual(20000 + 20 * goodsQuote, adapter.CashCents,
                "The supplier's ledger must rise by accepted x the pre-ship goods quote.");
            Assert.AreEqual(stockBefore - 20, adapter.AvailableUnits("staple_food"),
                "Supplier stock decremented exactly once by the ordered units.");
            Assert.AreEqual(70, state.GetCategoryStock("staple_food").CurrentStockUnits);
            Assert.AreEqual(10000 - 20 * landedCost, state.CurrentCashCents,
                "Buyer cash debited exactly once.");
            StringAssert.Contains("credited", receipt.StatusLine, receipt.StatusLine);
        }

        // T10 (weekly partial): accepted < shipped credits accepted only
        // (the refund path). Reached here through an over-shipping fake:
        // the real adapter never ships more than asked, so only a supplier
        // that ships past the planner's cap can make accepted fall short
        // of shipped; the planner's acceptance cap is what decides.
        [Test]
        public void T10_WeeklyPartialReceipt_CreditsAcceptedOnly()
        {
            var supplier = new FakeOverShippingLedgerSupplier();
            supplier.Stock["staple_food"] = 100;
            supplier.Quotes["staple_food"] = 50;
            var state = new BusinessRuntimeState();
            state.EnsureCategoryStock("staple_food", 90, 100);
            state.SetCurrentCashCents(10000);

            // Receivable is 10u, so the planner asks for 10u; the fake
            // ships 20u. Acceptance caps at 10u and the other 10u are
            // refunded, never credited.
            StoreWeeklyReorderPlanner.NamedSupplierReceiptResult receipt =
                StoreWeeklyReorderPlanner.ReceiveNamedSupplierShipment(
                    supplier, state, "staple_food", 20, 25, 3, new List<string>());

            Assert.AreEqual(20, receipt.ShippedUnits);
            Assert.AreEqual(10, receipt.AcceptedUnits);
            Assert.AreEqual(10 * 25, receipt.BuyerSpendCents, "The unaccepted 10u are refunded.");
            Assert.AreEqual(10 * 50, supplier.CashCents,
                "Only the accepted units are credited, at the goods quote.");
            Assert.AreEqual(100, state.GetCategoryStock("staple_food").CurrentStockUnits);
            Assert.AreEqual(10000 - 10 * 25, state.CurrentCashCents);
            StringAssert.Contains("credited", receipt.StatusLine, receipt.StatusLine);
        }

        // T11 (no ledger): a supplier without the interface completes a
        // daily receipt and a weekly receipt with no credit and no crash.
        [Test]
        public void T11_SupplierWithoutLedger_CompletesBothReceiptsUncredited()
        {
            // Daily leg.
            var port = StoreWith("staple_food", 20, 100, 120);
            var fake = new FakeUpstreamSupplier();
            fake.Stock["staple_food"] = 500;
            fake.Quotes["staple_food"] = 80;
            var service = new StoreRestockService();
            service.EvaluateRestock(port, new List<IUpstreamGoodsSupplier> { fake }, 0, 0.5f);
            Assert.AreEqual(1, service.Orders.Count);
            StoreProcurementOrder order = service.Orders[0];
            service.AdvanceDay(port, new List<IUpstreamGoodsSupplier> { fake }, order.ExpectedArrivalDayIndex, new List<string>());
            Assert.AreEqual(StoreProcurementStage.Received, order.Stage, "A ledger-less supplier must still settle daily.");
            Assert.AreEqual(100, port.CategoryStockUnits("staple_food"));
            Assert.AreEqual(50000 - 80 * 80, port.CashBalance);

            // Weekly leg.
            var state = new BusinessRuntimeState();
            state.EnsureCategoryStock("staple_food", 50, 100);
            state.SetCurrentCashCents(10000);
            StoreWeeklyReorderPlanner.NamedSupplierReceiptResult receipt =
                StoreWeeklyReorderPlanner.ReceiveNamedSupplierShipment(
                    fake, state, "staple_food", 20, 25, 3, new List<string>());
            Assert.AreEqual(20, receipt.AcceptedUnits, "A ledger-less supplier must still settle weekly.");
            Assert.AreEqual(10000 - 20 * 25, state.CurrentCashCents);
        }

        // T12 (daily item-level): an item-level order credits the item
        // quote — Redfield Grain & Feed's flour at 18c beats Millbrook's
        // 20c, so the chain orders flour from Redfield and the credit
        // lands there at the item quote.
        [Test]
        public void T12_DailyItemLevelOrder_CreditsItemQuote()
        {
            List<RegionalSupplierAdapter> adapters = RegionalSupplierSet.CreateAll();
            List<IUpstreamGoodsSupplier> suppliers = RegionalSupplierSet.AsSuppliers(adapters);
            var port = new FakeIdentifiedSkuStore();
            port.OfferList.Add(new GeneralStoreCategoryOffer
            {
                CategoryId = "staple_food",
                OfferedItemIds = new List<string> { "flour", "sugar" },
                UsesItemLevelInventory = true,
            });
            port.Stock["staple_food"] = 120;
            port.Targets["staple_food"] = 200;
            port.Prices["flour"] = 50;
            port.ItemStock["flour"] = 0;
            port.ItemTargets["flour"] = 30;
            port.ItemLots["flour"] = 6;
            port.ItemUnits["flour"] = "10 lb sack";
            var service = new StoreRestockService();

            service.EvaluateRestock(port, suppliers, 0, 0.5f);
            Assert.AreEqual(1, service.Orders.Count, "The flour SKU needs a reorder even though the category is healthy.");
            StoreProcurementOrder order = service.Orders[0];
            Assert.AreEqual("flour", order.ItemId);
            Assert.AreEqual(30, order.UnitsOrdered);
            Assert.AreEqual("redfield_grain_feed", order.UpstreamSupplierId,
                "Redfield's flour at 18c beats Millbrook's 20c.");
            Assert.AreEqual(18, order.UnitCostCents, "The order records the item quote, not the delivered cost.");

            RegionalSupplierAdapter supplier = FindAdapter(adapters, order.UpstreamSupplierId);
            Assert.NotNull(supplier);
            service.AdvanceDay(port, suppliers, order.ExpectedArrivalDayIndex, new List<string>());
            Assert.AreEqual(StoreProcurementStage.Received, order.Stage);
            Assert.AreEqual(30, port.ItemStockUnits("flour"));
            Assert.AreEqual(20000 + 30 * 18, supplier.CashCents,
                "The item-level credit lands at the item quote.");
            Assert.AreEqual(50000 - 30 * 18, port.CashBalance);
            Assert.AreEqual(20000, FindAdapter(adapters, "millbrook_grain_co").CashCents,
                "The losing supplier's ledger is untouched.");
        }
    }
}
