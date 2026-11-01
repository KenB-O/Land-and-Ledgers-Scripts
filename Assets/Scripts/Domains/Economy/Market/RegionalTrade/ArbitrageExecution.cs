using System;
using System.Collections.Generic;
using LandLedgers.Population;
using LandLedgers.World.Journeys;
using UnityEngine;

namespace LandLedgers.Economy.Market.RegionalTrade
{
    /// <summary>
    /// NX-3D: the sell side of an arbitrage — a real merchant that BUYS the
    /// good. Mirrors T1A's <see cref="IGoodsSupplier"/> (which sells to us).
    /// Finite appetite: a merchant cannot buy what it has no room or need
    /// for — the caller enforces real limits, never infinite demand.
    /// </summary>
    public interface IArbitrageSellSide
    {
        string MerchantName { get; }
        string LocationId { get; }
        bool BuysCategory(string categoryId);
        /// <summary>What the merchant pays us per unit (their buy price).</summary>
        int BuyPricePerUnitCents(string categoryId);
        /// <summary>Takes up to requestedUnits; returns units actually taken.</summary>
        int AcceptSale(string categoryId, int requestedUnits, int dayIndex, List<string> diagnostics);
    }

    /// <summary>
    /// NX-3D: the actor's purse. The freight company, the player, or any
    /// business can run arbitrage — each funds it differently, so funds are
    /// an interface: household ledgers, business working capital, or personal
    /// cash all implement this. No anonymous money.
    /// </summary>
    public interface IArbitrageFunds
    {
        string OwnerName { get; }
        /// <summary>Debits the buy cost; returns a rejection string, or null when paid.</summary>
        string DebitBuyCost(int costCents, string reason, int dayIndex, List<string> diag);
        /// <summary>Credits sale proceeds.</summary>
        void CreditSaleProceeds(int amountCents, string reason, int dayIndex, List<string> diag);
    }

    /// <summary>NX-3D: how the cargo travels between towns.</summary>
    public enum ArbitrageHaulKind
    {
        Unspecified = 0,
        HiredCarrier = 1, // a real freight company/carrier, paid the honest freight charge
        OwnTeam = 2,      // the actor's own wagon and team — internal cost, never revenue to itself (BIZ-3 rule)
    }

    /// <summary>NX-3D: one executed arbitrage trade — every leg recorded.</summary>
    [Serializable]
    public sealed class ArbitrageTrade
    {
        public string TradeId = string.Empty;
        public string GoodKind = string.Empty;
        public int Units;
        public string ActorName = string.Empty;
        public int ActingPersonId = -1;
        public string BuyMerchant = string.Empty;
        public string SellMerchant = string.Empty;
        public int BuyCostCents;
        public int FreightCents;
        public ArbitrageHaulKind HaulKind;
        public string CarrierName = string.Empty;
        public int SpoiledUnits;
        public int SaleRevenueCents;
        public int NetCents;
        public int TravelMinutes;
        public int DayIndex;
        public bool Completed;

        public ArbitrageTrade() { }
    }

    /// <summary>
    /// NX-3D: arbitrage EXECUTION. T3A derives opportunities as a read; this
    /// service lets someone ACT on one: buy low in one town, haul over real
    /// JRN routes, sell high in the other. Every unit physically travels —
    /// no teleporting goods (T3A canon lock).
    ///
    /// The legs, each honest:
    /// - BUY: the buy-side merchant is a real <see cref="IGoodsSupplier"/>
    ///   with finite stock; the acting person is real and a journey route to
    ///   the merchant must exist (the T1A embodied pattern: real person, real
    ///   route, named counterparty, no anonymous buyers). Funds debit first
    ///   (FRM-1 atomicity); if the merchant then has no stock, the debit is
    ///   refunded — nothing moves one-sided.
    /// - HAUL: freight cost from the regional trade service over the real
    ///   route (rail where both ends are rail-served, else wagon). A hired
    ///   carrier is paid the honest charge; the actor's own team is an
    ///   internal cost, never revenue to itself (BIZ-3 rule, Canon §3.6).
    /// - SELL: the sell-side merchant is a real <see cref="IArbitrageSellSide"/>
    ///   with finite appetite. Margins are net of freight, spoilage, and time.
    ///   If the net is not positive at execution, the trade is refused — stale
    ///   opportunities do not execute.
    /// </summary>
    public sealed class ArbitrageExecutor
    {
        private readonly List<ArbitrageTrade> trades = new List<ArbitrageTrade>();
        private readonly List<string> diagnostics = new List<string>();
        private int sequence;

        public IReadOnlyList<string> Diagnostics => diagnostics;
        public IReadOnlyList<ArbitrageTrade> Trades => trades;

        /// <summary>
        /// Executes one arbitrage opportunity end to end. Every parameter is a
        /// real thing: real merchants, real funds, a real person, a real route.
        /// </summary>
        /// <param name="buyPricePerUnitCents">What the buy merchant charges (their sell price), from a real observation.</param>
        /// <param name="sellPricePerUnitCents">What the sell merchant pays (their buy price), from a real observation.</param>
        /// <param name="spoiledUnits">Units lost in transit (perishables age on real travel time) — computed by the caller.</param>
        /// <param name="personLocationId">Resolves the acting person's journey location; defaults to town center.</param>
        public ArbitrageTrade Execute(
            ArbitrageOpportunity opportunity,
            string actorName, int actingPersonId,
            IGoodsSupplier buyMerchant, int buyPricePerUnitCents,
            IArbitrageSellSide sellMerchant, int sellPricePerUnitCents,
            IArbitrageFunds funds, ArbitrageHaulKind haulKind, string carrierName,
            int spoiledUnits, JourneyModel journeys, RegionalTradeService trade,
            PopulationState population, int dayIndex, List<string> diag,
            Func<int, string> personLocationId = null)
        {
            diag = diag ?? diagnostics;
            if (opportunity == null)
            {
                diag.Add("ArbitrageExecutor: no opportunity — nothing to execute.");
                return null;
            }
            if (string.IsNullOrWhiteSpace(actorName))
            {
                diag.Add("ArbitrageExecutor: the actor must be named — no anonymous arbitrageurs.");
                return null;
            }
            if (population == null || population.GetPerson(actingPersonId) == null)
            {
                diag.Add($"ArbitrageExecutor: P{actingPersonId} is not a known person — only a real acting Person moves goods.");
                return null;
            }
            if (buyMerchant == null || sellMerchant == null || funds == null || journeys == null || trade == null)
            {
                diag.Add("ArbitrageExecutor: merchants, funds, journeys, and the trade service are all required — no leg is faked.");
                return null;
            }
            if (!buyMerchant.HasCategory(opportunity.GoodKind) || !sellMerchant.BuysCategory(opportunity.GoodKind))
            {
                diag.Add($"ArbitrageExecutor: '{opportunity.GoodKind}' is not traded by the named merchants — opportunity is stale.");
                return null;
            }
            if (opportunity.Units <= 0)
            {
                diag.Add("ArbitrageExecutor: the opportunity names no units.");
                return null;
            }

            // The acting person must be able to REACH the buy merchant (T1A pattern).
            string originId = personLocationId != null ? personLocationId(actingPersonId) : "town-center";
            JourneyRoute buyRoute = journeys.FindRoute(originId, buyMerchant.LocationId, TravelMode.Foot);
            if (!buyRoute.Found)
            {
                diag.Add($"ArbitrageExecutor: no route for P{actingPersonId} to '{buyMerchant.SupplierName}' — {buyRoute.Diagnostic}. No trade faked.");
                return null;
            }

            // Honest freight over the real route — recomputed at execution, not
            // trusted from the read (prices and routes move).
            int freight = trade.FreightCostCents(
                buyMerchant.LocationId, sellMerchant.LocationId, opportunity.Units,
                journeys, diag, out bool usesRail, out int travelMinutes);
            if (freight < 0)
            {
                diag.Add("ArbitrageExecutor: the haul is unroutable — the cargo cannot travel, so no trade.");
                return null;
            }
            if (haulKind == ArbitrageHaulKind.Unspecified)
            {
                diag.Add("ArbitrageExecutor: the haul kind must be stated (hired carrier or own team).");
                return null;
            }

            int spoiled = Math.Max(0, Math.Min(opportunity.Units, spoiledUnits));
            int sellableUnits = opportunity.Units - spoiled;
            int buyCost = opportunity.Units * Math.Max(0, buyPricePerUnitCents);
            int revenue = sellableUnits * Math.Max(0, sellPricePerUnitCents);
            int net = revenue - buyCost - freight;
            if (net <= 0)
            {
                diag.Add($"ArbitrageExecutor: net {net}c (revenue {revenue}c − buy {buyCost}c − freight {freight}c) — " +
                    "stale opportunity refused; no loss-making trade executed.");
                return null;
            }

            // Funds first (FRM-1 atomicity): the actor pays before stock moves.
            string fundsRejection = funds.DebitBuyCost(buyCost,
                $"arbitrage buy: {opportunity.Units}u {opportunity.GoodKind} from {buyMerchant.SupplierName}", dayIndex, diag);
            if (fundsRejection != null)
            {
                diag.Add($"ArbitrageExecutor: '{actorName}' cannot fund the buy — {fundsRejection}");
                return null;
            }

            int bought = buyMerchant.Sell(opportunity.GoodKind, opportunity.Units, dayIndex, diag);
            if (bought <= 0)
            {
                // Stock vanished between the read and execution — refund, loud.
                funds.CreditSaleProceeds(buyCost, "arbitrage buy refunded: merchant had no stock", dayIndex, diag);
                diag.Add($"ArbitrageExecutor: '{buyMerchant.SupplierName}' had no '{opportunity.GoodKind}' — buy refunded, no trade faked.");
                return null;
            }
            if (bought < opportunity.Units)
            {
                // Partial fill: re-price the trade on what actually moved.
                int refund = (opportunity.Units - bought) * Math.Max(0, buyPricePerUnitCents);
                funds.CreditSaleProceeds(refund, "arbitrage partial fill refund", dayIndex, diag);
                diag.Add($"ArbitrageExecutor: partial fill — {bought}u of {opportunity.Units}u available. Trade re-priced on {bought}u.");
            }

            int actualBuyCost = bought * Math.Max(0, buyPricePerUnitCents);
            int actualSpoiled = Math.Max(0, Math.Min(bought, spoiled));
            int actualSellable = bought - actualSpoiled;
            // Re-check the net on the actual fill — a partial fill can kill the margin.
            int actualFreight = trade.FreightCostCents(
                buyMerchant.LocationId, sellMerchant.LocationId, bought,
                journeys, diag, out _, out int actualMinutes);
            if (actualFreight < 0)
            {
                funds.CreditSaleProceeds(actualBuyCost, "arbitrage refunded: haul unroutable on actual fill", dayIndex, diag);
                diag.Add("ArbitrageExecutor: haul unroutable on the actual fill — refunded, no trade.");
                return null;
            }
            int actualRevenue = actualSellable * Math.Max(0, sellPricePerUnitCents);
            int actualNet = actualRevenue - actualBuyCost - actualFreight;
            if (actualNet <= 0)
            {
                funds.CreditSaleProceeds(actualBuyCost, "arbitrage refunded: partial fill killed the margin", dayIndex, diag);
                diag.Add($"ArbitrageExecutor: partial fill net {actualNet}c — refused rather than executed at a loss.");
                return null;
            }

            // The haul: hired carrier is paid; own team is internal cost (BIZ-3 rule).
            if (haulKind == ArbitrageHaulKind.HiredCarrier)
            {
                if (string.IsNullOrWhiteSpace(carrierName))
                {
                    funds.CreditSaleProceeds(actualBuyCost, "arbitrage refunded: no carrier named", dayIndex, diag);
                    diag.Add("ArbitrageExecutor: a hired haul needs a named carrier.");
                    return null;
                }
                string freightRejection = funds.DebitBuyCost(actualFreight,
                    $"arbitrage freight: {bought}u {opportunity.GoodKind} via {carrierName}" +
                    (usesRail ? " (rail)" : " (wagon)"), dayIndex, diag);
                if (freightRejection != null)
                {
                    funds.CreditSaleProceeds(actualBuyCost, "arbitrage refunded: cannot pay freight", dayIndex, diag);
                    diag.Add($"ArbitrageExecutor: cannot pay the {actualFreight}c freight — {freightRejection}");
                    return null;
                }
                diag.Add($"ArbitrageExecutor: paid {actualFreight}c freight to '{carrierName}'" +
                    (usesRail ? " (rail)." : " (wagon)."));
            }
            else
            {
                diag.Add($"ArbitrageExecutor: '{actorName}' hauls {bought}u with its own team — " +
                    $"internal cost {actualFreight}c, never revenue to itself (BIZ-3 rule).");
            }

            // The sell: the merchant takes what it has appetite for.
            int taken = sellMerchant.AcceptSale(opportunity.GoodKind, actualSellable, dayIndex, diag);
            if (taken < actualSellable)
                diag.Add($"ArbitrageExecutor: '{sellMerchant.MerchantName}' took {taken}u of {actualSellable}u — finite appetite, honestly partial.");
            int finalRevenue = taken * Math.Max(0, sellPricePerUnitCents);
            funds.CreditSaleProceeds(finalRevenue,
                $"arbitrage sale: {taken}u {opportunity.GoodKind} to {sellMerchant.MerchantName}", dayIndex, diag);

            var tradeRecord = new ArbitrageTrade
            {
                TradeId = $"arb-{dayIndex}-{sequence++}",
                GoodKind = opportunity.GoodKind,
                Units = bought,
                ActorName = actorName,
                ActingPersonId = actingPersonId,
                BuyMerchant = buyMerchant.SupplierName,
                SellMerchant = sellMerchant.MerchantName,
                BuyCostCents = actualBuyCost,
                FreightCents = actualFreight,
                HaulKind = haulKind,
                CarrierName = haulKind == ArbitrageHaulKind.HiredCarrier ? carrierName : string.Empty,
                SpoiledUnits = actualSpoiled,
                SaleRevenueCents = finalRevenue,
                NetCents = finalRevenue - actualBuyCost - actualFreight,
                TravelMinutes = actualMinutes,
                DayIndex = dayIndex,
                Completed = true,
            };
            trades.Add(tradeRecord);
            diag.Add($"ArbitrageExecutor: trade {tradeRecord.TradeId} complete — '{actorName}' netted {tradeRecord.NetCents}c " +
                $"moving {bought}u {opportunity.GoodKind} ({travelMinutes} min travel).");
            return tradeRecord;
        }
    }
}
