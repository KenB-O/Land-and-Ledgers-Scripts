using System;
using System.Collections.Generic;
using LandLedgers.Economy.Businesses.GeneralStore;
using LandLedgers.Economy.Liabilities;
using LandLedgers.Economy.Trade;
using LandLedgers.Primitives;
using UnityEngine;

namespace LandLedgers.Economy.Businesses.LumberYard
{
    /// <summary>
    /// F1 (workstream F, glass leg): the lumber yard's window-glass
    /// import-order driver. Glass panes arrive by import — never from a
    /// local glassmaking industry, which does not exist in this pass —
    /// through the legitimate import authority (ImportService) and the
    /// registered material (LumberYardSupply.EnsureGlassImportable:
    /// "glass-panes", Pittsburgh glassworks via railhead, 220 mi, 14 days
    /// transit, 25c per pane). The driver owns one ImportService and
    /// drives it daily: it evaluates the yard's covered glass stock
    /// through the reorder policy, places ONE import order when the
    /// policy signal fires (cash from the yard's own business cash port,
    /// or honest credit through the liability ledger when cash is short),
    /// and receives due imports into the yard's glass stock with the
    /// order id and origin on the lot.
    ///
    /// No phantom merchant, no free glass, no stock appearing
    /// automatically: every glass pane in the yard traces to a paid
    /// import order (cash or a recorded payable) that spent 14 days in
    /// transit. An order refused for no cash and no credit leaves no
    /// order, no lot, and no payable behind.
    ///
    /// The driver is bound by the bootstrapper
    /// (FirstLedgerSliceBootstrapper.ConfigureLumberYardGlassImportWiring)
    /// and driven daily by SimulationDrivers.
    /// </summary>
    public sealed class LumberYardGlassImportDriver
    {
        private readonly LumberYardShopRuntime yard;
        private readonly List<string> diagnostics;
        private readonly ImportService importService = new ImportService();
        private readonly LumberYardGlassReorderPolicy policy = new LumberYardGlassReorderPolicy();

        /// <summary>
        /// F1: the yard's business cash port, bound by the production
        /// caller fresh each drive from the yard business's runtime
        /// state (the byproduct-transfer pattern). Null means no order
        /// can be paid in cash — the driver still tries honest credit,
        /// and refuses loudly when neither works.
        /// </summary>
        public IBusinessIdentityCashPort YardCash { get; set; }

        /// <summary>
        /// F1: the liability ledger, bound by the production caller
        /// fresh each drive from the simulation hub. Null means no
        /// credit path — cash-only orders.
        /// </summary>
        public BusinessLiabilityLedger LiabilityLedger { get; set; }

        /// <summary>
        /// F1: the yard's id registry, bound by the production caller
        /// fresh each drive from the simulation hub. The credit path
        /// needs it; without it, credit is unavailable.
        /// </summary>
        public EntityIdRegistry IdRegistry { get; set; }

        public LumberYardGlassImportDriver(LumberYardShopRuntime yard, List<string> diagnostics)
        {
            this.yard = yard;
            this.diagnostics = diagnostics ?? new List<string>();
            if (yard == null)
            {
                this.diagnostics.Add("LumberYardGlassImportDriver: no lumber yard runtime — glass imports refused.");
            }
            // The registered material is the driver's one input: without
            // it there is no legitimate import route, and the driver
            // must say so at construction instead of ordering into a
            // void. Registration is idempotent, so calling it here is
            // safe even when the bootstrapper already called it.
            LumberYardSupply.EnsureGlassImportable(this.diagnostics);
        }

        public LumberYardShopRuntime Yard => yard;
        public IReadOnlyList<string> Diagnostics => diagnostics;
        public ImportService ImportService => importService;
        public LumberYardGlassReorderPolicy Policy => policy;
        public IReadOnlyList<ImportOrder> Orders => importService.Orders;

        /// <summary>
        /// F1: the glass panes in outstanding (Ordered, not Delivered)
        /// import orders for this yard. The single-outstanding-order
        /// guard (the Millbrook pattern): the policy adds these to
        /// on-hand panes before deciding, so a covered stock never
        /// triggers a second order while the first is in transit.
        /// </summary>
        public int OutstandingGlassUnits
        {
            get
            {
                int outstanding = 0;
                foreach (ImportOrder order in importService.Orders)
                {
                    if (order == null) continue;
                    if (order.Status != ImportOrderStatus.Ordered) continue;
                    if (!string.Equals(order.MaterialId, LumberYardSupply.GlassImportMaterialId, StringComparison.Ordinal)) continue;
                    if (!string.Equals(order.BuyerBusinessId, yard != null ? yard.BusinessInstanceId : null, StringComparison.Ordinal)) continue;
                    outstanding += order.Units;
                }
                return outstanding;
            }
        }

        /// <summary>
        /// F1: one day's import work, in order: (1) receive due imports
        /// into the yard, (2) evaluate the covered-stock signal and
        /// place one order when it fires. Receiving first means a
        /// delivery landing today counts toward today's covered stock,
        /// so the driver never orders a second lot the same day a
        /// replenishment arrives. Returns a one-line summary for the
        /// daily drive log; never throws for a missing yard or a
        /// refused order — the refusal is the summary.
        /// </summary>
        public string Drive(int dayIndex)
        {
            if (yard == null)
            {
                string noYard = "LumberYardGlassImportDriver: no lumber yard runtime — no glass import work.";
                diagnostics.Add(noYard);
                return noYard;
            }

            int receivedUnits = ReceiveDueImports(dayIndex);
            LumberYardGlassReorderSignal signal = policy.Evaluate(yard, OutstandingGlassUnits);
            if (!signal.ShouldOrder)
            {
                string noOrder = $"LumberYardGlassImportDriver: received {receivedUnits} glass panes; {signal.Reason}";
                diagnostics.Add(noOrder);
                return noOrder;
            }

            ImportOrder order = PlaceGlassOrder(signal.OrderUnits, dayIndex);
            if (order == null)
            {
                string refused = $"LumberYardGlassImportDriver: received {receivedUnits} glass panes; {signal.Reason} Order refused — no cash and no credit; no order placed, no stock created.";
                diagnostics.Add(refused);
                return refused;
            }

            string placed = $"LumberYardGlassImportDriver: received {receivedUnits} glass panes; order {order.OrderId} placed for {order.Units} panes at {order.PricePerUnitCents}c "
                + $"({(order.PaidOnCredit ? "on credit" : "cash")}), arrives day {order.ExpectedArrivalDayIndex}.";
            diagnostics.Add(placed);
            return placed;
        }

        /// <summary>
        /// F1: places one glass import order through the import
        /// authority, paid from the yard's own business cash port or on
        /// honest credit. Returns the order, or null with the refusal in
        /// diagnostics. The yard's cash port must belong to the yard —
        /// a miswired port never pays for an order.
        /// </summary>
        public ImportOrder PlaceGlassOrder(int units, int dayIndex)
        {
            if (yard == null)
            {
                diagnostics.Add("LumberYardGlassImportDriver.PlaceGlassOrder: no lumber yard runtime — order refused.");
                return null;
            }
            if (YardCash != null && !string.Equals(YardCash.BusinessId, yard.BusinessInstanceId, StringComparison.Ordinal))
            {
                diagnostics.Add($"LumberYardGlassImportDriver.PlaceGlassOrder: the yard cash port belongs to '{YardCash.BusinessId}', not the yard '{yard.BusinessInstanceId}' — a miswired port never pays for an order.");
                return null;
            }
            return importService.PlaceOrderForBusiness(
                yard.BusinessInstanceId, yard.BusinessInstanceId,
                LumberYardSupply.GlassImportMaterialId, units, dayIndex,
                YardCash, LiabilityLedger, IdRegistry, diagnostics);
        }

        /// <summary>
        /// F1: pulls due imports from the service and receives each glass
        /// lot into the yard's glass stock, with the order id and origin
        /// on the lot. The yard's id registry is validated BEFORE
        /// pulling. Lane 9d (E R-08): intake-gated — the service calls the
        /// yard intake FIRST and only marks the order Delivered on success,
        /// so a refused lot stays Ordered and retryable on the next day's
        /// drive instead of the old Delivered-before-intake hazard (goods
        /// in no inventory, "UNWIND FAILED"). Returns the glass panes
        /// received.
        /// </summary>
        public int ReceiveDueImports(int dayIndex)
        {
            if (yard == null)
            {
                diagnostics.Add("LumberYardGlassImportDriver.ReceiveDueImports: no lumber yard runtime — nothing received.");
                return 0;
            }
            if (yard.GlassStock == null)
            {
                diagnostics.Add("LumberYardGlassImportDriver.ReceiveDueImports: the yard has no glass stock — nothing received; due imports stay Ordered and the next day's drive re-attempts them.");
                return 0;
            }
            if (yard.IdRegistry == null)
            {
                diagnostics.Add("LumberYardGlassImportDriver.ReceiveDueImports: the yard has no id registry — nothing received; due imports stay Ordered and the next day's drive re-attempts them.");
                return 0;
            }
            List<ImportLot> due = importService.ReceiveDueImportsWithIntake(dayIndex, lot =>
            {
                if (lot == null) return "no lot to receive.";
                if (!string.Equals(lot.MaterialId, LumberYardSupply.GlassImportMaterialId, StringComparison.Ordinal))
                    return $"import lot {lot.LotId} is material '{lot.MaterialId}', not glass-panes — this driver only receives glass.";
                ImportOrder order = FindOrder(lot.OrderId);
                int unitCostCents = order != null ? order.PricePerUnitCents : 0;
                return yard.ReceiveGlassImportArrival(
                    lot.Units, lot.OrderId, lot.OriginName,
                    dayIndex, unitCostCents, diagnostics);
            }, diagnostics);
            int receivedUnits = 0;
            foreach (ImportLot lot in due)
            {
                if (lot == null) continue;
                receivedUnits += lot.Units;
            }
            return receivedUnits;
        }

        private ImportOrder FindOrder(string orderId)
        {
            if (string.IsNullOrWhiteSpace(orderId)) return null;
            foreach (ImportOrder order in importService.Orders)
            {
                if (order == null) continue;
                if (string.Equals(order.OrderId, orderId, StringComparison.Ordinal)) return order;
            }
            return null;
        }

        // ---- Save contract (nested DTO, F6 driver pattern) ----

        [Serializable]
        public sealed class LumberYardGlassImportDriverSaveDto
        {
            public List<ImportOrder> ImportOrders = new List<ImportOrder>();
            public int NextOrderNumber = 1;
        }

        /// <summary>
        /// Deep copies the whole order book (Ordered AND Delivered
        /// orders — a Delivered order's lot already persists in the yard
        /// stock, but the order record is the provenance for that lot's
        /// order id, so it stays) plus the next order number, so a
        /// restored driver never reissues an id.
        /// </summary>
        public LumberYardGlassImportDriverSaveDto ToSaveDto()
        {
            var dto = new LumberYardGlassImportDriverSaveDto
            {
                NextOrderNumber = importService.NextOrderNumber,
            };
            foreach (ImportOrder order in importService.Orders)
            {
                if (order == null) continue;
                dto.ImportOrders.Add(new ImportOrder
                {
                    OrderId = order.OrderId,
                    BuyerBusinessId = order.BuyerBusinessId,
                    BuyerBusinessName = order.BuyerBusinessName,
                    MaterialId = order.MaterialId,
                    Units = order.Units,
                    PricePerUnitCents = order.PricePerUnitCents,
                    OrderDayIndex = order.OrderDayIndex,
                    ExpectedArrivalDayIndex = order.ExpectedArrivalDayIndex,
                    Status = order.Status,
                    OriginName = order.OriginName,
                    PaidOnCredit = order.PaidOnCredit,
                    LiabilityId = order.LiabilityId,
                });
            }
            return dto;
        }

        /// <summary>
        /// Clear-then-load for the order book, through
        /// ImportService.LoadOrders (which re-derives the next order
        /// number from the highest restored order id, so a restored
        /// service never reissues an id). A null DTO clears to no
        /// orders with the number reset to 1.
        /// </summary>
        public void LoadFromSaveDto(LumberYardGlassImportDriverSaveDto dto)
        {
            if (dto == null)
            {
                importService.LoadOrders(null, 1);
                return;
            }
            importService.LoadOrders(dto.ImportOrders, dto.NextOrderNumber);
        }
    }
}
