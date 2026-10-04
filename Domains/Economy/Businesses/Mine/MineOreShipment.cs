using EntityId = LandLedgers.Primitives.EntityId;

using System;
using System.Collections.Generic;
using LandLedgers.Population;
using LandLedgers.Primitives;
using LandLedgers.World;
using UnityEngine;

namespace LandLedgers.Economy.Businesses.Mine
{
    /// <summary>Lifecycle of one ore shipment to an off-map smelter.</summary>
    public enum MineOreShipmentStatus
    {
        Unspecified = 0,
        InTransit = 1,  // tons left the stockpile, payment not yet received
        Delivered = 2,  // arrived; smelter paid
    }

    /// <summary>
    /// W8D: a DECLARED off-map trade link for ore disposition — named
    /// destination, priced per ton, real transit days (EQU-1 precedent).
    /// Ore is never sold to a fiat buyer and never leaves as synthetic
    /// stock: shipments dispense real tons from the ore stockpile and the
    /// smelter pays on delivery.
    /// </summary>
    [Serializable]
    public sealed class MineSmelterLink
    {
        [SerializeField]
        private string linkId = string.Empty;

        [SerializeField]
        private string smelterName = string.Empty;

        [SerializeField]
        private MineralResourceKind mineralKind;

        /// <summary>TUNING (calibration): smelter price per ton, cents.</summary>
        [SerializeField, Min(0)]
        private int pricePerTonCents;

        /// <summary>TUNING (calibration): transit days mine → smelter.</summary>
        [SerializeField, Min(1)]
        private int transitDays = 21;

        public string LinkId => linkId ?? string.Empty;
        public string SmelterName => smelterName ?? string.Empty;
        public MineralResourceKind MineralKind => mineralKind;
        public int PricePerTonCents => Math.Max(0, pricePerTonCents);
        public int TransitDays => Math.Max(1, transitDays);

        public MineSmelterLink() { }

        public MineSmelterLink(string linkId, string smelterName, MineralResourceKind mineralKind,
            int pricePerTonCents, int transitDays)
        {
            this.linkId = linkId ?? string.Empty;
            this.smelterName = smelterName ?? string.Empty;
            this.mineralKind = mineralKind;
            this.pricePerTonCents = Math.Max(0, pricePerTonCents);
            this.transitDays = Math.Max(1, transitDays);
        }
    }

    /// <summary>
    /// W8D: the declared smelter links. Named off-map destinations with
    /// priced, delayed terms — the honest path for ore disposition.
    /// </summary>
    public static class MineSmelterCatalog
    {
        private static readonly List<MineSmelterLink> links = new List<MineSmelterLink>
        {
            new MineSmelterLink("smelter-omaha-au", "Omaha Smelting Works", MineralResourceKind.Gold, 2850, 21),
            new MineSmelterLink("smelter-omaha-ag", "Omaha Smelting Works", MineralResourceKind.Silver, 1900, 21),
            new MineSmelterLink("smelter-pueblo-fe", "Pueblo Iron Furnace", MineralResourceKind.Iron, 850, 28),
            new MineSmelterLink("smelter-denver-coal", "Denver Coal Yard", MineralResourceKind.Coal, 320, 18),
        };

        public static IReadOnlyList<MineSmelterLink> Links => links;

        public static MineSmelterLink FindLink(string linkId)
        {
            foreach (MineSmelterLink link in links)
            {
                if (string.Equals(link.LinkId, linkId, StringComparison.Ordinal))
                    return link;
            }
            return null;
        }
    }

    /// <summary>
    /// W8D: one ore shipment order — real tons dispensed from the stockpile,
    /// priced by the declared smelter link, paid on delivery.
    /// </summary>
    [Serializable]
    public sealed class MineOreShipmentOrder
    {
        [SerializeField]
        private string orderId = string.Empty;

        [SerializeField]
        private string mineBusinessId = string.Empty;

        [SerializeField]
        private string mineBusinessName = string.Empty;

        [SerializeField]
        private MineralResourceKind mineralKind;

        [SerializeField, Min(0)]
        private int tons;

        [SerializeField, Min(0)]
        private int pricePerTonCents;

        [SerializeField]
        private string smelterLinkId = string.Empty;

        [SerializeField]
        private string smelterName = string.Empty;

        [SerializeField]
        private string originShaftId = string.Empty;

        [SerializeField, Min(0)]
        private int orderDayIndex;

        [SerializeField, Min(0)]
        private int expectedArrivalDayIndex;

        [SerializeField]
        private MineOreShipmentStatus status = MineOreShipmentStatus.InTransit;

        [SerializeField, Min(-1)]
        private int deliveredDayIndex = -1;

        public string OrderId => orderId ?? string.Empty;
        public string MineBusinessId => mineBusinessId ?? string.Empty;
        public string MineBusinessName => mineBusinessName ?? string.Empty;
        public MineralResourceKind MineralKind => mineralKind;
        public int Tons => Math.Max(0, tons);
        public int PricePerTonCents => Math.Max(0, pricePerTonCents);
        public int TotalCents => Tons * PricePerTonCents;
        public string SmelterLinkId => smelterLinkId ?? string.Empty;
        public string SmelterName => smelterName ?? string.Empty;
        public string OriginShaftId => originShaftId ?? string.Empty;
        public int OrderDayIndex => Math.Max(0, orderDayIndex);
        public int ExpectedArrivalDayIndex => Math.Max(0, expectedArrivalDayIndex);
        public MineOreShipmentStatus Status => status;
        public int DeliveredDayIndex => deliveredDayIndex;
        public bool IsDelivered => status == MineOreShipmentStatus.Delivered;

        public MineOreShipmentOrder() { }

        public MineOreShipmentOrder(string orderId, string mineBusinessId, string mineBusinessName,
            MineralResourceKind mineralKind, int tons, MineSmelterLink link, string originShaftId, int orderDayIndex)
        {
            this.orderId = orderId ?? string.Empty;
            this.mineBusinessId = mineBusinessId ?? string.Empty;
            this.mineBusinessName = mineBusinessName ?? string.Empty;
            this.mineralKind = mineralKind;
            this.tons = Math.Max(0, tons);
            pricePerTonCents = link != null ? link.PricePerTonCents : 0;
            smelterLinkId = link != null ? link.LinkId : string.Empty;
            smelterName = link != null ? link.SmelterName : string.Empty;
            this.originShaftId = originShaftId ?? string.Empty;
            this.orderDayIndex = Math.Max(0, orderDayIndex);
            expectedArrivalDayIndex = this.orderDayIndex + (link != null ? link.TransitDays : 21);
        }

        public void MarkDelivered(int dayIndex)
        {
            status = MineOreShipmentStatus.Delivered;
            deliveredDayIndex = Math.Max(0, dayIndex);
        }

        public MineOreShipmentOrderSaveDto CaptureSaveDto()
        {
            return new MineOreShipmentOrderSaveDto
            {
                orderId = OrderId,
                mineBusinessId = MineBusinessId,
                mineBusinessName = MineBusinessName,
                mineralKind = mineralKind,
                tons = Tons,
                pricePerTonCents = PricePerTonCents,
                smelterLinkId = SmelterLinkId,
                smelterName = SmelterName,
                originShaftId = OriginShaftId,
                orderDayIndex = OrderDayIndex,
                expectedArrivalDayIndex = ExpectedArrivalDayIndex,
                status = status,
                deliveredDayIndex = deliveredDayIndex,
            };
        }

        public static MineOreShipmentOrder FromSaveDto(MineOreShipmentOrderSaveDto dto)
        {
            if (dto == null)
                return null;
            var order = new MineOreShipmentOrder
            {
                orderId = dto.orderId ?? string.Empty,
                mineBusinessId = dto.mineBusinessId ?? string.Empty,
                mineBusinessName = dto.mineBusinessName ?? string.Empty,
                mineralKind = dto.mineralKind,
                tons = Math.Max(0, dto.tons),
                pricePerTonCents = Math.Max(0, dto.pricePerTonCents),
                smelterLinkId = dto.smelterLinkId ?? string.Empty,
                smelterName = dto.smelterName ?? string.Empty,
                originShaftId = dto.originShaftId ?? string.Empty,
                orderDayIndex = Math.Max(0, dto.orderDayIndex),
                expectedArrivalDayIndex = Math.Max(0, dto.expectedArrivalDayIndex),
                status = dto.status,
                deliveredDayIndex = dto.deliveredDayIndex,
            };
            return order;
        }
    }

    /// <summary>
    /// W8D: the ore shipment desk. PlaceShipment dispenses REAL tons from
    /// the ore stockpile (FIFO, provenance attached) and books the in-transit
    /// order against a declared smelter link. SettleDelivery records the
    /// smelter's payment on arrival — never early, never twice.
    /// </summary>
    public sealed class MineOreShipmentService
    {
        private readonly List<MineOreShipmentOrder> orders;
        private readonly List<string> diagnostics = new List<string>();
        private int nextOrderNumber;

        public IReadOnlyList<string> Diagnostics => diagnostics;
        public IReadOnlyList<MineOreShipmentOrder> Orders => orders;

        /// <summary>
        /// Builds the service around the mine's authoritative order list
        /// (held by the mine runtime and persisted via its save DTO).
        /// Order numbers derive from the list so save/load stays stable.
        /// </summary>
        public MineOreShipmentService(List<MineOreShipmentOrder> orderList)
        {
            orders = orderList ?? new List<MineOreShipmentOrder>();
            nextOrderNumber = orders.Count + 1;
        }

        public MineOreShipmentService() : this(null)
        {
        }

        /// <summary>
        /// Ships tons of ore to a declared smelter. Refuses unknown links,
        /// non-positive tons, and stockpile shortfalls loudly.
        /// </summary>
        public MineOreShipmentOrder PlaceShipment(MineOreStock stock, string mineBusinessId,
            string mineBusinessName, string smelterLinkId, int tons, string originShaftId, int dayIndex)
        {
            if (stock == null)
            {
                diagnostics.Add("MineOreShipmentService.PlaceShipment: ore stock is required.");
                return null;
            }
            MineSmelterLink link = MineSmelterCatalog.FindLink(smelterLinkId);
            if (link == null)
            {
                diagnostics.Add($"MineOreShipmentService.PlaceShipment: unknown smelter link '{smelterLinkId}' — ore ships only on declared trade links.");
                return null;
            }
            if (tons <= 0)
            {
                diagnostics.Add("MineOreShipmentService.PlaceShipment: tons must be positive.");
                return null;
            }
            if (stock.TotalTons < tons)
            {
                diagnostics.Add($"MineOreShipmentService.PlaceShipment: stockpile holds {stock.TotalTons} tons, cannot ship {tons} — refused, nothing moved.");
                return null;
            }
            if (string.IsNullOrWhiteSpace(mineBusinessId))
            {
                diagnostics.Add("MineOreShipmentService.PlaceShipment: mine business id is required — no anonymous shippers.");
                return null;
            }

            var dispenseDiag = new List<string>();
            List<MineOreDispenseLine> lines = stock.DispenseTons(tons, link.SmelterName, dispenseDiag);
            if (lines.Count == 0)
            {
                diagnostics.Add($"MineOreShipmentService.PlaceShipment: dispense failed — {string.Join("; ", dispenseDiag.ToArray())}");
                return null;
            }

            var order = new MineOreShipmentOrder(
                $"ORE-SHIP-{nextOrderNumber++:D4}", mineBusinessId, mineBusinessName,
                link.MineralKind, tons, link, originShaftId, dayIndex);
            orders.Add(order);
            return order;
        }

        /// <summary>
        /// Settles a delivered shipment: the smelter pays (SaleProceeds with
        /// the named smelter as counterparty). Refuses early or duplicate
        /// settlement.
        /// </summary>
        public string SettleDelivery(string orderId, HouseholdLedger businessCash, int dayIndex)
        {
            MineOreShipmentOrder order = FindOrder(orderId);
            if (order == null)
                return $"MineOreShipmentService.SettleDelivery: unknown order '{orderId}'.";
            if (order.IsDelivered)
                return $"MineOreShipmentService.SettleDelivery: order '{orderId}' already delivered on day {order.DeliveredDayIndex}.";
            if (dayIndex < order.ExpectedArrivalDayIndex)
                return $"MineOreShipmentService.SettleDelivery: order '{orderId}' is still in transit (expected day {order.ExpectedArrivalDayIndex}).";
            if (businessCash == null)
                return "MineOreShipmentService.SettleDelivery: the mine's operating cash ledger is required.";

            string rejection = businessCash.RecordInflow(dayIndex, order.TotalCents,
                HouseholdIncomeSource.SaleProceeds, order.OrderId,
                $"ore shipment {order.OrderId}: {order.Tons} tons {order.MineralKind} to {order.SmelterName}",
                order.SmelterName);
            if (rejection != null)
                return $"MineOreShipmentService.SettleDelivery: payment failed — {rejection}";

            order.MarkDelivered(dayIndex);
            return null;
        }

        public MineOreShipmentOrder FindOrder(string orderId)
        {
            foreach (MineOreShipmentOrder order in orders)
            {
                if (string.Equals(order.OrderId, orderId, StringComparison.Ordinal))
                    return order;
            }
            return null;
        }

        public List<MineOreShipmentOrder> InTransitOrders()
        {
            var result = new List<MineOreShipmentOrder>();
            foreach (MineOreShipmentOrder order in orders)
            {
                if (!order.IsDelivered)
                    result.Add(order);
            }
            return result;
        }

        public List<MineOreShipmentOrderSaveDto> CaptureSaveDtos()
        {
            var dtos = new List<MineOreShipmentOrderSaveDto>();
            foreach (MineOreShipmentOrder order in orders)
                dtos.Add(order.CaptureSaveDto());
            return dtos;
        }

        public void RestoreFromSaveDtos(List<MineOreShipmentOrderSaveDto> dtos)
        {
            orders.Clear();
            if (dtos == null)
            {
                nextOrderNumber = 1;
                return;
            }
            foreach (MineOreShipmentOrderSaveDto dto in dtos)
            {
                MineOreShipmentOrder order = MineOreShipmentOrder.FromSaveDto(dto);
                if (order != null)
                    orders.Add(order);
            }
            nextOrderNumber = orders.Count + 1;
        }
    }

    /// <summary>W8D: save DTOs for ore shipments. Owned by the mine runtime (standing rule).</summary>
    [Serializable]
    public sealed class MineOreShipmentOrderSaveDto
    {
        public string orderId = string.Empty;
        public string mineBusinessId = string.Empty;
        public string mineBusinessName = string.Empty;
        public MineralResourceKind mineralKind;
        public int tons;
        public int pricePerTonCents;
        public string smelterLinkId = string.Empty;
        public string smelterName = string.Empty;
        public string originShaftId = string.Empty;
        public int orderDayIndex;
        public int expectedArrivalDayIndex;
        public MineOreShipmentStatus status = MineOreShipmentStatus.InTransit;
        public int deliveredDayIndex = -1;
    }

    [Serializable]
    public sealed class MineOreShipmentLedgerSaveDto
    {
        public List<MineOreShipmentOrderSaveDto> orders = new List<MineOreShipmentOrderSaveDto>();
    }
}
