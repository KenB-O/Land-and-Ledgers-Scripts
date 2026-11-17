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
    /// D5 (D4 follow-up): the smith's forge-coal import-order driver.
    /// Coal arrives by import — never from a local coal industry,
    /// which does not exist, and never from the fuel dealer's yard
    /// wood (FuelYardProfile keeps the forge on the EQU-1 import
    /// path) — through the legitimate import authority (ImportService)
    /// and the registered material (ImportCatalog: "forge-coal",
    /// off-map coal dealer via railhead, 180 mi, 12 days transit, 18c
    /// per unit). The driver owns one ImportService and drives it
    /// daily: it evaluates the smith's covered coal stock through the
    /// reorder policy, places ONE import order when the policy signal
    /// fires (cash from the smith's own business cash port, or honest
    /// credit through the liability ledger when cash is short), and
    /// receives due imports into the smith's material stock with the
    /// order id and origin on the lot.
    ///
    /// No phantom merchant, no free coal, no stock appearing
    /// automatically: every coal unit in the smithy traces to a paid
    /// import order (cash or a recorded payable) that spent 12 days in
    /// transit. An order refused for no cash and no credit leaves no
    /// order, no lot, and no payable behind.
    ///
    /// The driver is bound by the bootstrapper
    /// (FirstLedgerSliceBootstrapper.ConfigureBlacksmithCoalImportWiring)
    /// and driven daily by SimulationDrivers.
    /// </summary>
    public sealed class BlacksmithCoalImportDriver
    {
        private readonly BlacksmithRuntime smithy;
        private readonly List<string> diagnostics;
        private readonly ImportService importService = new ImportService();
        private readonly BlacksmithCoalReorderPolicy policy = new BlacksmithCoalReorderPolicy();

        /// <summary>
        /// D5: the smith's business cash port, bound by the production
        /// caller fresh each drive from the smith business's runtime
        /// state (the byproduct-transfer pattern). Null means no order
        /// can be paid in cash — the driver still tries honest credit,
        /// and refuses loudly when neither works.
        /// </summary>
        public IBusinessIdentityCashPort SmithCash { get; set; }

        /// <summary>
        /// D5: the liability ledger, bound by the production caller
        /// fresh each drive from the simulation hub. Null means no
        /// credit path — cash-only orders.
        /// </summary>
        public BusinessLiabilityLedger LiabilityLedger { get; set; }

        /// <summary>
        /// D5: the smith's id registry, bound by the production caller
        /// fresh each drive from the simulation hub. The credit path
        /// needs it; without it, credit is unavailable.
        /// </summary>
        public EntityIdRegistry IdRegistry { get; set; }

        public BlacksmithCoalImportDriver(BlacksmithRuntime smithy, List<string> diagnostics)
        {
            this.smithy = smithy;
            this.diagnostics = diagnostics ?? new List<string>();
            if (smithy == null)
            {
                this.diagnostics.Add("BlacksmithCoalImportDriver: no blacksmith runtime — coal imports refused.");
            }
            // The registered material is the driver's one input: without
            // it there is no legitimate import route, and the driver
            // must say so at construction instead of ordering into a
            // void. Forge coal is registered by ImportCatalog's static
            // constructor ("forge-coal", off-map coal dealer via
            // railhead), so this is a defensive assertion, not a
            // registration — the driver never invents a supplier.
            if (ImportCatalog.Get(ImportCatalog.ForgeCoalId) == null)
            {
                this.diagnostics.Add("BlacksmithCoalImportDriver: forge-coal is not in the import catalog — no legitimate coal route exists; coal imports refused.");
            }
        }

        public BlacksmithRuntime Smithy => smithy;
        public IReadOnlyList<string> Diagnostics => diagnostics;
        public ImportService ImportService => importService;
        public BlacksmithCoalReorderPolicy Policy => policy;
        public IReadOnlyList<ImportOrder> Orders => importService.Orders;

        /// <summary>
        /// D5: the coal units in outstanding (Ordered, not Delivered)
        /// import orders for this smithy. The single-outstanding-order
        /// guard (the Millbrook pattern): the policy adds these to
        /// on-hand units before deciding, so a covered stock never
        /// triggers a second order while the first is in transit.
        /// </summary>
        public int OutstandingCoalUnits
        {
            get
            {
                int outstanding = 0;
                foreach (ImportOrder order in importService.Orders)
                {
                    if (order == null) continue;
                    if (order.Status != ImportOrderStatus.Ordered) continue;
                    if (!string.Equals(order.MaterialId, ImportCatalog.ForgeCoalId, StringComparison.Ordinal)) continue;
                    if (!string.Equals(order.BuyerBusinessId, smithy != null ? smithy.BusinessInstanceId : null, StringComparison.Ordinal)) continue;
                    outstanding += order.Units;
                }
                return outstanding;
            }
        }

        /// <summary>
        /// D5: one day's import work, in order: (1) receive due imports
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
                string noSmithy = "BlacksmithCoalImportDriver: no blacksmith runtime — no coal import work.";
                diagnostics.Add(noSmithy);
                return noSmithy;
            }

            int receivedUnits = ReceiveDueImports(dayIndex);
            BlacksmithCoalReorderSignal signal = policy.Evaluate(smithy, OutstandingCoalUnits);
            if (!signal.ShouldOrder)
            {
                string noOrder = $"BlacksmithCoalImportDriver: received {receivedUnits} coal units; {signal.Reason}";
                diagnostics.Add(noOrder);
                return noOrder;
            }

            ImportOrder order = PlaceCoalOrder(signal.OrderUnits, dayIndex);
            if (order == null)
            {
                string refused = $"BlacksmithCoalImportDriver: received {receivedUnits} coal units; {signal.Reason} Order refused — no cash and no credit; no order placed, no stock created.";
                diagnostics.Add(refused);
                return refused;
            }

            string placed = $"BlacksmithCoalImportDriver: received {receivedUnits} coal units; order {order.OrderId} placed for {order.Units} coal at {order.PricePerUnitCents}c "
                + $"({(order.PaidOnCredit ? "on credit" : "cash")}), arrives day {order.ExpectedArrivalDayIndex}.";
            diagnostics.Add(placed);
            return placed;
        }

        /// <summary>
        /// D5: places one coal import order through the import
        /// authority, paid from the smith's own business cash port or on
        /// honest credit. Returns the order, or null with the refusal in
        /// diagnostics. The smith's cash port must belong to the smithy —
        /// a miswired port never pays for an order.
        /// </summary>
        public ImportOrder PlaceCoalOrder(int units, int dayIndex)
        {
            if (smithy == null)
            {
                diagnostics.Add("BlacksmithCoalImportDriver.PlaceCoalOrder: no blacksmith runtime — order refused.");
                return null;
            }
            if (SmithCash != null && !string.Equals(SmithCash.BusinessId, smithy.BusinessInstanceId, StringComparison.Ordinal))
            {
                diagnostics.Add($"BlacksmithCoalImportDriver.PlaceCoalOrder: the smith cash port belongs to '{SmithCash.BusinessId}', not the smithy '{smithy.BusinessInstanceId}' — a miswired port never pays for an order.");
                return null;
            }
            return importService.PlaceOrderForBusiness(
                smithy.BusinessInstanceId, smithy.BusinessName,
                ImportCatalog.ForgeCoalId, units, dayIndex,
                SmithCash, LiabilityLedger, IdRegistry, diagnostics);
        }

        /// <summary>
        /// D5: pulls due imports from the service and receives each coal
        /// lot into the smith's material stock, with the order id and
        /// origin on the lot. The smithy is validated BEFORE pulling:
        /// ReceiveDueImports marks an order Delivered the moment it
        /// hands the lot over, so a lot pulled with nowhere to put it
        /// would be lost. BlacksmithRuntime.ReceiveMaterial never
        /// refuses a well-formed lot, so a pulled coal lot always
        /// lands; a lot of another material is a loud reconciliation
        /// failure — this driver only receives coal, the lot is left
        /// unreceived and the order stays Delivered in the service.
        /// Returns the coal units received.
        /// </summary>
        public int ReceiveDueImports(int dayIndex)
        {
            if (smithy == null)
            {
                diagnostics.Add("BlacksmithCoalImportDriver.ReceiveDueImports: no blacksmith runtime — nothing received.");
                return 0;
            }
            List<ImportLot> due = importService.ReceiveDueImports(dayIndex, diagnostics);
            int receivedUnits = 0;
            foreach (ImportLot lot in due)
            {
                if (lot == null) continue;
                if (!string.Equals(lot.MaterialId, ImportCatalog.ForgeCoalId, StringComparison.Ordinal))
                {
                    diagnostics.Add($"BlacksmithCoalImportDriver.ReceiveDueImports: import lot {lot.LotId} is material '{lot.MaterialId}', not forge-coal — this driver only receives coal; the lot is left unreceived and the order stays Delivered in the service. Reconcile before the next drive.");
                    continue;
                }
                smithy.ReceiveMaterial(lot, diagnostics);
                receivedUnits += lot.Units;
            }
            return receivedUnits;
        }

        // ---- Save contract (nested DTO, F6 driver pattern) ----

        [Serializable]
        public sealed class BlacksmithCoalImportDriverSaveDto
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
        public BlacksmithCoalImportDriverSaveDto ToSaveDto()
        {
            var dto = new BlacksmithCoalImportDriverSaveDto
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
        public void LoadFromSaveDto(BlacksmithCoalImportDriverSaveDto dto)
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
