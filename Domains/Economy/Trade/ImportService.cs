using System;
using System.Collections.Generic;

namespace LandLedgers.Economy.Trade
{
    /// <summary>Lifecycle of one import order.</summary>
    public enum ImportOrderStatus
    {
        Unspecified = 0,
        Ordered = 1,    // paid (cash or credit), in transit
        Delivered = 2,  // lots received
    }

    /// <summary>
    /// EQU-1: one placed import order. Every unit is ordered, paid, and delivered —
    /// there is no standing import drip. Payment is either cash (payer ledger outflow
    /// with provenance) or honest credit (a payable on the SWN-3 liability ledger).
    /// </summary>
    [Serializable]
    public sealed class ImportOrder
    {
        public string OrderId = string.Empty;
        public string BuyerBusinessId = string.Empty;
        public string BuyerBusinessName = string.Empty;
        public string MaterialId = string.Empty;
        public int Units;
        public int PricePerUnitCents;
        public int TotalCents => Units * PricePerUnitCents;
        public int OrderDayIndex;
        public int ExpectedArrivalDayIndex;
        public ImportOrderStatus Status = ImportOrderStatus.Unspecified;
        public string OriginName = string.Empty;
        public bool PaidOnCredit;
        public string LiabilityId = string.Empty; // when PaidOnCredit
    }

    /// <summary>
    /// EQU-1: a delivered import lot. Carries the import provenance permanently:
    /// named off-map origin + order id. Downstream crafting (EQU-2 blacksmith)
    /// inherits this provenance into finished equipment.
    /// </summary>
    [Serializable]
    public sealed class ImportLot
    {
        public string LotId = string.Empty; // deterministic: IMP-{orderId}-{n}
        public string MaterialId = string.Empty;
        public string MaterialName = string.Empty;
        public int Units;
        public string OriginName = string.Empty;
        public string OrderId = string.Empty;
        public int ArrivalDayIndex;
        public bool Imported = true;
    }

    /// <summary>
    /// EQU-1: places and receives off-map import orders. Kennedy-authorized: a full
    /// mine is a later tier item, so raw materials arrive through this DECLARED
    /// trade link — real cost, real transit days, named origins. Not a fake.
    /// </summary>
    public sealed class ImportService
    {
        private readonly List<ImportOrder> orders = new List<ImportOrder>();
        private int nextOrderNumber = 1;

        public IReadOnlyList<ImportOrder> Orders => orders;

        /// <summary>
        /// Places an import order. The buyer pays NOW (cash outflow with provenance,
        /// or a payable on the liability ledger when cash is short and credit is
        /// offered). Refuses loudly when neither payment path works — no unpaid
        /// material ever enters the world.
        /// </summary>
        public ImportOrder PlaceOrder(
            string buyerBusinessId,
            string buyerBusinessName,
            string materialId,
            int units,
            int dayIndex,
            LandLedgers.Population.HouseholdLedger payerLedger,
            LandLedgers.Economy.Liabilities.BusinessLiabilityLedger liabilityLedger,
            LandLedgers.Primitives.EntityIdRegistry idRegistry,
            List<string> diagnostics)
        {
            diagnostics = diagnostics ?? new List<string>();

            ImportMaterial material = ImportCatalog.Get(materialId);
            if (material == null)
            {
                diagnostics.Add($"ImportService: unknown import material '{materialId}'.");
                return null;
            }
            if (units <= 0)
            {
                diagnostics.Add($"ImportService: order needs a positive unit count (got {units}).");
                return null;
            }
            if (string.IsNullOrWhiteSpace(buyerBusinessId))
            {
                diagnostics.Add("ImportService: buyer business id is required — no anonymous importers.");
                return null;
            }

            int totalCents = units * material.PricePerUnitCents;
            var order = new ImportOrder
            {
                OrderId = $"IMP-ORD-{nextOrderNumber++:D4}",
                BuyerBusinessId = buyerBusinessId,
                BuyerBusinessName = buyerBusinessName ?? buyerBusinessId,
                MaterialId = material.MaterialId,
                Units = units,
                PricePerUnitCents = material.PricePerUnitCents,
                OrderDayIndex = dayIndex,
                ExpectedArrivalDayIndex = dayIndex + material.TransitDays,
                OriginName = material.OriginName,
                Status = ImportOrderStatus.Ordered,
            };

            string purpose = $"import order {order.OrderId}: {units}x {material.DisplayName} from {material.OriginName}";
            bool paid = false;

            // RecordOutflow does not check the balance itself — the service must.
            if (payerLedger != null && payerLedger.GetBalanceCents() >= totalCents)
            {
                string rejection = payerLedger.RecordOutflow(dayIndex, totalCents, purpose, material.OriginName);
                paid = rejection == null;
                if (!paid)
                    diagnostics.Add($"ImportService: cash payment failed ({rejection}) — trying credit.");
            }
            else if (payerLedger != null)
            {
                diagnostics.Add(
                    $"ImportService: insufficient cash ({payerLedger.GetBalanceCents()}c < {totalCents}c) — trying credit.");
            }

            if (!paid && liabilityLedger != null && idRegistry != null)
            {
                var liability = liabilityLedger.BuyOnCredit(
                    idRegistry, buyerBusinessId, material.OriginName, totalCents,
                    purpose, dayIndex, diagnostics);
                if (liability != null)
                {
                    paid = true;
                    order.PaidOnCredit = true;
                    order.LiabilityId = liability.LiabilityId.ToString();
                }
            }

            if (!paid)
            {
                diagnostics.Add(
                    $"ImportService: order {order.OrderId} refused — no cash and no credit. " +
                    "Unpaid material never enters the world.");
                return null;
            }

            orders.Add(order);
            diagnostics.Add(
                $"ImportService: order {order.OrderId} placed — {units}x {material.DisplayName} " +
                $"from {material.OriginName}, arrives day {order.ExpectedArrivalDayIndex} " +
                $"({(order.PaidOnCredit ? "on credit" : "cash")}).");
            return order;
        }

        /// <summary>
        /// Delivers all orders due on or before dayIndex as provenance-carrying lots.
        /// </summary>
        public List<ImportLot> ReceiveDueImports(int dayIndex, List<string> diagnostics)
        {
            diagnostics = diagnostics ?? new List<string>();
            var delivered = new List<ImportLot>();
            foreach (ImportOrder order in orders)
            {
                if (order.Status != ImportOrderStatus.Ordered) continue;
                if (order.ExpectedArrivalDayIndex > dayIndex) continue;

                ImportMaterial material = ImportCatalog.Get(order.MaterialId);
                var lot = new ImportLot
                {
                    LotId = $"IMP-{order.OrderId}-{delivered.Count + 1}",
                    MaterialId = order.MaterialId,
                    MaterialName = material != null ? material.DisplayName : order.MaterialId,
                    Units = order.Units,
                    OriginName = order.OriginName,
                    OrderId = order.OrderId,
                    ArrivalDayIndex = dayIndex,
                    Imported = true,
                };
                order.Status = ImportOrderStatus.Delivered;
                delivered.Add(lot);
                diagnostics.Add(
                    $"ImportService: order {order.OrderId} delivered — {lot.Units}x {lot.MaterialName} " +
                    $"from {lot.OriginName} (lot {lot.LotId}).");
            }
            return delivered;
        }
    }
}
