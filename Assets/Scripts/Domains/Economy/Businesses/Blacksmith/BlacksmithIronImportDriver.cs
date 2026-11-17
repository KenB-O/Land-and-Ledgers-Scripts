using System;
using System.Collections.Generic;
using LandLedgers.Economy.Businesses.GeneralStore;
using LandLedgers.Economy.Liabilities;
using LandLedgers.Economy.Trade;
using LandLedgers.Primitives;
using UnityEngine;

namespace LandLedgers.Economy.Blacksmith
{
    /// <summary>
    /// D4 (F-D2-3): the smith's iron import-order driver. Iron arrives
    /// by import — never from a local iron industry, which does not
    /// exist — through the legitimate import authority (ImportService)
    /// and the registered material (ImportCatalog: "iron-stock",
    /// Pittsburgh ironworks via railhead, 220 mi, 14 days transit, 45c
    /// per unit). The driver owns one ImportService and drives it
    /// daily: it evaluates the smith's covered iron stock through the
    /// reorder policy, places ONE import order when the policy signal
    /// fires (cash from the smith's own business cash port, or honest
    /// credit through the liability ledger when cash is short), and
    /// receives due imports into the smith's material stock with the
    /// order id and origin on the lot.
    ///
    /// No phantom merchant, no free iron, no stock appearing
    /// automatically: every iron unit in the smithy traces to a paid
    /// import order (cash or a recorded payable) that spent 14 days in
    /// transit. An order refused for no cash and no credit leaves no
    /// order, no lot, and no payable behind.
    ///
    /// The driver is bound by the bootstrapper
    /// (FirstLedgerSliceBootstrapper.ConfigureBlacksmithIronImportWiring)
    /// and driven daily by SimulationDrivers.
    /// </summary>
    public sealed class BlacksmithIronImportDriver
    {
        private readonly BlacksmithRuntime smithy;
        private readonly List<string> diagnostics;
        private readonly ImportService importService = new ImportService();
        private readonly BlacksmithIronReorderPolicy policy = new BlacksmithIronReorderPolicy();

        /// <summary>
        /// D4: the smith's business cash port, bound by the production
        /// caller fresh each drive from the smith business's runtime
        /// state (the byproduct-transfer pattern). Null means no order
        /// can be paid in cash — the driver still tries honest credit,
        /// and refuses loudly when neither works.
        /// </summary>
        public IBusinessIdentityCashPort SmithCash { get; set; }

        /// <summary>
        /// D4: the liability ledger, bound by the production caller
        /// fresh each drive from the simulation hub. Null means no
        /// credit path — cash-only orders.
        /// </summary>
        public BusinessLiabilityLedger LiabilityLedger { get; set; }

        /// <summary>
        /// D4: the smith's id registry, bound by the production caller
        /// fresh each drive from the simulation hub. The credit path
        /// needs it; without it, credit is unavailable.
        /// </summary>
        public EntityIdRegistry IdRegistry { get; set; }

        public BlacksmithIronImportDriver(BlacksmithRuntime smithy, List<string> diagnostics)
        {
            this.smithy = smithy;
            this.diagnostics = diagnostics ?? new List<string>();
            if (smithy == null)
            {
                this.diagnostics.Add("BlacksmithIronImportDriver: no blacksmith runtime — iron imports refused.");
            }
            // The registered material is the driver's one input: without
            // it there is no legitimate import route, and the driver
            // must say so at construction instead of ordering into a
            // void. Iron stock is registered by ImportCatalog's static
            // constructor ("iron-stock", Pittsburgh ironworks via
            // railhead), so this is a defensive assertion, not a
            // registration — the driver never invents a supplier.
            if (ImportCatalog.Get(ImportCatalog.IronStockId) == null)
            {
                this.diagnostics.Add("BlacksmithIronImportDriver: iron-stock is not in the import catalog — no legitimate iron route exists; iron imports refused.");
            }
        }

        public BlacksmithRuntime Smithy => smithy;
        public IReadOnlyList<string> Diagnostics => diagnostics;
        public ImportService ImportService => importService;
        public BlacksmithIronReorderPolicy Policy => policy;
        public IReadOnlyList<ImportOrder> Orders => importService.Orders;

        /// <summary>
        /// D4: the iron units in outstanding (Ordered, not Delivered)
        /// import orders for this smithy. The single-outstanding-order
        /// guard (the Millbrook pattern): the policy adds these to
        /// on-hand units before deciding, so a covered stock never
        /// triggers a second order while the first is in transit.
        /// </summary>
        public int OutstandingIronUnits
        {
            get
            {
                int outstanding = 0;
                foreach (ImportOrder order in importService.Orders)
                {
                    if (order == null) continue;
                    if (order.Status != ImportOrderStatus.Ordered) continue;
                    if (!string.Equals(order.MaterialId, ImportCatalog.IronStockId, StringComparison.Ordinal)) continue;
                    if (!string.Equals(order.BuyerBusinessId, smithy != null ? smithy.BusinessInstanceId : null, StringComparison.Ordinal)) continue;
                    outstanding += order.Units;
                }
                return outstanding;
            }
        }

        /// <summary>
        /// D4: one day's import work, in order: (1) receive due imports
        /// into the smithy, (2) evaluate the covered-stock signal and
        /// place one order when it fires. Receiving first means a
        /// delivery landing today counts toward today's covered stock,
        /// so the driver never orders a second lot the same day a
        /// replenishment arrives. Returns a one-line summary for the
        /// daily drive log; never throws for a missing smithy or a
        /// refused order — the refusal is the summary.
        /// </summary>
        public string Drive(int dayIndex)
        {
            if (smithy == null)
            {
                string noSmithy = "BlacksmithIronImportDriver: no blacksmith runtime — no iron import work.";
                diagnostics.Add(noSmithy);
                return noSmithy;
            }

            int receivedUnits = ReceiveDueImports(dayIndex);
            BlacksmithIronReorderSignal signal = policy.Evaluate(smithy, OutstandingIronUnits);
            if (!signal.ShouldOrder)
            {
                string noOrder = $"BlacksmithIronImportDriver: received {receivedUnits} iron units; {signal.Reason}";
                diagnostics.Add(noOrder);
                return noOrder;
            }

            ImportOrder order = PlaceIronOrder(signal.OrderUnits, dayIndex);
            if (order == null)
            {
                string refused = $"BlacksmithIronImportDriver: received {receivedUnits} iron units; {signal.Reason} Order refused — no cash and no credit; no order placed, no stock created.";
                diagnostics.Add(refused);
                return refused;
            }

            string placed = $"BlacksmithIronImportDriver: received {receivedUnits} iron units; order {order.OrderId} placed for {order.Units} iron at {order.PricePerUnitCents}c "
                + $"({(order.PaidOnCredit ? "on credit" : "cash")}), arrives day {order.ExpectedArrivalDayIndex}.";
            diagnostics.Add(placed);
            return placed;
        }

        /// <summary>
        /// D4: places one iron import order through the import
        /// authority, paid from the smith's own business cash port or on
        /// honest credit. Returns the order, or null with the refusal in
        /// diagnostics. The smith's cash port must belong to the smithy —
        /// a miswired port never pays for an order.
        /// </summary>
        public ImportOrder PlaceIronOrder(int units, int dayIndex)
        {
            if (smithy == null)
            {
                diagnostics.Add("BlacksmithIronImportDriver.PlaceIronOrder: no blacksmith runtime — order refused.");
                return null;
            }
            if (SmithCash != null && !string.Equals(SmithCash.BusinessId, smithy.BusinessInstanceId, StringComparison.Ordinal))
            {
                diagnostics.Add($"BlacksmithIronImportDriver.PlaceIronOrder: the smith cash port belongs to '{SmithCash.BusinessId}', not the smithy '{smithy.BusinessInstanceId}' — a miswired port never pays for an order.");
                return null;
            }
            return importService.PlaceOrderForBusiness(
                smithy.BusinessInstanceId, smithy.BusinessName,
                ImportCatalog.IronStockId, units, dayIndex,
                SmithCash, LiabilityLedger, IdRegistry, diagnostics);
        }

        /// <summary>
        /// D4: pulls due imports from the service and receives each iron
        /// lot into the smith's material stock, with the order id and
        /// origin on the lot. The smithy is validated BEFORE pulling:
        /// ReceiveDueImports marks an order Delivered the moment it
        /// hands the lot over, so a lot pulled with nowhere to put it
        /// would be lost. BlacksmithRuntime.ReceiveMaterial never
        /// refuses a well-formed lot, so a pulled iron lot always
        /// lands; a lot of another material is a loud reconciliation
        /// failure — this driver only receives iron, the lot is left
        /// unreceived and the order stays Delivered in the service.
        /// Returns the iron units received.
        /// </summary>
        public int ReceiveDueImports(int dayIndex)
        {
            if (smithy == null)
            {
                diagnostics.Add("BlacksmithIronImportDriver.ReceiveDueImports: no blacksmith runtime — nothing received.");
                return 0;
            }
            List<ImportLot> due = importService.ReceiveDueImports(dayIndex, diagnostics);
            int receivedUnits = 0;
            foreach (ImportLot lot in due)
            {
                if (lot == null) continue;
                if (!string.Equals(lot.MaterialId, ImportCatalog.IronStockId, StringComparison.Ordinal))
                {
                    diagnostics.Add($"BlacksmithIronImportDriver.ReceiveDueImports: import lot {lot.LotId} is material '{lot.MaterialId}', not iron-stock — this driver only receives iron; the lot is left unreceived and the order stays Delivered in the service. Reconcile before the next drive.");
                    continue;
                }
                smithy.ReceiveMaterial(lot, diagnostics);
                receivedUnits += lot.Units;
            }
            return receivedUnits;
        }

        // ---- Save contract (nested DTO, F6 driver pattern) ----

        [Serializable]
        public sealed class BlacksmithIronImportDriverSaveDto
        {
            public List<ImportOrder> ImportOrders = new List<ImportOrder>();
            public int NextOrderNumber = 1;
        }

        /// <summary>
        /// Deep copies the whole order book (Ordered AND Delivered
        /// orders — a Delivered order's lot already persists in the
        /// smithy stock, but the order record is the provenance for
        /// that lot's order id, so it stays) plus the next order
        /// number, so a restored driver never reissues an id.
        /// </summary>
        public BlacksmithIronImportDriverSaveDto ToSaveDto()
        {
            var dto = new BlacksmithIronImportDriverSaveDto
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
        public void LoadFromSaveDto(BlacksmithIronImportDriverSaveDto dto)
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
