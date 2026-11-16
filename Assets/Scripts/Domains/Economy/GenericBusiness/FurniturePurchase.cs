// Copyright (c) Land & Ledgers contributors. Licensed under the MIT License.
// Agent F, slice F4 (expanded campaign 2026-11-16): household furniture
// purchase + delivery end to end — the staged EquipmentPurchase shape
// (quote -> order -> pay -> deliver -> receive -> install -> operational)
// wired to the real authorities: HouseholdLedger (the one cash writer),
// F3 consolidated freight (one manifest, one wagon, one trip), and the
// household furnishing register through a caller-supplied install handler.
//
// B0 lives on the B branch, so F4 cannot call the register directly:
// the install handler is the seam G binds in production (register the
// received asset in the household furnishing register, then install it
// in the building). F4 guarantees the handler is invoked exactly once
// per received asset, only after receipt, never before — the tests pin
// the ordering, not the scene work.
//
// Cash: the buyer pays once, through the HouseholdLedger, at the Pay
// stage — one outflow for goods + freight. The seller's goods money
// and the carrier's fare are credited by one combined handler that
// must credit both or refuse without crediting either (atomicity is
// the binder's job); a credit refusal refunds the buyer in full and
// the purchase stays Ordered. Freight is a caller-supplied one-trip
// fare (DEF-016 prices per trip; a consolidated shipment is one trip)
// — F4 never invents a freight price, and the vendor quote is
// caller-supplied (EquipmentPurchase: prices are caller-supplied;
// F4's QuoteUnitPriceCents names the F2 cost-plus price as the quote
// source without inventing one).
//
// Goods, new kind: the vendor's source lots are debited per line at
// load (F3's CommitConsolidatedSourceLots hook); at receipt one
// EquipmentAsset is created per purchased UNIT — not one per line:
// the plan's F4 test list says "one asset per line", but B0 installs
// one asset per binding and F1 manufactures one asset per chair, so a
// single asset for 4 chairs could never be installed as 4 chairs.
// The test pins 6 assets for bed + table + 4 chairs. Maker is unknown
// until O-F3 binds a producing business, so MadeByBusinessId stays
// empty and provenance is the source lot id + purchase id + day.
//
// Goods, secondhand kind: nothing is created. The existing asset
// transfers ownership to the buyer at receipt (UsedEquipmentMarket
// pattern); the manifest line's lot id IS the asset id, one line per
// asset, Units must be 1. The commit step verifies the asset is still
// seller-owned and unreserved; a sale that moved first refuses loudly.
//
// Purchase records are runtime-only in F4 (no save DTO): a save/load
// mid-purchase is a known gap for a later slice. The shipment itself
// persists via FreightServiceSaveDto; the order would need
// re-derivation from the shipment + ledgers.

using System;
using System.Collections.Generic;
using System.Globalization;
using LandLedgers.Economy;
using LandLedgers.Economy.Blacksmith;
using LandLedgers.Economy.Logistics;
using LandLedgers.Economy.Storage;
using LandLedgers.Population;
using LandLedgers.Primitives;

namespace LandLedgers.Economy.GenericBusiness
{
    /// <summary>New furniture from a vendor's stock, or an existing asset bought secondhand.</summary>
    public enum FurniturePurchaseKind
    {
        New = 0,
        Secondhand = 1,
    }

    /// <summary>
    /// One line of a household furniture purchase: one furniture item,
    /// whole units, one vendor source lot (new kind) or one existing asset
    /// id (secondhand kind, Units must be 1), and the vendor-quoted unit
    /// price. Stage follows EquipmentPurchase (Quoted -> Ordered ->
    /// PaidOrFinanced -> Shipped -> Received -> Installed -> Operational).
    /// AssetIds are filled at receipt: created assets (new) or transferred
    /// assets (secondhand), one id per unit.
    /// </summary>
    public sealed class FurniturePurchaseLine
    {
        public string ItemId = string.Empty;
        public int Units;
        public string SourceLotId = string.Empty;
        public int UnitPriceCents;
        public EquipmentPurchaseStage Stage = EquipmentPurchaseStage.Quoted;
        public List<string> AssetIds = new List<string>();

        /// <summary>Secondhand only: the existing asset, resolved at Quote time. Runtime only.</summary>
        public EquipmentAsset ExistingAsset;
    }

    /// <summary>
    /// One household furniture purchase. The record is the staged
    /// EquipmentPurchase shape; the service drives it. Runtime only in
    /// F4 (no save DTO — see the file header).
    /// </summary>
    public sealed class FurniturePurchaseOrder
    {
        public string PurchaseId = string.Empty;
        public FurniturePurchaseKind Kind = FurniturePurchaseKind.New;
        public int HouseholdId = -1;
        public int SellerHouseholdId = -1; // secondhand only
        public string VendorId = string.Empty; // "general-store" (new) or "household:<seller>" (secondhand)
        public string VendorName = string.Empty;
        public int BuildingInstanceId = -1; // delivery + install location
        public int DayIndex;
        public List<FurniturePurchaseLine> Lines = new List<FurniturePurchaseLine>();
        public int FreightCents; // caller-supplied one-trip fare (DEF-016 prices per trip)
        public string ShipmentId = string.Empty;
        public List<string> Diagnostics = new List<string>();

        /// <summary>Assets received for this purchase, in line order. Runtime only.</summary>
        public List<EquipmentAsset> ReceivedAssets = new List<EquipmentAsset>();

        /// <summary>The freight hooks bound at ship time. Runtime only.</summary>
        public FreightShipmentHooks FreightHooks;

        public int GoodsTotalCents()
        {
            int total = 0;
            if (Lines != null)
            {
                foreach (FurniturePurchaseLine line in Lines)
                {
                    if (line != null) total += Math.Max(0, line.Units) * Math.Max(0, line.UnitPriceCents);
                }
            }

            return total;
        }

        public int TotalCents()
        {
            return GoodsTotalCents() + Math.Max(0, FreightCents);
        }

        public bool AllLinesAt(EquipmentPurchaseStage stage)
        {
            if (Lines == null || Lines.Count == 0) return false;
            foreach (FurniturePurchaseLine line in Lines)
            {
                if (line == null || line.Stage != stage) return false;
            }

            return true;
        }

        public bool IsOperational => AllLinesAt(EquipmentPurchaseStage.Operational);
    }

    /// <summary>
    /// The vendor's stock, for the new kind. Debit must refuse without
    /// moving stock when the lot does not hold the units; Credit is the
    /// rollback for a debit that succeeded before a later line refused.
    /// UnitsOnHand answers the pre-check so a shortfall refuses before
    /// any line moves.
    /// </summary>
    public interface IFurnitureVendorStock
    {
        int UnitsOnHand(string lotId);
        string TryDebit(string lotId, int units);
        string TryCredit(string lotId, int units);
    }

    /// <summary>
    /// Credits the seller's goods money and the carrier's fare for one
    /// purchase. Must credit both or refuse without crediting either —
    /// atomicity is the binder's job. Returns null on success, a refusal
    /// reason on failure. Production binds this to the business cash
    /// authority (new kind) or the seller household's ledger (secondhand).
    /// </summary>
    public delegate string CreditPurchaseProceedsHandler(int goodsCents, int freightCents, string purchaseId);

    /// <summary>
    /// Installs one received asset in the buyer's building. The B0 seam:
    /// production binds this to the household furnishing register
    /// (register the asset, then install it). Returns null on success, a
    /// refusal reason on failure. F4 calls it exactly once per received
    /// asset, only after receipt.
    /// </summary>
    public delegate string InstallPurchasedFurnitureHandler(EquipmentAsset asset, string purchaseId);

    /// <summary>
    /// Drives one household furniture purchase end to end: quote, order,
    /// pay, ship (F3 consolidated freight), deliver, receive, install.
    /// One service instance serves one buyer household; the caller
    /// supplies the vendor stock (new kind), the asset lookup (secondhand
    /// kind), the proceeds credit, and the install handler. The service
    /// never invents a price, a vendor, or an asset: every unit received
    /// corresponds 1:1 to a unit debited from the vendor (new) or an
    /// existing asset transferred (secondhand).
    /// </summary>
    public sealed class FurniturePurchaseService
    {
        private const string HouseholdOwnerKind = "household";

        private readonly HouseholdLedger buyerLedger;
        private readonly IFurnitureVendorStock vendorStock;
        private readonly CreditPurchaseProceedsHandler creditProceeds;
        private readonly InstallPurchasedFurnitureHandler installHandler;
        private readonly Func<string, EquipmentAsset> findAssetById;
        private readonly List<string> diagnostics = new List<string>();
        private readonly HashSet<string> seenPurchaseIds = new HashSet<string>(StringComparer.Ordinal);
        private readonly HashSet<string> committedShipments = new HashSet<string>(StringComparer.Ordinal);
        private readonly HashSet<string> receivedShipments = new HashSet<string>(StringComparer.Ordinal);
        private readonly HashSet<string> committedAssets = new HashSet<string>(StringComparer.Ordinal);

        public FurniturePurchaseService(
            HouseholdLedger buyerLedger,
            IFurnitureVendorStock vendorStock,
            CreditPurchaseProceedsHandler creditProceeds,
            InstallPurchasedFurnitureHandler installHandler,
            Func<string, EquipmentAsset> findAssetById)
        {
            this.buyerLedger = buyerLedger ?? throw new ArgumentNullException(nameof(buyerLedger));
            this.vendorStock = vendorStock;
            this.creditProceeds = creditProceeds ?? throw new ArgumentNullException(nameof(creditProceeds));
            this.installHandler = installHandler ?? throw new ArgumentNullException(nameof(installHandler));
            this.findAssetById = findAssetById;
        }

        public IReadOnlyList<string> Diagnostics => diagnostics;

        /// <summary>
        /// Owner id convention for a household-owned asset, matching
        /// HouseholdFurnishingRegister.OwnerIdForHousehold (the bare
        /// household id, invariant culture — NOT "household:&lt;id&gt;", which
        /// is the cash-store owner name, a different convention).
        /// </summary>
        public static string HouseholdOwnerId(int householdId)
        {
            return householdId.ToString(CultureInfo.InvariantCulture);
        }

        /// <summary>
        /// Custody location for a household-owned asset not yet installed,
        /// matching HouseholdFurnishingRegister.HouseholdCustodyLocationId.
        /// Install moves it to "building:&lt;instanceId&gt;".
        /// </summary>
        public static string HouseholdCustodyLocationId(int householdId)
        {
            return "household:" + householdId.ToString(CultureInfo.InvariantCulture);
        }

        /// <summary>
        /// The vendor quote for one unit from the F2 cost-plus price: the
        /// named quote source, not an invented price. RetailPricingDefinition
        /// is a struct, so a quote always carries a real pricing — there is
        /// no null pricing to refuse.
        /// </summary>
        public static int QuoteUnitPriceCents(RetailPricingDefinition pricing, float categoryMarkupMultiplier, float supplyDemandPressure)
        {
            return pricing.GetCostPlusPriceCents(categoryMarkupMultiplier, supplyDemandPressure);
        }

        /// <summary>
        /// Validates a purchase before ordering: known furniture items with
        /// Known freight measures, positive units and prices, a real vendor
        /// and delivery building, a unique purchase id, and — secondhand
        /// only — existing seller-owned assets, one per line. No cash moves.
        /// Lines stay Quoted.
        /// </summary>
        public string Quote(FurniturePurchaseOrder order)
        {
            if (order == null) return Reject("FurniturePurchase: no purchase order supplied.");
            if (string.IsNullOrWhiteSpace(order.PurchaseId))
                return Reject("FurniturePurchase: a purchase needs an id — every purchase is identified before it is ordered.");
            if (seenPurchaseIds.Contains(order.PurchaseId.Trim()))
                return Reject($"FurniturePurchase: purchase id '{order.PurchaseId}' was already quoted by this service — purchase ids are unique.");
            if (order.HouseholdId < 0)
                return Reject("FurniturePurchase: a purchase needs a real buying household.");
            if (string.IsNullOrWhiteSpace(order.VendorId))
                return Reject("FurniturePurchase: a purchase needs a vendor id — no vendor means no legitimate purchase.");
            if (order.BuildingInstanceId < 0)
                return Reject("FurniturePurchase: a purchase needs a delivery building — furniture is delivered to a real building, never to nowhere.");
            if (order.FreightCents < 0)
                return Reject("FurniturePurchase: the freight fare cannot be negative.");
            if (order.Lines == null || order.Lines.Count == 0)
                return Reject("FurniturePurchase: a purchase needs at least one line.");
            if (order.Kind == FurniturePurchaseKind.New && vendorStock == null)
                return Reject("FurniturePurchase: a new purchase needs the vendor stock wired — the goods come from real stock, never from nowhere.");
            if (order.Kind == FurniturePurchaseKind.Secondhand)
            {
                if (order.SellerHouseholdId < 0)
                    return Reject("FurniturePurchase: a secondhand purchase needs the selling household.");
                if (order.SellerHouseholdId == order.HouseholdId)
                    return Reject("FurniturePurchase: a household cannot buy secondhand from itself.");
                if (findAssetById == null)
                    return Reject("FurniturePurchase: a secondhand purchase needs the asset lookup wired — the goods are existing assets, never spawned.");
            }

            for (int i = 0; i < order.Lines.Count; i++)
            {
                FurniturePurchaseLine line = order.Lines[i];
                if (line == null)
                    return Reject($"FurniturePurchase: line {i} is null — refusing a phantom line.");
                if (!FurnitureItemSpecs.TryGetRow(line.ItemId, out FurnitureItemSpecs.Row row))
                    return Reject($"FurniturePurchase: line {i} names unknown furniture item '{line.ItemId}' — only first-wave furniture is sold.");
                if (line.Units <= 0)
                    return Reject($"FurniturePurchase: line {i} ({line.ItemId}) needs a positive unit count, not {line.Units}.");
                if (line.UnitPriceCents <= 0)
                    return Reject($"FurniturePurchase: line {i} ({line.ItemId}) needs a positive unit price — free inventory is never granted.");
                if (string.IsNullOrWhiteSpace(line.SourceLotId))
                    return Reject($"FurniturePurchase: line {i} ({line.ItemId}) needs a source lot id — every unit comes from a real lot.");
                if (line.Stage != EquipmentPurchaseStage.Quoted)
                    return Reject($"FurniturePurchase: line {i} ({line.ItemId}) is {line.Stage}, not Quoted — a purchase starts at Quoted.");
                string manifestRefusal = FurnitureCargoSpecs.TryBuildManifestLine(line.ItemId, line.SourceLotId.Trim(), line.Units, out FreightManifestLine manifestLine);
                if (manifestRefusal != null)
                    return Reject($"FurniturePurchase: line {i} cannot ship — {manifestRefusal}");
                if (order.Kind == FurniturePurchaseKind.Secondhand)
                {
                    if (line.Units != 1)
                        return Reject($"FurniturePurchase: secondhand line {i} ({line.ItemId}) must be one asset, not {line.Units} — one line per existing asset.");
                    EquipmentAsset asset = findAssetById(line.SourceLotId.Trim());
                    if (asset == null)
                        return Reject($"FurniturePurchase: secondhand line {i} names asset '{line.SourceLotId}', which does not exist — secondhand goods are existing assets, never spawned.");
                    if (asset.OwnerKind != HouseholdOwnerKind || asset.OwnerId != HouseholdOwnerId(order.SellerHouseholdId))
                        return Reject($"FurniturePurchase: secondhand line {i} asset '{asset.AssetId}' is owned by {asset.OwnerKind}:{asset.OwnerId}, not the selling household {order.SellerHouseholdId}.");
                    if (asset.IsReserved)
                        return Reject($"FurniturePurchase: secondhand line {i} asset '{asset.AssetId}' is reserved — a reserved asset cannot be sold.");
                    line.ExistingAsset = asset;
                }
            }

            seenPurchaseIds.Add(order.PurchaseId.Trim());
            order.Diagnostics.Add($"day {order.DayIndex}: purchase '{order.PurchaseId}' quoted — {order.Lines.Count} line(s), goods {order.GoodsTotalCents()}c + freight {Math.Max(0, order.FreightCents)}c = {order.TotalCents()}c.");
            return null;
        }

        /// <summary>
        /// Orders a quoted purchase: the household must afford the total
        /// (goods + freight) from its real ledger balance. No cash moves.
        /// An unaffordable order is refused with the shortfall named.
        /// </summary>
        public string TryOrder(FurniturePurchaseOrder order)
        {
            if (order == null) return Reject("FurniturePurchase: no purchase order supplied.");
            if (!order.AllLinesAt(EquipmentPurchaseStage.Quoted))
                return Reject($"FurniturePurchase: purchase '{order.PurchaseId}' is not fully Quoted — an order starts at Quoted.");
            int total = order.TotalCents();
            int balance = buyerLedger.GetBalanceCents();
            if (total > balance)
                return Reject($"FurniturePurchase: purchase '{order.PurchaseId}' costs {total}c but household {order.HouseholdId} holds {balance}c — insufficient funds, nothing moved.");
            foreach (FurniturePurchaseLine line in order.Lines)
            {
                AdvanceLine(line);
            }

            order.Diagnostics.Add($"day {order.DayIndex}: purchase '{order.PurchaseId}' ordered — {total}c against a balance of {balance}c.");
            return null;
        }

        /// <summary>
        /// Pays an ordered purchase: ONE outflow from the buyer ledger for
        /// goods + freight, then one combined proceeds credit (seller goods
        /// + carrier fare, both or neither). A refused credit refunds the
        /// buyer in full and the purchase stays Ordered. Exactly once: a
        /// second pay is refused by the stage guard.
        /// </summary>
        public string TryPay(FurniturePurchaseOrder order)
        {
            if (order == null) return Reject("FurniturePurchase: no purchase order supplied.");
            if (!order.AllLinesAt(EquipmentPurchaseStage.Ordered))
                return Reject($"FurniturePurchase: purchase '{order.PurchaseId}' is not fully Ordered — payment follows ordering.");
            int total = order.TotalCents();
            int balance = buyerLedger.GetBalanceCents();
            if (total > balance)
                return Reject($"FurniturePurchase: purchase '{order.PurchaseId}' costs {total}c but household {order.HouseholdId} holds {balance}c — the balance moved since ordering, nothing moved.");
            string outflowRefusal = buyerLedger.RecordOutflow(order.DayIndex, total,
                $"furniture purchase {order.PurchaseId} ({order.Kind})", order.VendorId);
            if (outflowRefusal != null)
                return Reject($"FurniturePurchase: purchase '{order.PurchaseId}' could not be paid — {outflowRefusal}");
            string creditRefusal = creditProceeds(order.GoodsTotalCents(), Math.Max(0, order.FreightCents), order.PurchaseId);
            if (creditRefusal != null)
            {
                string refundRefusal = buyerLedger.RecordInflow(order.DayIndex, total,
                    HouseholdIncomeSource.OtherDocumented, order.PurchaseId,
                    $"refund of refused furniture purchase {order.PurchaseId}: {creditRefusal}", order.VendorId);
                if (refundRefusal != null)
                    return Reject($"FurniturePurchase: purchase '{order.PurchaseId}' credit refused ({creditRefusal}) and the refund also refused ({refundRefusal}) — the buyer outflow stands, recorded here, never hidden.");
                order.Diagnostics.Add($"day {order.DayIndex}: purchase '{order.PurchaseId}' payment refused at credit ({creditRefusal}) — buyer refunded {total}c in full, purchase stays Ordered.");
                return Reject($"FurniturePurchase: purchase '{order.PurchaseId}' payment refused at credit — {creditRefusal}; buyer refunded in full, purchase stays Ordered.");
            }

            foreach (FurniturePurchaseLine line in order.Lines)
            {
                AdvanceLine(line);
            }

            order.Diagnostics.Add($"day {order.DayIndex}: purchase '{order.PurchaseId}' paid — buyer {total}c out (goods {order.GoodsTotalCents()}c + freight {Math.Max(0, order.FreightCents)}c), seller and carrier credited.");
            return null;
        }

        /// <summary>
        /// Ships a paid purchase as ONE consolidated F3 shipment: one
        /// manifest, one wagon, one trip. Binds the F4 commit/receive
        /// closures on the supplied hooks (the same hooks object must be
        /// passed to TryDeliver). An over-capacity manifest is refused
        /// pre-load by the service — the purchase stays Paid, the goods
        /// are paid for, and the shipment must be re-planned (smaller
        /// manifest or a second trip), never silently dropped.
        /// </summary>
        public string TryShip(FurniturePurchaseOrder order, FreightShipmentService freight, string wagonAssetId, FreightShipmentHooks hooks)
        {
            if (order == null) return Reject("FurniturePurchase: no purchase order supplied.");
            if (freight == null) return Reject("FurniturePurchase: a shipment needs the freight service.");
            if (hooks == null) return Reject("FurniturePurchase: a shipment needs the freight hooks.");
            if (string.IsNullOrWhiteSpace(wagonAssetId))
                return Reject("FurniturePurchase: a shipment needs a real wagon — furniture travels by wagon, never by fiat.");
            if (!order.AllLinesAt(EquipmentPurchaseStage.PaidOrFinanced))
                return Reject($"FurniturePurchase: purchase '{order.PurchaseId}' is not fully Paid — shipment follows payment.");
            var lines = new List<FreightManifestLine>();
            foreach (FurniturePurchaseLine line in order.Lines)
            {
                string manifestRefusal = FurnitureCargoSpecs.TryBuildManifestLine(line.ItemId, line.SourceLotId.Trim(), line.Units, out FreightManifestLine manifestLine);
                if (manifestRefusal != null)
                    return Reject($"FurniturePurchase: purchase '{order.PurchaseId}' cannot ship — {manifestRefusal}");
                lines.Add(manifestLine);
            }

            hooks.CommitConsolidatedSourceLots = (LogisticsShipmentState shipment, out string reason) =>
                CommitForOrder(order, shipment, out reason);
            hooks.ReceiveConsolidatedCargoLots = (LogisticsShipmentState shipment, out string reason) =>
                ReceiveForOrder(order, shipment, out reason);
            order.FreightHooks = hooks;
            order.ShipmentId = "ship-" + order.PurchaseId.Trim();
            string sourceId = order.Kind == FurniturePurchaseKind.Secondhand
                ? HouseholdCustodyLocationId(order.SellerHouseholdId)
                : order.VendorId;
            LogisticsShipmentState shipment = freight.CreateConsolidatedShipment(order.ShipmentId, sourceId,
                HouseholdCustodyLocationId(order.HouseholdId), lines, wagonAssetId.Trim(), hooks, out string createReason);
            if (shipment == null)
                return Reject($"FurniturePurchase: purchase '{order.PurchaseId}' shipment refused — {createReason}");
            if (shipment.IsPreLoadBlocked)
                return Reject($"FurniturePurchase: purchase '{order.PurchaseId}' shipment blocked pre-load — {shipment.BlockedReason}; the goods are paid for, re-plan the shipment (smaller manifest or a second trip), never silently dropped.");
            foreach (FurniturePurchaseLine line in order.Lines)
            {
                AdvanceLine(line);
            }

            order.Diagnostics.Add($"day {order.DayIndex}: purchase '{order.PurchaseId}' shipped as '{order.ShipmentId}' on wagon '{wagonAssetId}' — one manifest, one trip.");
            return null;
        }

        /// <summary>
        /// Drives a shipped purchase through load, dispatch, transit and
        /// unload until the shipment completes. Any step refusal is loud
        /// and the purchase stays Shipped — cargo stays where the service
        /// left it (in wagon custody on a receipt refusal), never falsely
        /// marked delivered. On completion the lines advance to Received.
        /// </summary>
        public string TryDeliver(FurniturePurchaseOrder order, FreightShipmentService freight, FreightShipmentHooks hooks, EntityId loader, EntityId driver, List<EntityId> animals)
        {
            if (order == null) return Reject("FurniturePurchase: no purchase order supplied.");
            if (freight == null) return Reject("FurniturePurchase: delivery needs the freight service.");
            FreightShipmentHooks driveHooks = hooks ?? order.FreightHooks;
            if (driveHooks == null)
                return Reject($"FurniturePurchase: purchase '{order.PurchaseId}' has no freight hooks — ship first, with the same hooks object.");
            if (!order.AllLinesAt(EquipmentPurchaseStage.Shipped))
                return Reject($"FurniturePurchase: purchase '{order.PurchaseId}' is not fully Shipped — delivery follows shipment.");
            LogisticsShipmentState shipment = null;
            foreach (LogisticsShipmentState tracked in freight.TrackedShipments)
            {
                if (tracked != null && string.Equals(tracked.ShipmentId, order.ShipmentId, StringComparison.Ordinal))
                {
                    shipment = tracked;
                    break;
                }
            }

            if (shipment == null)
                return Reject($"FurniturePurchase: purchase '{order.PurchaseId}' shipment '{order.ShipmentId}' is not tracked by the freight service.");
            if (!freight.BeginLoading(shipment, loader, driveHooks, out string loadingReason))
                return Reject($"FurniturePurchase: purchase '{order.PurchaseId}' could not begin loading — {loadingReason}");
            int guard = 0;
            while (!shipment.loadApplied && guard++ < 20)
            {
                if (!freight.WorkLoading(shipment, 60, driveHooks, out string workReason))
                    return Reject($"FurniturePurchase: purchase '{order.PurchaseId}' loading failed — {workReason}");
            }

            if (!shipment.loadApplied)
                return Reject($"FurniturePurchase: purchase '{order.PurchaseId}' loading did not complete — the loader never finished.");
            if (!freight.Dispatch(shipment, driver, animals, driveHooks, out string dispatchReason))
                return Reject($"FurniturePurchase: purchase '{order.PurchaseId}' could not dispatch — {dispatchReason}");
            freight.AdvanceTransit(shipment, shipment.transitDurationGameSeconds, driveHooks);
            if (shipment.Status != LogisticsShipmentStatus.Arrived)
                return Reject($"FurniturePurchase: purchase '{order.PurchaseId}' did not arrive — shipment is {shipment.Status}.");
            if (!freight.BeginUnloading(shipment, loader, driveHooks, out string unloadReason))
                return Reject($"FurniturePurchase: purchase '{order.PurchaseId}' could not begin unloading — {unloadReason}");
            guard = 0;
            while (shipment.Status != LogisticsShipmentStatus.Completed && guard++ < 20)
            {
                if (!freight.WorkUnloading(shipment, 60, driveHooks, out string unloadWorkReason))
                    return Reject($"FurniturePurchase: purchase '{order.PurchaseId}' unloading failed — {unloadWorkReason}");
            }

            if (shipment.Status != LogisticsShipmentStatus.Completed)
                return Reject($"FurniturePurchase: purchase '{order.PurchaseId}' unloading did not complete — shipment is {shipment.Status}.");
            foreach (FurniturePurchaseLine line in order.Lines)
            {
                AdvanceLine(line);
            }

            order.Diagnostics.Add($"day {order.DayIndex}: purchase '{order.PurchaseId}' delivered — shipment '{order.ShipmentId}' completed, {order.ReceivedAssets.Count} asset(s) received.");
            return null;
        }

        /// <summary>
        /// Installs every received asset through the install handler (the
        /// B0 seam), exactly once per asset, only after receipt. A refusal
        /// stops the remaining installs loudly — already-installed assets
        /// stay installed, the purchase stays Received, nothing is ever
        /// marked installed that was not. On success the lines advance
        /// Received -> Installed -> Operational.
        /// </summary>
        public string TryInstall(FurniturePurchaseOrder order)
        {
            if (order == null) return Reject("FurniturePurchase: no purchase order supplied.");
            if (!order.AllLinesAt(EquipmentPurchaseStage.Received))
                return Reject($"FurniturePurchase: purchase '{order.PurchaseId}' is not fully Received — install follows receipt, never precedes it.");
            foreach (FurniturePurchaseLine line in order.Lines)
            {
                foreach (string assetId in line.AssetIds)
                {
                    EquipmentAsset asset = FindReceivedAsset(order, assetId);
                    if (asset == null)
                        return Reject($"FurniturePurchase: purchase '{order.PurchaseId}' asset '{assetId}' was received but cannot be found — install refused, purchase stays Received.");
                    string installRefusal = installHandler(asset, order.PurchaseId);
                    if (installRefusal != null)
                    {
                        return Reject($"FurniturePurchase: purchase '{order.PurchaseId}' asset '{assetId}' install refused — {installRefusal}; purchase stays Received.");
                    }
                }
            }

            foreach (FurniturePurchaseLine line in order.Lines)
            {
                AdvanceLine(line);
                AdvanceLine(line);
            }

            order.Diagnostics.Add($"day {order.DayIndex}: purchase '{order.PurchaseId}' installed and operational — {order.ReceivedAssets.Count} asset(s) in building {order.BuildingInstanceId}.");
            return null;
        }

        /// <summary>
        /// Advances one purchase line one stage forward, mirroring
        /// EquipmentPurchase.TryAdvance's forward-only rule (Quoted ->
        /// Ordered -> PaidOrFinanced -> Shipped -> Received -> Installed
        /// -> Operational). The advance lives here because
        /// FurniturePurchaseLine is a purpose-built line (units, source
        /// lot, unit price) and is not an EquipmentPurchaseLine, so
        /// TryAdvance cannot take it. The shared contract is the
        /// EquipmentPurchaseStage enum, not the line type.
        /// </summary>
        private static void AdvanceLine(FurniturePurchaseLine line)
        {
            if (line == null || line.Stage == EquipmentPurchaseStage.Operational) return;
            line.Stage = (EquipmentPurchaseStage)((int)line.Stage + 1);
        }

        private EquipmentAsset FindReceivedAsset(FurniturePurchaseOrder order, string assetId)
        {
            if (order.ReceivedAssets == null) return null;
            foreach (EquipmentAsset asset in order.ReceivedAssets)
            {
                if (asset != null && string.Equals(asset.AssetId, assetId, StringComparison.Ordinal)) return asset;
            }

            return null;
        }

        /// <summary>
        /// F3 CommitConsolidatedSourceLots for one purchase. New kind:
        /// check every line's lot covers, then debit each, rolling back
        /// on a later refusal — a shortfall moves no stock. Secondhand
        /// kind: verify every asset is still seller-owned and unreserved,
        /// then mark each committed — a sale that moved first refuses
        /// loudly. Idempotent per shipment.
        /// </summary>
        private bool CommitForOrder(FurniturePurchaseOrder order, LogisticsShipmentState shipment, out string reason)
        {
            reason = string.Empty;
            if (shipment == null)
            {
                reason = "FurniturePurchase: no shipment to commit.";
                return false;
            }

            if (committedShipments.Contains(shipment.ShipmentId)) return true;
            if (order.Kind == FurniturePurchaseKind.Secondhand)
            {
                foreach (FurniturePurchaseLine line in order.Lines)
                {
                    EquipmentAsset asset = line.ExistingAsset;
                    if (asset == null)
                    {
                        reason = $"FurniturePurchase: secondhand purchase '{order.PurchaseId}' line ({line.ItemId}) has no resolved asset — the sale cannot commit.";
                        return false;
                    }

                    if (asset.OwnerKind != HouseholdOwnerKind || asset.OwnerId != HouseholdOwnerId(order.SellerHouseholdId))
                    {
                        reason = $"FurniturePurchase: secondhand purchase '{order.PurchaseId}' asset '{asset.AssetId}' is no longer owned by the selling household {order.SellerHouseholdId} — refusing.";
                        return false;
                    }

                    if (asset.IsReserved)
                    {
                        reason = $"FurniturePurchase: secondhand purchase '{order.PurchaseId}' asset '{asset.AssetId}' is reserved — a reserved asset cannot be sold.";
                        return false;
                    }

                    if (committedAssets.Contains(asset.AssetId))
                    {
                        reason = $"FurniturePurchase: secondhand purchase '{order.PurchaseId}' asset '{asset.AssetId}' is already committed to another shipment.";
                        return false;
                    }
                }

                foreach (FurniturePurchaseLine line in order.Lines)
                {
                    committedAssets.Add(line.ExistingAsset.AssetId);
                }
            }
            else
            {
                foreach (FreightManifestLine manifestLine in shipment.ManifestLines)
                {
                    if (manifestLine == null) continue;
                    if (vendorStock.UnitsOnHand(manifestLine.LotId) < manifestLine.Units)
                    {
                        reason = $"FurniturePurchase: vendor stock shortfall on lot '{manifestLine.LotId}' — {vendorStock.UnitsOnHand(manifestLine.LotId)} on hand, {manifestLine.Units} needed.";
                        return false;
                    }
                }

                var debited = new List<FreightManifestLine>();
                foreach (FreightManifestLine manifestLine in shipment.ManifestLines)
                {
                    if (manifestLine == null) continue;
                    string debitRefusal = vendorStock.TryDebit(manifestLine.LotId, manifestLine.Units);
                    if (debitRefusal != null)
                    {
                        foreach (FreightManifestLine done in debited)
                        {
                            vendorStock.TryCredit(done.LotId, done.Units);
                        }

                        reason = $"FurniturePurchase: vendor stock debit refused for lot '{manifestLine.LotId}' — {debitRefusal}; earlier lines rolled back.";
                        return false;
                    }

                    debited.Add(manifestLine);
                }
            }

            committedShipments.Add(shipment.ShipmentId);
            return true;
        }

        /// <summary>
        /// F3 ReceiveConsolidatedCargoLots for one purchase. New kind:
        /// creates one EquipmentAsset per received unit (household-owned,
        /// provenance = source lot + purchase id + day; maker empty —
        /// O-F3 open). Secondhand kind: transfers each existing asset to
        /// the buyer (no asset created). A failed line fails the whole
        /// receipt loudly — the cargo stays in wagon custody. Idempotent
        /// per shipment: a duplicate receipt is refused, never credited
        /// twice.
        /// </summary>
        private bool ReceiveForOrder(FurniturePurchaseOrder order, LogisticsShipmentState shipment, out string reason)
        {
            reason = string.Empty;
            if (shipment == null)
            {
                reason = "FurniturePurchase: no shipment to receive.";
                return false;
            }

            if (receivedShipments.Contains(shipment.ShipmentId))
            {
                reason = "FurniturePurchase: duplicate consolidated shipment receipt refused.";
                return false;
            }

            var received = new List<EquipmentAsset>();
            if (order.Kind == FurniturePurchaseKind.Secondhand)
            {
                foreach (FurniturePurchaseLine line in order.Lines)
                {
                    EquipmentAsset asset = line.ExistingAsset;
                    if (asset == null)
                    {
                        reason = $"FurniturePurchase: secondhand purchase '{order.PurchaseId}' line ({line.ItemId}) has no resolved asset.";
                        return false;
                    }

                    if (asset.OwnerKind == HouseholdOwnerKind && asset.OwnerId == HouseholdOwnerId(order.HouseholdId))
                    {
                        received.Add(asset);
                        continue;
                    }

                    string transferRefusal = asset.TransferOwnership(HouseholdOwnerKind,
                        HouseholdOwnerId(order.HouseholdId), "furniture purchase " + order.PurchaseId, order.DayIndex);
                    if (transferRefusal != null)
                    {
                        reason = $"FurniturePurchase: secondhand purchase '{order.PurchaseId}' asset '{asset.AssetId}' transfer refused — {transferRefusal}.";
                        return false;
                    }

                    asset.LocationId = HouseholdCustodyLocationId(order.HouseholdId);
                    received.Add(asset);
                }
            }
            else
            {
                for (int i = 0; i < order.Lines.Count; i++)
                {
                    FurniturePurchaseLine line = order.Lines[i];
                    FurnitureItemSpecs.TryGetRow(line.ItemId, out FurnitureItemSpecs.Row row);
                    for (int u = 0; u < line.Units; u++)
                    {
                        received.Add(new EquipmentAsset
                        {
                            AssetId = "furn-" + order.PurchaseId.Trim() + "-" + i + "-" + u,
                            Kind = line.ItemId,
                            DisplayName = row != null ? row.DisplayName : line.ItemId,
                            Condition01 = 1f,
                            OwnerKind = HouseholdOwnerKind,
                            OwnerId = HouseholdOwnerId(order.HouseholdId),
                            LocationId = HouseholdCustodyLocationId(order.HouseholdId),
                            MadeByBusinessId = string.Empty,
                            MaterialLotIds = new List<string> { line.SourceLotId },
                            MadeDayIndex = order.DayIndex,
                        });
                    }
                }
            }

            receivedShipments.Add(shipment.ShipmentId);
            foreach (EquipmentAsset asset in received)
            {
                order.ReceivedAssets.Add(asset);
            }

            for (int i = 0; i < order.Lines.Count; i++)
            {
                FurniturePurchaseLine line = order.Lines[i];
                if (order.Kind == FurniturePurchaseKind.Secondhand)
                {
                    line.AssetIds.Add(line.ExistingAsset.AssetId);
                }
                else
                {
                    for (int u = 0; u < line.Units; u++)
                    {
                        line.AssetIds.Add("furn-" + order.PurchaseId.Trim() + "-" + i + "-" + u);
                    }
                }
            }

            return true;
        }

        private string Reject(string diagnostic)
        {
            diagnostics.Add(diagnostic);
            return diagnostic;
        }
    }
}
