// Copyright (c) Land & Ledgers contributors. Licensed under the MIT License.
// Agent G, slice G-3 (expanded campaign 2026-11-16, W-G4 Workstream 3):
// the store side of the F4 purchase/install binding. F4's service
// (FurniturePurchaseService) drives a household furniture purchase end to
// end, but it had no production vendor stock, no production proceeds
// handler, and no production entry point — the leg failed before Quote.
// This file supplies them for the General Store:
//
// - StoreFurnitureVendorStock: the IFurnitureVendorStock over the store's
//   real runtime lots (category "furniture", F2). The debit is LOT-PINNED:
//   it debits the named lot (lot.TryConsume) and the category, so the
//   asset's provenance (MaterialLotIds) names the lot that was actually
//   debited — the item-level FIFO consume would draw from whichever lot
//   is first in list order and could misattribute provenance when the
//   order names a later lot. The price of the lot-pinned debit is the
//   store's daily COGS: no public lot-level COGS recording API exists on
//   BusinessRuntimeState (the COGS fields are private, updated only by
//   TryConsumeRetailItemStockUnits), so furniture sales through this
//   adapter do not feed LastDailyRetailCostOfGoodsSoldCents — recorded
//   as a follow-up (plan §9.9), not hidden. The credit is a loud-logging
//   refusal: no lot-level restore API exists (TryAddRetailItemLot refuses
//   duplicate lot ids, and a lot has no add method), so a rollback cannot
//   restore stock — unreachable by construction (F4's commit proves every
//   line's lot covers before debiting, single-threaded synchronous), and
//   the refusal is logged loudly rather than silently pretending.
// - FurnitureStorePurchase.TryBuildOrder: one order for a household's
//   want, lines built in FIFO order (oldest lots first) with per-lot
//   caps, priced at the live store selling price — never a guessed price,
//   never free inventory (a zero live price refuses).
// - FurnitureStorePurchase.TryQuoteDeliveryCharge: the C-6 cargo-aware
//   delivery quote for one furniture kind (FurnitureCargoSpecs + the
//   carrier's configured rate card + the production wagon's capacity).
// - FurnitureStorePurchaseDriver: one day's bindings (store, carrier,
//   freight, crew, scene seams, hooks template) driving one purchase per
//   household through the F4 service — Quote, Order, Pay, Ship, Deliver,
//   Install — plus the custody-piece install leg. The driver completes
//   each purchase within one day, so no mid-purchase state persists (F4
//   purchase records are runtime-only, a known F4 gap).

using System;
using System.Collections.Generic;
using LandLedgers.Economy;
using LandLedgers.Economy.Blacksmith;
using LandLedgers.Economy.Businesses.GeneralStore;
using LandLedgers.Economy.Freight;
using LandLedgers.Economy.Logistics;
using LandLedgers.Economy.Storage;
using LandLedgers.Population;
using LandLedgers.Primitives;
using LandLedgers.World;
using UnityEngine;

namespace LandLedgers.Economy.GenericBusiness
{
    /// <summary>
    /// G-3: the F4 vendor stock over the General Store's real runtime lots.
    /// UnitsOnHand answers the pre-check from the named lot; TryDebit
    /// debits the NAMED lot (lot-pinned, so the asset's provenance names
    /// the lot that was actually debited) plus the category; TryCredit is
    /// a loud-logging refusal (no lot-level restore API exists — see the
    /// file header). The debit refuses without moving stock when the lot
    /// does not hold the units or the category is short.
    /// </summary>
    public sealed class StoreFurnitureVendorStock : IFurnitureVendorStock
    {
        private readonly BusinessRuntimeState storeRuntime;
        private readonly List<string> diagnostics;

        public StoreFurnitureVendorStock(BusinessRuntimeState storeRuntime, List<string> diagnostics)
        {
            this.storeRuntime = storeRuntime;
            this.diagnostics = diagnostics;
        }

        public int UnitsOnHand(string lotId)
        {
            if (storeRuntime == null || string.IsNullOrWhiteSpace(lotId)) return 0;
            BusinessRetailItemLotState lot = storeRuntime.FindRetailItemLot(lotId.Trim());
            return lot != null ? lot.AvailableUnits : 0;
        }

        public string TryDebit(string lotId, int units)
        {
            if (storeRuntime == null)
                return "StoreFurnitureVendorStock: no store runtime — the goods come from real stock, never from nowhere.";
            if (string.IsNullOrWhiteSpace(lotId))
                return "StoreFurnitureVendorStock: a debit needs a lot id — every unit comes from a real lot.";
            if (units <= 0)
                return $"StoreFurnitureVendorStock: a debit needs a positive unit count, not {units}.";
            BusinessRetailItemLotState lot = storeRuntime.FindRetailItemLot(lotId.Trim());
            if (lot == null)
                return $"StoreFurnitureVendorStock: lot '{lotId}' is not a store lot — refusing a phantom debit.";
            if (lot.AvailableUnits < units)
                return $"StoreFurnitureVendorStock: lot '{lotId}' holds {lot.AvailableUnits} units, {units} needed — shortfall, nothing moved.";
            CategoryStockState category = storeRuntime.GetCategoryStock(lot.CategoryId);
            if (category == null || !category.TryConsumeStockUnits(units))
                return $"StoreFurnitureVendorStock: category '{lot.CategoryId}' cannot cover {units} units for lot '{lotId}' — nothing moved.";
            if (!lot.TryConsume(units, out int _, out bool _))
            {
                // Unreachable after the AvailableUnits check above in a
                // single-threaded synchronous call — but the category was
                // already debited, so restore it before refusing rather
                // than leaving the category short with the lot untouched.
                category.AddStockUnits(units);
                return $"StoreFurnitureVendorStock: lot '{lotId}' refused the debit after the category moved — category restored, nothing moved.";
            }
            return null;
        }

        public string TryCredit(string lotId, int units)
        {
            // No lot-level restore API exists: TryAddRetailItemLot refuses
            // duplicate lot ids, and a lot has no add method. F4's commit
            // proves every line's lot covers before debiting any line, so a
            // debit-then-refuse cannot happen in a single-threaded
            // synchronous call — this refusal is a loud record of an
            // unreachable rollback, never a silent loss. If a future caller
            // ever reaches it, the stock stays debited and the refusal
            // names the lot, so the discrepancy is visible and reconcilable.
            string refusal = $"StoreFurnitureVendorStock: rollback credit refused for lot '{lotId}' ({units} units) — no lot-level restore API exists; the stock stays debited and this must be reconciled, never silently re-added.";
            diagnostics?.Add(refusal);
            Debug.LogWarning(refusal);
            return refusal;
        }
    }

    /// <summary>
    /// G-3: the store side of the F4 purchase — order building, the
    /// delivery quote, the shipment acceptance, and the daily purchase
    /// driver. Unity-free except for Debug (the credit refusal log), so
    /// it is harness-compiled and harness-tested; the daily driver
    /// (SimulationDrivers) binds the scene and journey seams.
    /// </summary>
    public static class FurnitureStorePurchase
    {
        /// <summary>The F2 retail category for all eight first-wave furniture items.</summary>
        public const string FurnitureCategoryId = "furniture";

        /// <summary>
        /// The F4-test-shaped shipment acceptance for a purchase's
        /// shipment: every unloaded unit is accepted. The cargo IS the
        /// purchase's own furniture — already paid for, debited from the
        /// store, and received by the F4 service's ReceiveForOrder — and no
        /// inventory authority for EquipmentAssets exists that could
        /// refuse it, so accepting all units is the honest equivalent of
        /// the test double. One hooks object serves one purchase, so this
        /// never accepts another purchase's cargo.
        /// </summary>
        public static int AcceptAllShipmentUnits(LogisticsShipmentState shipment, int quantityUnits, out string reason)
        {
            reason = string.Empty;
            return Math.Max(0, quantityUnits);
        }

        /// <summary>
        /// The C-6 cargo-aware delivery quote for a furniture purchase:
        /// one trip, the kind's cargo spec (FurnitureCargoSpecs), the
        /// carrier's configured rate card, the production wagon's capacity,
        /// and the journey's road-condition multiplier. Mirrors
        /// LumberYardRetailDelivery.TryQuoteDeliveryCharge, with the
        /// furniture spec in place of the lumber spec. A refused quote is
        /// a refused delivery, never a guessed charge — including zero or
        /// negative miles (the store and the home are different buildings;
        /// a zero-mile route is a data defect, not a free delivery).
        /// </summary>
        public static bool TryQuoteDeliveryCharge(
            int units,
            string kind,
            float oneWayMiles,
            DevelopmentTownConfiguration.FreightServiceRateCard rateCard,
            EquipmentAsset wagon,
            float conditionMultiplier,
            out FreightServiceQuote quote,
            out string reason)
        {
            quote = null;
            reason = string.Empty;
            if (rateCard == null)
            {
                reason = "FurnitureStorePurchase: no freight rate card — the delivery charge is quoted from the freight business's configured card, never invented.";
                return false;
            }
            if (wagon == null)
            {
                reason = "FurnitureStorePurchase: no wagon — a delivery quote needs the production wagon's capacity.";
                return false;
            }
            FreightCargoSpec cargo = FurnitureCargoSpecs.SpecFor(kind);
            if (cargo == null)
            {
                reason = $"FurnitureStorePurchase: no cargo spec for '{kind}' — only first-wave furniture ships.";
                return false;
            }
            return FreightServiceQuoteCalculator.TryCalculateCargo(
                oneWayMiles,
                rateCard.baseServiceFeeCents,
                rateCard.ratePerMileCents,
                rateCard.chargeRoundTrip,
                units,
                cargo,
                wagon.PayloadMassCapacityKg,
                wagon.CargoVolumeCapacityM3,
                conditionMultiplier,
                out quote,
                out reason);
        }

        /// <summary>
        /// Builds one new-kind F4 order for a household's want: lines in
        /// FIFO order (the store's RetailItemLots list order — oldest lots
        /// first) with per-lot caps, priced at the live store selling
        /// price from the caller. The vendor id is the store runtime's own
        /// business id (the F4 service uses it as the shipment's source).
        /// Returns null on success, the refusal otherwise — a shortfall, an
        /// unknown kind, or a zero live price refuses before any stock
        /// moves (the order is only built, never committed, here).
        /// </summary>
        public static string TryBuildOrder(
            BusinessRuntimeState storeRuntime,
            Func<string, int> sellingPriceCentsForKind,
            string kind,
            int units,
            string purchaseId,
            int householdId,
            int buildingInstanceId,
            int dayIndex,
            int freightCents,
            out FurniturePurchaseOrder order)
        {
            order = null;
            if (storeRuntime == null)
                return "FurnitureStorePurchase: a purchase needs the store runtime — the goods come from real stock, never from nowhere.";
            if (sellingPriceCentsForKind == null)
                return "FurnitureStorePurchase: a purchase needs the live selling price — prices are read from the store, never guessed.";
            if (!FurnitureItemSpecs.TryGetRow(kind, out FurnitureItemSpecs.Row _))
                return $"FurnitureStorePurchase: unknown furniture item '{kind}' — only first-wave furniture is sold.";
            if (units <= 0)
                return $"FurnitureStorePurchase: a purchase of '{kind}' needs a positive unit count, not {units}.";
            if (string.IsNullOrWhiteSpace(purchaseId))
                return "FurnitureStorePurchase: a purchase needs an id — every purchase is identified before it is ordered.";
            if (householdId < 0 || buildingInstanceId < 0)
                return "FurnitureStorePurchase: a purchase needs a real buying household and delivery building.";
            if (freightCents < 0)
                return "FurnitureStorePurchase: the freight fare cannot be negative.";
            int unitPriceCents = sellingPriceCentsForKind(kind);
            if (unitPriceCents <= 0)
                return $"FurnitureStorePurchase: '{kind}' has no live selling price ({unitPriceCents}c) — free inventory is never granted.";
            var lines = new List<FurniturePurchaseLine>();
            int remaining = units;
            if (storeRuntime.RetailItemLots != null)
            {
                foreach (BusinessRetailItemLotState lot in storeRuntime.RetailItemLots)
                {
                    if (remaining <= 0) break;
                    if (lot == null) continue;
                    if (!string.Equals(lot.ItemId, kind, StringComparison.OrdinalIgnoreCase)) continue;
                    if (!string.Equals(lot.CategoryId, FurnitureCategoryId, StringComparison.OrdinalIgnoreCase)) continue;
                    if (lot.AvailableUnits <= 0) continue;
                    int take = Math.Min(remaining, lot.AvailableUnits);
                    lines.Add(new FurniturePurchaseLine
                    {
                        ItemId = kind,
                        Units = take,
                        SourceLotId = lot.LotId,
                        UnitPriceCents = unitPriceCents,
                    });
                    remaining -= take;
                }
            }
            if (remaining > 0)
                return $"FurnitureStorePurchase: store holds {units - remaining} of '{kind}' in the '{FurnitureCategoryId}' category, {units} needed — shortfall, no order built.";
            order = new FurniturePurchaseOrder
            {
                PurchaseId = purchaseId.Trim(),
                Kind = FurniturePurchaseKind.New,
                HouseholdId = householdId,
                VendorId = storeRuntime.BusinessId,
                VendorName = "General Store",
                BuildingInstanceId = buildingInstanceId,
                DayIndex = dayIndex,
                FreightCents = Math.Max(0, freightCents),
                Lines = lines,
            };
            return null;
        }
    }

    /// <summary>
    /// G-3: one day's store-purchase bindings, driving household furniture
    /// purchases through the F4 service. Constructed once per day by the
    /// daily driver with the day's fixed bindings (store, carrier cash,
    /// freight, wagon, crew, scene seams, hooks template); one purchase
    /// per household per day through TryPurchaseForHousehold, and the
    /// custody-piece install leg through TryInstallCustodyPieces. The
    /// hooks template is cloned per purchase (fresh diagnostics; the F4
    /// service binds its commit/receive closures on the clone, so one
    /// purchase's closures can never overwrite another's). The service
    /// instance is fresh per purchase, so purchase ids stay unique per
    /// service by construction and the deterministic purchase id
    /// ("furn-store-&lt;household&gt;-&lt;kind&gt;-&lt;day&gt;") never collides
    /// across households, kinds, or days.
    /// </summary>
    public sealed class FurnitureStorePurchaseDriver
    {
        private readonly BusinessRuntimeState storeRuntime;
        private readonly Func<string, int> sellingPriceCentsForKind;
        private readonly IBusinessCashPort carrierCash;
        private readonly FreightShipmentService freight;
        private readonly string wagonAssetId;
        private readonly EntityId loader;
        private readonly EntityId driver;
        private readonly List<EntityId> animals;
        private readonly Func<IEnumerable<BuildingEquipmentPlacement>> scenePlacements;
        private readonly Func<string, bool> placementIsBound;
        private readonly FreightShipmentHooks hooksTemplate;
        private readonly int dayIndex;

        public FurnitureStorePurchaseDriver(
            BusinessRuntimeState storeRuntime,
            Func<string, int> sellingPriceCentsForKind,
            IBusinessCashPort carrierCash,
            FreightShipmentService freight,
            string wagonAssetId,
            EntityId loader,
            EntityId driver,
            List<EntityId> animals,
            Func<IEnumerable<BuildingEquipmentPlacement>> scenePlacements,
            Func<string, bool> placementIsBound,
            FreightShipmentHooks hooksTemplate,
            int dayIndex)
        {
            this.storeRuntime = storeRuntime;
            this.sellingPriceCentsForKind = sellingPriceCentsForKind;
            this.carrierCash = carrierCash;
            this.freight = freight;
            this.wagonAssetId = wagonAssetId;
            this.loader = loader;
            this.driver = driver;
            this.animals = animals;
            this.scenePlacements = scenePlacements;
            this.placementIsBound = placementIsBound;
            this.hooksTemplate = hooksTemplate;
            this.dayIndex = dayIndex;
        }

        /// <summary>
        /// Drives one household's store purchase end to end through the F4
        /// service: Quote, Order, Pay, Ship, Deliver, Install. The goods
        /// money goes to the store through RecordRetailItemSettlement and
        /// the fare to the carrier through its cash port — carrier first,
        /// then store: the carrier Credit cannot refuse for a non-negative
        /// amount on a non-null runtime (Series2BusinessRuntimeCashPort),
        /// and the settlement is void and cannot refuse, so both-or-neither
        /// holds in practice; a refusal anywhere else refunds the buyer in
        /// full through the service and the purchase stays at its stage.
        /// Returns the operational order on success, null on refusal (the
        /// reason is appended to the diagnostics).
        /// </summary>
        public FurniturePurchaseOrder TryPurchaseForHousehold(
            HouseholdLedger ledger,
            HouseholdFurnishingRegister register,
            int householdId,
            int buildingInstanceId,
            BuildingFootprintAuthority building,
            string kind,
            int freightCents,
            List<string> diag)
        {
            diag = diag ?? new List<string>();
            if (ledger == null || register == null || building == null)
            {
                diag.Add("FurnitureStorePurchase: a purchase needs a buyer ledger, a buyer register, and a building — refusing a purchase for nobody, nowhere.");
                return null;
            }
            if (storeRuntime == null || freight == null || carrierCash == null)
            {
                diag.Add("FurnitureStorePurchase: a purchase needs the store runtime, the freight service, and the carrier cash port — refusing a purchase with no seller, no carrier, or no freight.");
                return null;
            }
            if (string.IsNullOrWhiteSpace(wagonAssetId))
            {
                diag.Add("FurnitureStorePurchase: a purchase needs a real wagon — furniture travels by wagon, never by fiat.");
                return null;
            }
            string purchaseId = "furn-store-" + householdId + "-" + (kind ?? string.Empty).Trim() + "-" + dayIndex;
            string buildRefusal = FurnitureStorePurchase.TryBuildOrder(storeRuntime, sellingPriceCentsForKind,
                kind, 1, purchaseId, householdId, buildingInstanceId, dayIndex, freightCents, out FurniturePurchaseOrder order);
            if (buildRefusal != null)
            {
                diag.Add(buildRefusal);
                return null;
            }
            var placements = new List<BuildingEquipmentPlacement>();
            if (scenePlacements != null)
            {
                foreach (BuildingEquipmentPlacement placement in scenePlacements())
                {
                    if (placement != null) placements.Add(placement);
                }
            }
            var handler = new FurniturePurchaseInstallHandler(register, building, buildingInstanceId,
                placements, placementIsBound, FurnishingAcquisitionSource.Purchase, null, diag);
            int totalUnits = 0;
            foreach (FurniturePurchaseLine line in order.Lines)
            {
                if (line != null) totalUnits += Math.Max(0, line.Units);
            }
            CreditPurchaseProceedsHandler creditProceeds = (int goodsCents, int fareCents, string id) =>
            {
                string carrierRefusal = carrierCash.Credit(fareCents,
                    "furniture purchase " + id + " freight");
                if (carrierRefusal != null) return carrierRefusal;
                storeRuntime.RecordRetailItemSettlement(FurnitureStorePurchase.FurnitureCategoryId, totalUnits, goodsCents);
                return null;
            };
            var service = new FurniturePurchaseService(ledger,
                new StoreFurnitureVendorStock(storeRuntime, diag), creditProceeds, handler.Install, null);
            FreightShipmentHooks hooks = CloneHooks();
            string refusal = service.Quote(order)
                ?? service.TryOrder(order)
                ?? service.TryPay(order)
                ?? service.TryShip(order, freight, wagonAssetId, hooks)
                ?? service.TryDeliver(order, freight, hooks, loader, driver, animals)
                ?? service.TryInstall(order);
            diag.AddRange(order.Diagnostics);
            diag.AddRange(hooks.Diagnostics ?? new List<string>());
            if (refusal != null)
            {
                diag.Add($"FurnitureStorePurchase: household {householdId} purchase '{purchaseId}' ({kind}) refused — {refusal}");
                return null;
            }
            diag.Add($"FurnitureStorePurchase: day {dayIndex}: household {householdId} bought 1x '{kind}' from the General Store for {order.GoodsTotalCents()}c + {Math.Max(0, freightCents)}c freight, installed in building {buildingInstanceId} — purchase '{purchaseId}' operational.");
            return order;
        }

        /// <summary>
        /// Installs the household's workshop-made custody pieces (the G-1
        /// sale's post-sale state) in its building. Returns the installed
        /// count; refusals are recorded loudly in the diagnostics.
        /// </summary>
        public int TryInstallCustodyPieces(
            HouseholdFurnishingRegister register,
            int buildingInstanceId,
            BuildingFootprintAuthority building,
            List<string> diag)
        {
            diag = diag ?? new List<string>();
            var placements = new List<BuildingEquipmentPlacement>();
            if (scenePlacements != null)
            {
                foreach (BuildingEquipmentPlacement placement in scenePlacements())
                {
                    if (placement != null) placements.Add(placement);
                }
            }
            return FurnitureInstallBinding.TryInstallCustodyPieces(register, building, buildingInstanceId,
                placements, placementIsBound, diag);
        }

        private FreightShipmentHooks CloneHooks()
        {
            var hooks = new FreightShipmentHooks
            {
                DayIndex = dayIndex,
                Diagnostics = new List<string>(),
                ReceiveShipment = FurnitureStorePurchase.AcceptAllShipmentUnits,
            };
            if (hooksTemplate != null)
            {
                hooks.Tasks = hooksTemplate.Tasks;
                hooks.Budgets = hooksTemplate.Budgets;
                hooks.Reservations = hooksTemplate.Reservations;
                hooks.NowGameSeconds = hooksTemplate.NowGameSeconds;
                hooks.Schedules = hooksTemplate.Schedules;
                hooks.MinuteOfDay = hooksTemplate.MinuteOfDay;
            }
            return hooks;
        }
    }
}
