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
    /// D3 (F-D2-2): the lumber yard's nails import-order driver. Nails
    /// arrive by import — never from a local nail-making industry, which
    /// does not exist — through the legitimate import authority
    /// (ImportService) and the registered material
    /// (LumberYardSupply.EnsureHardwareImportable: "nails-hardware",
    /// Pittsburgh ironworks via railhead, 220 mi, 14 days transit,
    /// 30c per unit). The driver owns one ImportService and drives it
    /// daily: it evaluates the yard's covered nails stock through the
    /// reorder policy, places ONE import order when the policy signal
    /// fires (cash from the yard's own business cash port, or honest
    /// credit through the liability ledger when cash is short), and
    /// receives due imports into the yard's hardware stock with the
    /// order id and origin on the lot.
    ///
    /// No phantom merchant, no free nails, no stock appearing
    /// automatically: every nails unit in the yard traces to a paid
    /// import order (cash or a recorded payable) that spent 14 days in
    /// transit. An order refused for no cash and no credit leaves no
    /// order, no lot, and no payable behind.
    ///
    /// The driver is bound by the bootstrapper
    /// (FirstLedgerSliceBootstrapper.ConfigureLumberYardHardwareImportWiring)
    /// and driven daily by SimulationDrivers.
    /// </summary>
    public sealed class LumberYardHardwareImportDriver
    {
        private readonly LumberYardShopRuntime yard;
        private readonly List<string> diagnostics;
        private readonly ImportService importService = new ImportService();
        private readonly LumberYardHardwareReorderPolicy policy = new LumberYardHardwareReorderPolicy();

        /// <summary>
        /// D3: the yard's business cash port, bound by the production
        /// caller fresh each drive from the yard business's runtime
        /// state (the byproduct-transfer pattern). Null means no order
        /// can be paid in cash — the driver still tries honest credit,
        /// and refuses loudly when neither works.
        /// </summary>
        public IBusinessIdentityCashPort YardCash { get; set; }

        /// <summary>
        /// D3: the liability ledger, bound by the production caller
        /// fresh each drive from the simulation hub. Null means no
        /// credit path — cash-only orders.
        /// </summary>
        public BusinessLiabilityLedger LiabilityLedger { get; set; }

        /// <summary>
        /// D3: the yard's id registry, bound by the production caller
        /// fresh each drive from the simulation hub. The credit path
        /// needs it; without it, credit is unavailable.
        /// </summary>
        public EntityIdRegistry IdRegistry { get; set; }

        public LumberYardHardwareImportDriver(LumberYardShopRuntime yard, List<string> diagnostics)
        {
            this.yard = yard;
            this.diagnostics = diagnostics ?? new List<string>();
            if (yard == null)
            {
                this.diagnostics.Add("LumberYardHardwareImportDriver: no lumber yard runtime — nails imports refused.");
            }
            // The registered material is the driver's one input: without
            // it there is no legitimate import route, and the driver
            // must say so at construction instead of ordering into a
            // void. Registration is idempotent, so calling it here is
            // safe even when the bootstrapper already called it.
            LumberYardSupply.EnsureHardwareImportable(this.diagnostics);
        }

        public LumberYardShopRuntime Yard => yard;
        public IReadOnlyList<string> Diagnostics => diagnostics;
        public ImportService ImportService => importService;
        public LumberYardHardwareReorderPolicy Policy => policy;
        public IReadOnlyList<ImportOrder> Orders => importService.Orders;

        /// <summary>
        /// D3: the nails units in outstanding (Ordered, not Delivered)
        /// import orders for this yard. The single-outstanding-order
        /// guard (the Millbrook pattern): the policy adds these to
        /// on-hand units before deciding, so a covered stock never
        /// triggers a second order while the first is in transit.
        /// </summary>
        public int OutstandingNailsUnits
        {
            get
            {
                int outstanding = 0;
                foreach (ImportOrder order in importService.Orders)
                {
                    if (order == null) continue;
                    if (order.Status != ImportOrderStatus.Ordered) continue;
                    if (!string.Equals(order.MaterialId, LumberYardSupply.HardwareImportMaterialId, StringComparison.Ordinal)) continue;
                    if (!string.Equals(order.BuyerBusinessId, yard != null ? yard.BusinessInstanceId : null, StringComparison.Ordinal)) continue;
                    outstanding += order.Units;
                }
                return outstanding;
            }
        }

        /// <summary>
        /// D3: one day's import work, in order: (1) receive due imports
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
                string noYard = "LumberYardHardwareImportDriver: no lumber yard runtime — no nails import work.";
                diagnostics.Add(noYard);
                return noYard;
            }

            int receivedUnits = ReceiveDueImports(dayIndex);
            LumberYardHardwareReorderSignal signal = policy.Evaluate(yard, OutstandingNailsUnits);
            if (!signal.ShouldOrder)
            {
                string noOrder = $"LumberYardHardwareImportDriver: received {receivedUnits} nails units; {signal.Reason}";
                diagnostics.Add(noOrder);
                return noOrder;
            }

            ImportOrder order = PlaceNailsOrder(signal.OrderUnits, dayIndex);
            if (order == null)
            {
                string refused = $"LumberYardHardwareImportDriver: received {receivedUnits} nails units; {signal.Reason} Order refused — no cash and no credit; no order placed, no stock created.";
                diagnostics.Add(refused);
                return refused;
            }

            string placed = $"LumberYardHardwareImportDriver: received {receivedUnits} nails units; order {order.OrderId} placed for {order.Units} nails at {order.PricePerUnitCents}c "
                + $"({(order.PaidOnCredit ? "on credit" : "cash")}), arrives day {order.ExpectedArrivalDayIndex}.";
            diagnostics.Add(placed);
            return placed;
        }

        /// <summary>
        /// D3: places one nails import order through the import
        /// authority, paid from the yard's own business cash port or on
        /// honest credit. Returns the order, or null with the refusal in
        /// diagnostics. The yard's cash port must belong to the yard —
        /// a miswired port never pays for an order.
        /// </summary>
        public ImportOrder PlaceNailsOrder(int units, int dayIndex)
        {
            if (yard == null)
            {
                diagnostics.Add("LumberYardHardwareImportDriver.PlaceNailsOrder: no lumber yard runtime — order refused.");
                return null;
            }
            if (YardCash != null && !string.Equals(YardCash.BusinessId, yard.BusinessInstanceId, StringComparison.Ordinal))
            {
                diagnostics.Add($"LumberYardHardwareImportDriver.PlaceNailsOrder: the yard cash port belongs to '{YardCash.BusinessId}', not the yard '{yard.BusinessInstanceId}' — a miswired port never pays for an order.");
                return null;
            }
            return importService.PlaceOrderForBusiness(
                yard.BusinessInstanceId, yard.BusinessInstanceId,
                LumberYardSupply.HardwareImportMaterialId, units, dayIndex,
                YardCash, LiabilityLedger, IdRegistry, diagnostics);
        }

        /// <summary>
        /// D3: pulls due imports from the service and receives each nails
        /// lot into the yard's hardware stock, with the order id and
        /// origin on the lot. The yard's id registry is validated BEFORE
        /// pulling: ReceiveDueImports marks an order Delivered the
        /// moment it hands the lot over, so a lot pulled with nowhere to
        /// put it would be lost. A lot the yard intake refuses after
        /// that point is a loud reconciliation failure — the order is
        /// already Delivered, so the driver records the loss in
        /// diagnostics instead of silently dropping it. Returns the
        /// nails units received.
        /// </summary>
        public int ReceiveDueImports(int dayIndex)
        {
            if (yard == null)
            {
                diagnostics.Add("LumberYardHardwareImportDriver.ReceiveDueImports: no lumber yard runtime — nothing received.");
                return 0;
            }
            if (yard.HardwareStock == null)
            {
                diagnostics.Add("LumberYardHardwareImportDriver.ReceiveDueImports: the yard has no hardware stock — nothing received; due imports stay Ordered and the next day's drive re-attempts them.");
                return 0;
            }
            if (yard.IdRegistry == null)
            {
                diagnostics.Add("LumberYardHardwareImportDriver.ReceiveDueImports: the yard has no id registry — nothing received; due imports stay Ordered and the next day's drive re-attempts them.");
                return 0;
            }
            List<ImportLot> due = importService.ReceiveDueImports(dayIndex, diagnostics);
            int receivedUnits = 0;
            foreach (ImportLot lot in due)
            {
                if (lot == null) continue;
                if (!string.Equals(lot.MaterialId, LumberYardSupply.HardwareImportMaterialId, StringComparison.Ordinal))
                {
                    diagnostics.Add($"LumberYardHardwareImportDriver.ReceiveDueImports: import lot {lot.LotId} is material '{lot.MaterialId}', not nails-hardware — this driver only receives nails; the lot is left unreceived and the order stays Delivered in the service. Reconcile before the next drive.");
                    continue;
                }
                ImportOrder order = FindOrder(lot.OrderId);
                int unitCostCents = order != null ? order.PricePerUnitCents : 0;
                string refusal = yard.ReceiveHardwareImportArrival(
                    LumberYardHardwareKind.Nails, lot.Units, lot.OrderId, lot.OriginName,
                    dayIndex, unitCostCents, diagnostics);
                if (refusal != null)
                {
                    diagnostics.Add($"LumberYardHardwareImportDriver.ReceiveDueImports: UNWIND FAILED — nails lot {lot.LotId} ({lot.Units} units, order {lot.OrderId}) was marked Delivered by the import service but the yard intake refused it ({refusal}). The goods are not in any inventory. Reconcile the books and the order before the next drive.");
                    continue;
                }
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
        public sealed class LumberYardHardwareImportDriverSaveDto
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
        public LumberYardHardwareImportDriverSaveDto ToSaveDto()
        {
            var dto = new LumberYardHardwareImportDriverSaveDto
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
        public void LoadFromSaveDto(LumberYardHardwareImportDriverSaveDto dto)
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
