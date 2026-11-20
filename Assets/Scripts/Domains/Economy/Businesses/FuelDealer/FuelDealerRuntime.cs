// Campaign B-18 (DEF-003, owner Decision 3, Nov 15, 2026): the Fuel
// Dealer as a real per-instance business runtime.
//
// Before this package there was no per-instance Fuel Dealer business
// package at all: the Pass 7 adapter is only a selling surface over
// intake lots that already exist, and the Pass 8 Series2FuelDealer
// only moves custody. This class is the missing business: it owns the
// dealer's purchasing decisions, supplier relationships, order book,
// and cash book over the shared provenance-tracked intake
// (FuelYardByproductIntake), which stays the one stock authority —
// this runtime never holds a second copy of the stock, never
// synthesizes a unit, and never invents a supplier.
//
// Decision 3: implement the missing functional business (inventory,
// purchasing, sales, cash accounting, custody, supplier relationships,
// save/load). Source fuel from actual local sawmill output where
// available, with legitimate off-map procurement as a fallback. Use
// the Decision 1 conversions (1 dealer intake unit is approximately
// 1 kg dry-wood equivalent; 1 bakery fuel unit = 4 intake units;
// 1 household retail bundle = 10 intake units; sawmill-to-dealer
// byproduct purchase price 1 cent per intake unit before transport
// and processing). Seasonal demand, housing conditions and available
// stock govern household fuel consumption and restocking. Do not
// introduce an imaginary Timberline business without first authoring
// a legitimate supplier definition and supply path.
//
// What this runtime does and does not do:
//   - Inventory: reads live stock from the intake, never a copy.
//   - Purchasing (local): a daily demand assessment (AssessLocalPurchase)
//     decides whether the dealer wants the mill's held byproduct at all
//     (Buy / DeclineFull / NoSupply / Sufficient / OffMapAvailable).
//     The daily custody transfer itself stays in SimulationDrivers; this
//     runtime records the settled purchase (RecordLocalPurchase) from the
//     driver's actual numbers, never from an estimate.
//   - Purchasing (off-map): the mechanism only — RegisterOffMapSupplier
//     records an authored supplier (id, input SKU, price, conversion
//     rate); PlaceOffMapOrder / ReceiveOffMapOrder / CancelOffMapOrder
//     run a real order through receipt into real intake lots with
//     off-map provenance. With no registered supplier every off-map
//     request refuses loudly: no vendor is ever conjured, and in
//     particular there is no Timberline (zero hits in documentation
//     v3.2). The authoring of an actual off-map supplier is scenario /
//     content work for the manager, not this package.
//   - Sales: retail sales go through the Pass 7 adapter and bakery sales
//     through the paid dealer source; both report into this runtime's
//     cash book through the OnRetailSaleRecorded / OnBakerySaleRecorded
//     hooks. This runtime never sells by itself and never duplicates a
//     sale path.
//   - Cash: money moves ONLY through the caller-supplied
//     IBusinessCashPort (the same BusinessRuntimeState port the adapter
//     and the settlement use). This runtime is never a second cash
//     writer: the cash book is an append-only RECORD of transactions
//     settled elsewhere (or by this runtime's own off-map receipt debit,
//     which goes through the same port), never a balance of its own.
//   - Custody: lots carry full provenance (mill lots) or off-map
//     provenance (supplier, order, input lot); a lot id can never appear
//     in two yards (the intake enforces it).
//   - Save/load: CaptureSaveDto / LoadFromSaveDto persist suppliers,
//     orders, the cash book, policy values, and the last procurement
//     note. Clear-then-load, idempotent; a DTO for another dealer is
//     refused (identity guard, like the sawmill's). The intake lots
//     themselves are saved by the intake's own DTO, never duplicated
//     here.
//
// Policy values (target stock, reorder point, cold-season multiplier)
// are provisional calibration under the Decision 1 delegation
// principle ("starting values are calibration, not permanent universal
// rules"), documented here and adjustable through the settable
// properties / the save DTO. Plain C#, no Unity dependency,
// Roslyn-testable.
using System;
using System.Collections.Generic;
using LandLedgers.Economy;
using LandLedgers.Economy.Businesses.GeneralStore;
using LandLedgers.Economy.Businesses.Sawmill;
using LandLedgers.Primitives;

namespace LandLedgers.Economy.Businesses.FuelDealer
{
    /// <summary>Retail-sale record hook: the Pass 7 adapter reports each settled retail sale here.</summary>
    public delegate void FuelDealerRetailSaleRecord(
        int dayIndex, int bundlesSold, int intakeUnits, int revenueCents,
        string receiptId, bool cashCredited, bool onTradeCredit);

    /// <summary>Bakery-sale record hook: the paid dealer source reports each settled bakery sale here.</summary>
    public delegate void FuelDealerBakerySaleRecord(
        int dayIndex, int bakeryUnits, int intakeUnits, int revenueCents,
        string bakeryBusinessId, string bakeryLotId);

    /// <summary>Per-instance Fuel Dealer business runtime (B-18, Decision 3).</summary>
    public sealed class FuelDealerRuntime
    {
        /// <summary>Provisional target yard stock, intake units (20 household bundles / 50 bakery firings).</summary>
        public const int DefaultTargetStockIntakeUnits = 200;

        /// <summary>Provisional reorder point, intake units (6 household bundles / 15 bakery firings).</summary>
        public const int DefaultReorderPointIntakeUnits = 60;

        /// <summary>Provisional cold-season target multiplier (Nov-Mar, matching SimulationDrivers.IsColdSeasonMonth).</summary>
        public const double DefaultColdSeasonTargetMultiplier = 1.5;

        public enum FuelDealerSupplierKind
        {
            LocalSawmill = 0,
            OffMap = 1,
        }

        public enum FuelDealerSupplierStatus
        {
            Active = 0,
            Unavailable = 1,
        }

        public enum FuelDealerOrderStatus
        {
            Placed = 0,
            Received = 1,
            Cancelled = 2,
        }

        public enum FuelDealerCashBookKind
        {
            LocalPurchase = 0,
            OffMapPurchase = 1,
            RetailSale = 2,
            BakerySale = 3,
        }

        public enum FuelDealerProcurementDecision
        {
            /// <summary>Below target and local byproduct is held: buy (take the held lots).</summary>
            Buy = 0,
            /// <summary>At or above target and local byproduct is held: decline; the mill keeps its slabs.</summary>
            DeclineFull = 1,
            /// <summary>Below reorder, no local output, and no authored off-map supplier: cannot restock.</summary>
            NoSupply = 2,
            /// <summary>At or above reorder and no local output: nothing to do, no alarm.</summary>
            Sufficient = 3,
            /// <summary>Below reorder, no local output, but an authored off-map supplier exists: order from it.</summary>
            OffMapAvailable = 4,
        }

        /// <summary>One supplier relationship: the local sawmill, or an authored off-map supplier.</summary>
        [Serializable]
        public sealed class FuelDealerSupplierRelationship
        {
            public string SupplierId = string.Empty;
            public FuelDealerSupplierKind Kind = FuelDealerSupplierKind.OffMap;
            public string InputSkuId = string.Empty;
            public int PriceCentsPerInputUnit;
            public double ConversionRateFuelPerInputUnit;
            public string Terms = string.Empty;
            public FuelDealerSupplierStatus Status = FuelDealerSupplierStatus.Active;
            public int FirstSeenDayIndex = -1;
            public int LastDeliveryDayIndex = -1;
            public int TotalFuelUnitsReceived;
            public int TotalCentsPaid;
        }

        /// <summary>One dealer procurement order: placed, then received or cancelled. Never deleted.</summary>
        [Serializable]
        public sealed class FuelDealerOrder
        {
            public string OrderId = string.Empty;
            public string SupplierId = string.Empty;
            public string InputSkuId = string.Empty;
            public int InputUnits;
            public int PriceCentsPerInputUnit;
            public double ConversionRateFuelPerInputUnit;
            public FuelDealerOrderStatus Status = FuelDealerOrderStatus.Placed;
            public int PlacedDayIndex = -1;
            public int ClosedDayIndex = -1;
            public string InputLotId = string.Empty;
            public int FuelUnitsReceived;
        }

        /// <summary>
        /// One cash-book entry. AmountCents is signed from the dealer's
        /// perspective: purchases negative (cash out), sales positive (cash
        /// in). An entry records what actually settled; a sale whose cash
        /// never arrived is recorded at 0 with the refusal in the note.
        /// The book is a record only — the balance lives in the cash port.
        /// </summary>
        [Serializable]
        public sealed class FuelDealerCashBookEntry
        {
            public int DayIndex = -1;
            public FuelDealerCashBookKind Kind = FuelDealerCashBookKind.LocalPurchase;
            public string CounterpartyId = string.Empty;
            public int IntakeUnits;
            public int AmountCents;
            public string Note = string.Empty;
        }

        /// <summary>A procurement assessment: what the dealer wants today, and why.</summary>
        public sealed class FuelDealerProcurementAssessment
        {
            public FuelDealerProcurementDecision Decision = FuelDealerProcurementDecision.Sufficient;
            public int WantIntakeUnits;
            public string Reason = string.Empty;
        }

        /// <summary>
        /// Save contract: suppliers, orders, cash book, policy values, and
        /// the last procurement note. The intake lots are NOT duplicated
        /// here — they ride in the intake's own DTO.
        /// </summary>
        [Serializable]
        public sealed class FuelDealerRuntimeSaveDto
        {
            public string DealerBusinessId = string.Empty;
            public int TargetStockIntakeUnits;
            public int ReorderPointIntakeUnits;
            public double ColdSeasonTargetMultiplier;
            public int ConfiguredBundlePriceCents;
            public List<FuelDealerSupplierRelationship> Suppliers = new List<FuelDealerSupplierRelationship>();
            public List<FuelDealerOrder> Orders = new List<FuelDealerOrder>();
            public List<FuelDealerCashBookEntry> CashBook = new List<FuelDealerCashBookEntry>();
            public string LastProcurementNote = string.Empty;
            public int LastProcurementDayIndex = -1;
            public int OrderSequence;
        }

        private readonly FuelYardByproductIntake intake;
        private readonly IBusinessCashPort cash;
        private readonly List<FuelDealerSupplierRelationship> suppliers = new List<FuelDealerSupplierRelationship>();
        private readonly List<FuelDealerOrder> orders = new List<FuelDealerOrder>();
        private readonly List<FuelDealerCashBookEntry> cashBook = new List<FuelDealerCashBookEntry>();
        private int orderSequence;

        /// <summary>
        /// Creates the runtime for one formed Fuel Dealer business.
        /// <paramref name="cash"/> may be null (a dealer without a cash
        /// authority); off-map receipt then refuses, and purchase recording
        /// still works because the driver settles local purchases itself.
        /// </summary>
        public FuelDealerRuntime(string dealerBusinessId, FuelYardByproductIntake intake, IBusinessCashPort cash)
        {
            if (string.IsNullOrWhiteSpace(dealerBusinessId))
                throw new ArgumentException("A Fuel Dealer runtime needs a dealer business id.", nameof(dealerBusinessId));
            this.intake = intake ?? throw new ArgumentNullException(nameof(intake));
            this.cash = cash;
            DealerBusinessId = dealerBusinessId;
            TargetStockIntakeUnits = DefaultTargetStockIntakeUnits;
            ReorderPointIntakeUnits = DefaultReorderPointIntakeUnits;
            ColdSeasonTargetMultiplier = DefaultColdSeasonTargetMultiplier;
        }

        public string DealerBusinessId { get; }

        /// <summary>Live yard stock, intake units, read from the intake (the one stock authority).</summary>
        public int StockIntakeUnits => intake.TotalFuelWoodUnitsFor(DealerBusinessId);

        /// <summary>Provisional target yard stock (calibration, not canon).</summary>
        public int TargetStockIntakeUnits { get; set; }

        /// <summary>Provisional reorder point (calibration, not canon).</summary>
        public int ReorderPointIntakeUnits { get; set; }

        /// <summary>Provisional cold-season target multiplier (calibration, not canon).</summary>
        public double ColdSeasonTargetMultiplier { get; set; }

        /// <summary>Configured retail bundle price in cents (from the business profile, via the driver). 0 = unset.</summary>
        public int ConfiguredBundlePriceCents { get; private set; }

        public IReadOnlyList<FuelDealerSupplierRelationship> Suppliers => suppliers;
        public IReadOnlyList<FuelDealerOrder> Orders => orders;
        public IReadOnlyList<FuelDealerCashBookEntry> CashBook => cashBook;

        /// <summary>Orders placed and not yet received or cancelled.</summary>
        public IReadOnlyList<FuelDealerOrder> OutstandingOrders
        {
            get
            {
                var outstanding = new List<FuelDealerOrder>();
                foreach (FuelDealerOrder order in orders)
                    if (order != null && order.Status == FuelDealerOrderStatus.Placed)
                        outstanding.Add(order);
                return outstanding;
            }
        }

        public string LastProcurementNote { get; private set; } = string.Empty;
        public int LastProcurementDayIndex { get; private set; } = -1;

        /// <summary>Target yard stock for the season: the base target, scaled in cold season.</summary>
        public int TargetStockIntakeUnitsFor(bool coldSeason)
        {
            int target = TargetStockIntakeUnits > 0 ? TargetStockIntakeUnits : DefaultTargetStockIntakeUnits;
            if (!coldSeason) return target;
            double multiplier = ColdSeasonTargetMultiplier > 0 ? ColdSeasonTargetMultiplier : DefaultColdSeasonTargetMultiplier;
            return (int)Math.Ceiling(target * multiplier);
        }

        /// <summary>Records the configured retail bundle price. Non-positive values are ignored (price stays unset).</summary>
        public void SetConfiguredBundlePriceCents(int priceCents)
        {
            if (priceCents > 0) ConfiguredBundlePriceCents = priceCents;
        }

        /// <summary>
        /// Dealer margin per household bundle at the configured price:
        /// bundle price minus the byproduct cost of the 10 intake units in
        /// it (Decision 1: 1c per intake unit). 0 when no price is configured.
        /// A diagnostic, not a price — the price itself stays caller-configured.
        /// </summary>
        public int MarginPerBundleCents()
        {
            if (ConfiguredBundlePriceCents <= 0) return 0;
            int costPerBundle = FuelConversionRates.IntakeUnitsPerHouseholdBundle
                * FuelConversionRates.SawmillByproductPriceCentsPerIntakeUnit;
            return ConfiguredBundlePriceCents - costPerBundle;
        }

        /// <summary>
        /// Syncs the local-sawmill relationship from the formed mill
        /// business id (or null when no mill is formed). The relationship
        /// keeps its totals when the mill goes unavailable.
        /// </summary>
        public void SyncLocalSupplier(string millBusinessId, int dayIndex)
        {
            FuelDealerSupplierRelationship local = FindSupplierByKind(FuelDealerSupplierKind.LocalSawmill);
            if (string.IsNullOrWhiteSpace(millBusinessId))
            {
                if (local != null) local.Status = FuelDealerSupplierStatus.Unavailable;
                return;
            }
            if (local == null)
            {
                local = new FuelDealerSupplierRelationship
                {
                    SupplierId = millBusinessId,
                    Kind = FuelDealerSupplierKind.LocalSawmill,
                    InputSkuId = "slabs_offcuts",
                    PriceCentsPerInputUnit = FuelConversionRates.SawmillByproductPriceCentsPerIntakeUnit,
                    ConversionRateFuelPerInputUnit = 1.0,
                    Terms = "sawmill byproduct, 1c per intake unit before transport and processing (Decision 1)",
                    Status = FuelDealerSupplierStatus.Active,
                    FirstSeenDayIndex = dayIndex,
                };
                suppliers.Add(local);
                return;
            }
            local.SupplierId = millBusinessId;
            local.PriceCentsPerInputUnit = FuelConversionRates.SawmillByproductPriceCentsPerIntakeUnit;
            local.ConversionRateFuelPerInputUnit = 1.0;
            local.Terms = "sawmill byproduct, 1c per intake unit before transport and processing (Decision 1)";
            local.Status = FuelDealerSupplierStatus.Active;
            if (local.FirstSeenDayIndex < 0) local.FirstSeenDayIndex = dayIndex;
        }

        /// <summary>
        /// The daily purchasing decision: does the dealer want the mill's
        /// held byproduct today? Demand only — affordability is the
        /// driver's gate (it refuses the transfer when the dealer cannot
        /// pay). Sets LastProcurementNote / LastProcurementDayIndex.
        /// </summary>
        public FuelDealerProcurementAssessment AssessLocalPurchase(int dayIndex, int heldLocalUnits, bool coldSeason)
        {
            int stock = StockIntakeUnits;
            int target = TargetStockIntakeUnitsFor(coldSeason);
            int reorder = ReorderPointIntakeUnits > 0 ? ReorderPointIntakeUnits : DefaultReorderPointIntakeUnits;
            int expectedIncoming = ExpectedIncomingFuelUnits();
            int projectedStock = (int)Math.Min(int.MaxValue, (long)stock + expectedIncoming);
            var assessment = new FuelDealerProcurementAssessment();
            if (heldLocalUnits > 0)
            {
                if (projectedStock >= target)
                {
                    assessment.Decision = FuelDealerProcurementDecision.DeclineFull;
                    assessment.WantIntakeUnits = 0;
                    assessment.Reason = $"yard holds {stock}u with {expectedIncoming}u expected from placed orders ({projectedStock}u projected), at/above target {target}u — declining the mill's {heldLocalUnits}u; the mill keeps its slabs.";
                }
                else
                {
                    assessment.Decision = FuelDealerProcurementDecision.Buy;
                    assessment.WantIntakeUnits = target - projectedStock;
                    assessment.Reason = $"yard holds {stock}u with {expectedIncoming}u expected from placed orders ({projectedStock}u projected), below target {target}u — the remaining procurement gap is {assessment.WantIntakeUnits}u; local output available today is {heldLocalUnits}u.";
                }
            }
            // The reorder point is the action threshold: reaching it means
            // restocking should begin. Only a projection strictly above it
            // suppresses another order, so an incoming shipment that lands
            // exactly on the threshold does not defer procurement a full day.
            else if (projectedStock > reorder)
            {
                assessment.Decision = FuelDealerProcurementDecision.Sufficient;
                assessment.WantIntakeUnits = 0;
                assessment.Reason = $"yard holds {stock}u with {expectedIncoming}u expected from placed orders ({projectedStock}u projected), above reorder {reorder}u — no local output today, nothing to do.";
            }
            else
            {
                FuelDealerSupplierRelationship offMap = FindActiveOffMapSupplier();
                if (offMap != null)
                {
                    assessment.Decision = FuelDealerProcurementDecision.OffMapAvailable;
                    assessment.WantIntakeUnits = target - projectedStock;
                    assessment.Reason = $"yard holds {stock}u with {expectedIncoming}u expected from placed orders ({projectedStock}u projected), at or below reorder {reorder}u, no local output — authored off-map supplier '{offMap.SupplierId}' is available for the remaining {assessment.WantIntakeUnits}u target gap.";
                }
                else
                {
                    assessment.Decision = FuelDealerProcurementDecision.NoSupply;
                    assessment.WantIntakeUnits = 0;
                    assessment.Reason = $"yard holds {stock}u with {expectedIncoming}u expected from placed orders ({projectedStock}u projected), at or below reorder {reorder}u, no local output and no authored off-map supplier — cannot restock; no vendor is conjured.";
                }
            }
            LastProcurementNote = assessment.Reason;
            LastProcurementDayIndex = dayIndex;
            return assessment;
        }

        /// <summary>
        /// Counts only fuel that active placed orders are expected to yield.
        /// This is a planning estimate, not saleable stock or a reservation;
        /// physical intake remains the inventory authority until receipt.
        /// Fractional conversions are floored exactly as they are on receipt.
        /// </summary>
        private int ExpectedIncomingFuelUnits()
        {
            long total = 0;
            foreach (FuelDealerOrder order in orders)
            {
                if (order == null || order.Status != FuelDealerOrderStatus.Placed
                    || order.InputUnits <= 0
                    || double.IsNaN(order.ConversionRateFuelPerInputUnit)
                    || double.IsInfinity(order.ConversionRateFuelPerInputUnit)
                    || order.ConversionRateFuelPerInputUnit <= 0d)
                {
                    continue;
                }

                double converted = Math.Floor(order.InputUnits * order.ConversionRateFuelPerInputUnit);
                if (converted <= 0d) continue;
                if (converted >= int.MaxValue || total + (long)converted >= int.MaxValue)
                    return int.MaxValue;
                total += (long)converted;
            }
            return (int)total;
        }

        /// <summary>
        /// Records a settled local purchase from the driver's actual
        /// numbers (units, price, lot count) — never from an estimate.
        /// Zero units settle vacuously: no entry, no supplier change.
        /// </summary>
        public void RecordLocalPurchase(int dayIndex, string millBusinessId, int settledUnits, int priceCents, int lotCount)
        {
            if (settledUnits <= 0) return;
            FuelDealerSupplierRelationship local = FindSupplierByKind(FuelDealerSupplierKind.LocalSawmill);
            if (local == null)
            {
                local = new FuelDealerSupplierRelationship
                {
                    SupplierId = string.IsNullOrWhiteSpace(millBusinessId) ? "local sawmill" : millBusinessId,
                    Kind = FuelDealerSupplierKind.LocalSawmill,
                    InputSkuId = "slabs_offcuts",
                    PriceCentsPerInputUnit = FuelConversionRates.SawmillByproductPriceCentsPerIntakeUnit,
                    ConversionRateFuelPerInputUnit = 1.0,
                    Terms = "sawmill byproduct, 1c per intake unit before transport and processing (Decision 1)",
                    Status = FuelDealerSupplierStatus.Active,
                    FirstSeenDayIndex = dayIndex,
                };
                suppliers.Add(local);
            }
            local.TotalFuelUnitsReceived += settledUnits;
            local.TotalCentsPaid += priceCents;
            local.LastDeliveryDayIndex = dayIndex;
            local.Status = FuelDealerSupplierStatus.Active;
            cashBook.Add(new FuelDealerCashBookEntry
            {
                DayIndex = dayIndex,
                Kind = FuelDealerCashBookKind.LocalPurchase,
                CounterpartyId = string.IsNullOrWhiteSpace(millBusinessId) ? "local sawmill" : millBusinessId,
                IntakeUnits = settledUnits,
                AmountCents = -priceCents,
                Note = $"{lotCount} lot(s), {settledUnits}u @ 1c/u (Decision 1)",
            });
        }

        /// <summary>
        /// Registers an authored off-map supplier. Returns null on success,
        /// or the refusal. No supplier exists unless registered here with
        /// an id, an input SKU, a price, and a conversion rate — in
        /// particular there is no Timberline and no default vendor.
        /// </summary>
        public string RegisterOffMapSupplier(
            string supplierId, string inputSkuId, int priceCentsPerInputUnit,
            double conversionRateFuelPerInputUnit, List<string> diag)
        {
            if (string.IsNullOrWhiteSpace(supplierId))
                return Refuse(diag, "FuelDealerRuntime: an off-map supplier needs an id; no vendor is conjured.");
            if (string.IsNullOrWhiteSpace(inputSkuId))
                return Refuse(diag, "FuelDealerRuntime: an off-map supplier needs an input SKU id; the raw input type is never defaulted.");
            if (priceCentsPerInputUnit <= 0)
                return Refuse(diag, "FuelDealerRuntime: an off-map supplier needs a positive price per input unit.");
            if (conversionRateFuelPerInputUnit <= 0)
                return Refuse(diag, "FuelDealerRuntime: an off-map supplier needs a positive conversion rate (fuel units per input unit).");
            if (FindSupplier(supplierId) != null)
                return Refuse(diag, $"FuelDealerRuntime: off-map supplier '{supplierId}' is already registered; register a new id, never silently overwrite.");
            suppliers.Add(new FuelDealerSupplierRelationship
            {
                SupplierId = supplierId,
                Kind = FuelDealerSupplierKind.OffMap,
                InputSkuId = inputSkuId.Trim(),
                PriceCentsPerInputUnit = priceCentsPerInputUnit,
                ConversionRateFuelPerInputUnit = conversionRateFuelPerInputUnit,
                Terms = $"off-map input '{inputSkuId.Trim()}' at {priceCentsPerInputUnit}c per input unit, {conversionRateFuelPerInputUnit} fuel units per input unit",
                Status = FuelDealerSupplierStatus.Active,
                FirstSeenDayIndex = -1,
            });
            return null;
        }

        /// <summary>
        /// Places an off-map order with a registered supplier. Returns the
        /// order, or null with the refusal in diag. Placement moves no cash
        /// and no stock: payment happens on receipt, so an order the dealer
        /// cannot yet pay stays placed until it can pay or is cancelled.
        /// </summary>
        public FuelDealerOrder PlaceOffMapOrder(string supplierId, int inputUnits, int dayIndex, List<string> diag)
        {
            FuelDealerSupplierRelationship supplier = FindSupplier(supplierId);
            if (supplier == null || supplier.Kind != FuelDealerSupplierKind.OffMap)
            {
                Refuse(diag, $"FuelDealerRuntime: no authored off-map supplier '{supplierId}' — off-map fuel must be authored, not assumed.");
                return null;
            }
            if (supplier.Status != FuelDealerSupplierStatus.Active)
            {
                Refuse(diag, $"FuelDealerRuntime: off-map supplier '{supplierId}' is not active.");
                return null;
            }
            if (inputUnits <= 0)
            {
                Refuse(diag, "FuelDealerRuntime: an off-map order needs a positive input quantity.");
                return null;
            }
            orderSequence++;
            var order = new FuelDealerOrder
            {
                OrderId = $"fuel-order-{DealerBusinessId}-{dayIndex}-{orderSequence}",
                SupplierId = supplier.SupplierId,
                InputSkuId = supplier.InputSkuId,
                InputUnits = inputUnits,
                PriceCentsPerInputUnit = supplier.PriceCentsPerInputUnit,
                ConversionRateFuelPerInputUnit = supplier.ConversionRateFuelPerInputUnit,
                Status = FuelDealerOrderStatus.Placed,
                PlacedDayIndex = dayIndex,
            };
            orders.Add(order);
            return order;
        }

        /// <summary>
        /// Receives an off-map order into real intake lots with off-map
        /// provenance — the driver the Pass 3 input model was waiting for.
        /// Pays the supplier on receipt through the cash port (cash on
        /// delivery): the receipt refuses when the dealer cannot pay, and
        /// the order stays placed. Conversion floors (input x rate), so
        /// fractional input never conjures a whole fuel unit. Returns null
        /// on success, or the refusal. A received order can never be
        /// received again.
        /// </summary>
        public string ReceiveOffMapOrder(
            string orderId, string inputLotId, int receivedInputUnits,
            int dayIndex, EntityIdRegistry ids, List<string> diag)
        {
            FuelDealerOrder order = FindOrder(orderId);
            if (order == null)
                return Refuse(diag, $"FuelDealerRuntime: unknown off-map order '{orderId}'; nothing received.");
            if (order.Status == FuelDealerOrderStatus.Received)
                return Refuse(diag, $"FuelDealerRuntime: off-map order '{orderId}' was already received on day {order.ClosedDayIndex}; no double receipt.");
            if (order.Status == FuelDealerOrderStatus.Cancelled)
                return Refuse(diag, $"FuelDealerRuntime: off-map order '{orderId}' was cancelled on day {order.ClosedDayIndex}; nothing received.");
            if (receivedInputUnits <= 0)
                return Refuse(diag, $"FuelDealerRuntime: off-map order '{orderId}' needs a positive received quantity.");
            if (string.IsNullOrWhiteSpace(inputLotId))
                return Refuse(diag, $"FuelDealerRuntime: off-map order '{orderId}' needs a named input lot id.");
            if (ids == null)
                return Refuse(diag, $"FuelDealerRuntime: off-map order '{orderId}' needs an id registry; received fuel lots need real ids.");
            if (cash == null)
                return Refuse(diag, $"FuelDealerRuntime: off-map order '{orderId}' cannot be paid — the dealer has no cash authority; no fuel moved unpaid.");
            int fuelUnits = (int)Math.Floor(receivedInputUnits * order.ConversionRateFuelPerInputUnit);
            if (fuelUnits <= 0)
                return Refuse(diag, $"FuelDealerRuntime: off-map order '{orderId}' converts {receivedInputUnits}u input to no fuel unit; nothing received.");
            long priceCents = (long)receivedInputUnits * order.PriceCentsPerInputUnit;
            if (priceCents > int.MaxValue)
                return Refuse(diag, $"FuelDealerRuntime: off-map order '{orderId}' price exceeds the supported transaction limit; nothing received.");
            string debitProblem = cash.TryDebit((int)priceCents,
                $"fuel dealer off-map purchase {orderId} ({receivedInputUnits}u input from '{order.SupplierId}')");
            if (debitProblem != null)
                return Refuse(diag, $"FuelDealerRuntime: off-map order '{orderId}' refused — dealer cannot pay {priceCents}c ({debitProblem}); order stays placed, nothing received.");
            var lot = new SlabOffcutFuelLot
            {
                LotId = ids.Allocate(EntityKind.Lot),
                FuelWoodUnits = fuelUnits,
                OwnerId = DealerBusinessId,
                SourceMillBusinessId = string.Empty,
                SourceLogLotId = string.Empty,
                StandId = string.Empty,
                Species = order.InputSkuId,
                ProducedDayIndex = dayIndex,
                OffMapSupplierId = order.SupplierId,
                OffMapOrderId = order.OrderId,
                OffMapInputLotId = inputLotId,
            };
            string receiptRefusal = intake.ReceiveOffMapLot(DealerBusinessId, lot, diag);
            if (receiptRefusal != null)
            {
                // Unreachable in practice: the lot id is fresh from the
                // registry and every field was validated above. If it ever
                // fires, the cash already moved, so the books must be
                // reconciled — never silently re-paid, never re-received.
                cashBook.Add(new FuelDealerCashBookEntry
                {
                    DayIndex = dayIndex,
                    Kind = FuelDealerCashBookKind.OffMapPurchase,
                    CounterpartyId = order.SupplierId,
                    IntakeUnits = 0,
                    AmountCents = -(int)priceCents,
                    Note = $"off-map order '{order.OrderId}' paid {priceCents}c but the fuel lot was refused ({receiptRefusal}); reconcile, do not re-pay.",
                });
                return receiptRefusal;
            }
            order.Status = FuelDealerOrderStatus.Received;
            order.ClosedDayIndex = dayIndex;
            order.InputLotId = inputLotId;
            order.FuelUnitsReceived = fuelUnits;
            FuelDealerSupplierRelationship supplier = FindSupplier(order.SupplierId);
            if (supplier != null)
            {
                supplier.TotalFuelUnitsReceived += fuelUnits;
                supplier.TotalCentsPaid += (int)priceCents;
                supplier.LastDeliveryDayIndex = dayIndex;
            }
            cashBook.Add(new FuelDealerCashBookEntry
            {
                DayIndex = dayIndex,
                Kind = FuelDealerCashBookKind.OffMapPurchase,
                CounterpartyId = order.SupplierId,
                IntakeUnits = fuelUnits,
                AmountCents = -(int)priceCents,
                Note = $"off-map order '{order.OrderId}', {receivedInputUnits}u input '{order.InputSkuId}' -> {fuelUnits}u fuel, input lot '{inputLotId}'",
            });
            diag?.Add($"FuelDealerRuntime: off-map order '{order.OrderId}' received {fuelUnits}u fuel from '{order.SupplierId}' for {priceCents}c.");
            return null;
        }

        /// <summary>Cancels a placed off-map order. Received and cancelled orders cannot be cancelled. Moves no cash, no stock.</summary>
        public string CancelOffMapOrder(string orderId, int dayIndex, List<string> diag)
        {
            FuelDealerOrder order = FindOrder(orderId);
            if (order == null)
                return Refuse(diag, $"FuelDealerRuntime: unknown off-map order '{orderId}'; nothing cancelled.");
            if (order.Status == FuelDealerOrderStatus.Received)
                return Refuse(diag, $"FuelDealerRuntime: off-map order '{orderId}' was already received on day {order.ClosedDayIndex}; a received order cannot be cancelled.");
            if (order.Status == FuelDealerOrderStatus.Cancelled)
                return Refuse(diag, $"FuelDealerRuntime: off-map order '{orderId}' was already cancelled on day {order.ClosedDayIndex}.");
            order.Status = FuelDealerOrderStatus.Cancelled;
            order.ClosedDayIndex = dayIndex;
            return null;
        }

        /// <summary>Records a retail sale reported by the Pass 7 adapter. Vacuous for zero bundles.</summary>
        public void RecordRetailSale(
            int dayIndex, int bundlesSold, int intakeUnits, int revenueCents,
            string receiptId, bool cashCredited, bool onTradeCredit)
        {
            if (bundlesSold <= 0) return;
            if (onTradeCredit)
            {
                cashBook.Add(new FuelDealerCashBookEntry
                {
                    DayIndex = dayIndex,
                    Kind = FuelDealerCashBookKind.RetailSale,
                    CounterpartyId = "household retail",
                    IntakeUnits = intakeUnits,
                    AmountCents = 0,
                    Note = $"sold {bundlesSold}u retail on trade credit {receiptId} ({revenueCents}c receivable, cash untouched)",
                });
                return;
            }
            cashBook.Add(new FuelDealerCashBookEntry
            {
                DayIndex = dayIndex,
                Kind = FuelDealerCashBookKind.RetailSale,
                CounterpartyId = "household retail",
                IntakeUnits = intakeUnits,
                AmountCents = cashCredited ? revenueCents : 0,
                Note = cashCredited
                    ? $"sold {bundlesSold}u retail {receiptId}"
                    : $"sold {bundlesSold}u retail {receiptId} — dealer cash port refused {revenueCents}c credit; goods dispensed, cash not received; reconcile",
            });
        }

        /// <summary>Records a bakery sale reported by the paid dealer source. Vacuous for zero units.</summary>
        public void RecordBakerySale(
            int dayIndex, int bakeryUnits, int intakeUnits, int revenueCents,
            string bakeryBusinessId, string bakeryLotId)
        {
            if (bakeryUnits <= 0) return;
            cashBook.Add(new FuelDealerCashBookEntry
            {
                DayIndex = dayIndex,
                Kind = FuelDealerCashBookKind.BakerySale,
                CounterpartyId = string.IsNullOrWhiteSpace(bakeryBusinessId) ? "bakery" : bakeryBusinessId,
                IntakeUnits = intakeUnits,
                AmountCents = revenueCents,
                Note = $"sold {bakeryUnits}u bakery fuel ({intakeUnits} intake units) in lot {bakeryLotId}",
            });
        }

        // ---- Save / load ----

        public FuelDealerRuntimeSaveDto CaptureSaveDto()
        {
            var dto = new FuelDealerRuntimeSaveDto
            {
                DealerBusinessId = DealerBusinessId,
                TargetStockIntakeUnits = TargetStockIntakeUnits,
                ReorderPointIntakeUnits = ReorderPointIntakeUnits,
                ColdSeasonTargetMultiplier = ColdSeasonTargetMultiplier,
                ConfiguredBundlePriceCents = ConfiguredBundlePriceCents,
                LastProcurementNote = LastProcurementNote ?? string.Empty,
                LastProcurementDayIndex = LastProcurementDayIndex,
                OrderSequence = orderSequence,
            };
            foreach (FuelDealerSupplierRelationship supplier in suppliers)
            {
                if (supplier == null) continue;
                dto.Suppliers.Add(new FuelDealerSupplierRelationship
                {
                    SupplierId = supplier.SupplierId,
                    Kind = supplier.Kind,
                    InputSkuId = supplier.InputSkuId,
                    PriceCentsPerInputUnit = supplier.PriceCentsPerInputUnit,
                    ConversionRateFuelPerInputUnit = supplier.ConversionRateFuelPerInputUnit,
                    Terms = supplier.Terms,
                    Status = supplier.Status,
                    FirstSeenDayIndex = supplier.FirstSeenDayIndex,
                    LastDeliveryDayIndex = supplier.LastDeliveryDayIndex,
                    TotalFuelUnitsReceived = supplier.TotalFuelUnitsReceived,
                    TotalCentsPaid = supplier.TotalCentsPaid,
                });
            }
            foreach (FuelDealerOrder order in orders)
            {
                if (order == null) continue;
                dto.Orders.Add(new FuelDealerOrder
                {
                    OrderId = order.OrderId,
                    SupplierId = order.SupplierId,
                    InputSkuId = order.InputSkuId,
                    InputUnits = order.InputUnits,
                    PriceCentsPerInputUnit = order.PriceCentsPerInputUnit,
                    ConversionRateFuelPerInputUnit = order.ConversionRateFuelPerInputUnit,
                    Status = order.Status,
                    PlacedDayIndex = order.PlacedDayIndex,
                    ClosedDayIndex = order.ClosedDayIndex,
                    InputLotId = order.InputLotId,
                    FuelUnitsReceived = order.FuelUnitsReceived,
                });
            }
            foreach (FuelDealerCashBookEntry entry in cashBook)
            {
                if (entry == null) continue;
                dto.CashBook.Add(new FuelDealerCashBookEntry
                {
                    DayIndex = entry.DayIndex,
                    Kind = entry.Kind,
                    CounterpartyId = entry.CounterpartyId,
                    IntakeUnits = entry.IntakeUnits,
                    AmountCents = entry.AmountCents,
                    Note = entry.Note,
                });
            }
            return dto;
        }

        /// <summary>
        /// Clear-then-load, idempotent. A null DTO clears to defaults (the
        /// honest old-save default). A DTO for another dealer is refused
        /// (identity guard) and the live state is left untouched.
        /// Returns false on refusal, true otherwise.
        /// </summary>
        public bool LoadFromSaveDto(FuelDealerRuntimeSaveDto dto)
        {
            if (dto == null)
            {
                ClearToDefaults();
                return true;
            }
            if (string.IsNullOrWhiteSpace(dto.DealerBusinessId)
                || !string.Equals(dto.DealerBusinessId, DealerBusinessId, StringComparison.Ordinal))
                return false;
            ClearToDefaults();
            TargetStockIntakeUnits = dto.TargetStockIntakeUnits > 0
                ? dto.TargetStockIntakeUnits : DefaultTargetStockIntakeUnits;
            ReorderPointIntakeUnits = dto.ReorderPointIntakeUnits > 0
                ? dto.ReorderPointIntakeUnits : DefaultReorderPointIntakeUnits;
            ColdSeasonTargetMultiplier = dto.ColdSeasonTargetMultiplier > 0
                ? dto.ColdSeasonTargetMultiplier : DefaultColdSeasonTargetMultiplier;
            ConfiguredBundlePriceCents = Math.Max(0, dto.ConfiguredBundlePriceCents);
            LastProcurementNote = dto.LastProcurementNote ?? string.Empty;
            LastProcurementDayIndex = dto.LastProcurementDayIndex;
            orderSequence = Math.Max(0, dto.OrderSequence);
            if (dto.Suppliers != null)
            {
                foreach (FuelDealerSupplierRelationship supplier in dto.Suppliers)
                {
                    if (supplier == null) continue;
                    suppliers.Add(new FuelDealerSupplierRelationship
                    {
                        SupplierId = supplier.SupplierId,
                        Kind = supplier.Kind,
                        InputSkuId = supplier.InputSkuId,
                        PriceCentsPerInputUnit = supplier.PriceCentsPerInputUnit,
                        ConversionRateFuelPerInputUnit = supplier.ConversionRateFuelPerInputUnit,
                        Terms = supplier.Terms,
                        Status = supplier.Status,
                        FirstSeenDayIndex = supplier.FirstSeenDayIndex,
                        LastDeliveryDayIndex = supplier.LastDeliveryDayIndex,
                        TotalFuelUnitsReceived = supplier.TotalFuelUnitsReceived,
                        TotalCentsPaid = supplier.TotalCentsPaid,
                    });
                }
            }
            if (dto.Orders != null)
            {
                foreach (FuelDealerOrder order in dto.Orders)
                {
                    if (order == null) continue;
                    orders.Add(new FuelDealerOrder
                    {
                        OrderId = order.OrderId,
                        SupplierId = order.SupplierId,
                        InputSkuId = order.InputSkuId,
                        InputUnits = order.InputUnits,
                        PriceCentsPerInputUnit = order.PriceCentsPerInputUnit,
                        ConversionRateFuelPerInputUnit = order.ConversionRateFuelPerInputUnit,
                        Status = order.Status,
                        PlacedDayIndex = order.PlacedDayIndex,
                        ClosedDayIndex = order.ClosedDayIndex,
                        InputLotId = order.InputLotId,
                        FuelUnitsReceived = order.FuelUnitsReceived,
                    });
                }
            }
            if (dto.CashBook != null)
            {
                foreach (FuelDealerCashBookEntry entry in dto.CashBook)
                {
                    if (entry == null) continue;
                    cashBook.Add(new FuelDealerCashBookEntry
                    {
                        DayIndex = entry.DayIndex,
                        Kind = entry.Kind,
                        CounterpartyId = entry.CounterpartyId,
                        IntakeUnits = entry.IntakeUnits,
                        AmountCents = entry.AmountCents,
                        Note = entry.Note,
                    });
                }
            }
            return true;
        }

        private void ClearToDefaults()
        {
            suppliers.Clear();
            orders.Clear();
            cashBook.Clear();
            TargetStockIntakeUnits = DefaultTargetStockIntakeUnits;
            ReorderPointIntakeUnits = DefaultReorderPointIntakeUnits;
            ColdSeasonTargetMultiplier = DefaultColdSeasonTargetMultiplier;
            ConfiguredBundlePriceCents = 0;
            LastProcurementNote = string.Empty;
            LastProcurementDayIndex = -1;
            orderSequence = 0;
        }

        private FuelDealerSupplierRelationship FindSupplier(string supplierId)
        {
            if (string.IsNullOrWhiteSpace(supplierId)) return null;
            foreach (FuelDealerSupplierRelationship supplier in suppliers)
                if (supplier != null && string.Equals(supplier.SupplierId, supplierId, StringComparison.Ordinal))
                    return supplier;
            return null;
        }

        private FuelDealerSupplierRelationship FindSupplierByKind(FuelDealerSupplierKind kind)
        {
            foreach (FuelDealerSupplierRelationship supplier in suppliers)
                if (supplier != null && supplier.Kind == kind)
                    return supplier;
            return null;
        }

        private FuelDealerSupplierRelationship FindActiveOffMapSupplier()
        {
            foreach (FuelDealerSupplierRelationship supplier in suppliers)
                if (supplier != null && supplier.Kind == FuelDealerSupplierKind.OffMap
                    && supplier.Status == FuelDealerSupplierStatus.Active)
                    return supplier;
            return null;
        }

        private FuelDealerOrder FindOrder(string orderId)
        {
            if (string.IsNullOrWhiteSpace(orderId)) return null;
            foreach (FuelDealerOrder order in orders)
                if (order != null && string.Equals(order.OrderId, orderId, StringComparison.Ordinal))
                    return order;
            return null;
        }

        private static string Refuse(List<string> diag, string refusal)
        {
            diag?.Add(refusal);
            return refusal;
        }
    }
}
