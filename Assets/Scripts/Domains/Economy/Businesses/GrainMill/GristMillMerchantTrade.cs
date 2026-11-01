using System;
using System.Collections.Generic;
using LandLedgers.Economy.Farming.Crops;
using UnityEngine;

namespace LandLedgers.Economy.Businesses.GrainMill
{
    /// <summary>
    /// D2E: the gristmill's MERCHANT books — the mill as grain buyer and
    /// flour/feed seller. Canon audit (Rev XXVII R6 / XVII-B):
    ///
    /// - The W5A width built the two grist businesses as policy data
    ///   (GristBatchMode Toll/Merchant, Canon R6 "business form follows
    ///   actual function") and authored a merchant price book on
    ///   GristMillPolicy ("buy:wheat", "sell:flour", ...), but NO runtime
    ///   path ever read those rows: intake recorded no purchase price or
    ///   seller, and sales recorded no revenue. The merchant model was
    ///   calibration without bookkeeping.
    /// - Canon Part XV: prices are calibration data; the mill never moves
    ///   money itself — prices name what the ledger authorities post.
    /// - Upstream-provenance doctrine: every merchant grain lot must trace
    ///   to a real farm/dealer/elevator; the seller is NAMED on the
    ///   purchase record.
    /// - Canon R6 §1.2: feed commerce is a valid independent role serving
    ///   farms, ranches, livery operators and draft-animal users; the
    ///   Canon lock "a mill may sell flour/feed through an attached or
    ///   remote commercial outlet" is the mill's own retail outlet channel.
    ///
    /// What this adds (depth, no width behavior changed):
    /// - GristMillPurchaseRecord: one named grain purchase (seller id/name,
    ///   price per unit from the named price or the policy book, cost in
    ///   cents for the ledger authority to post);
    /// - GristMillMerchantSaleResult + GristMillSaleRecord: one named
    ///   product sale from mill-owned stock (flour/meal/feed/bran), FIFO
    ///   dispensed with the source grain lots named, revenue in cents for
    ///   the ledger authority to post;
    /// - purchase/sale histories owned by GrainMillShopRuntime, surviving
    ///   the save round trip.
    ///
    /// DESIGN FORK (recorded, not guessed): the mill's working-capital
    /// exposure on merchant grain (the Canon R6 §1.3 dealer analog) is NOT
    /// enforced in this runtime — the mill holds no cash here, and the
    /// runtime never posts ledgers. BuyMerchantLot records the cost and
    /// returns it; the ledger authority enforces capital and posts the
    /// outflow.
    ///
    /// DESIGN FORK (recorded, not guessed): grain in the mill's books does
    /// not shrink or rot — Canon §7.4L treats grain as storable (not
    /// perishable like milk), and no canon or research figure names a
    /// weevil/spoilage rate. Custody lots and merchant lots persist at
    /// their received units until ground or dispensed.
    ///
    /// Toll calibration (1/16 in-kind + 2c cash per unit) is unchanged —
    /// the gristmill keeps its earlier-reported toll terms.
    /// </summary>
    [Serializable]
    public sealed class GristMillPurchaseRecord
    {
        public string GrainLotId = string.Empty;
        public CropKind Crop;
        public int Units;
        public int PricePerUnitCents; // the price actually booked (named price, else the policy "buy:{crop}" row)
        public int CostCents;         // booked outlay — the ledger authority posts it, never here
        public string SellerId = string.Empty;   // farm / dealer / elevator — the real upstream source
        public string SellerName = string.Empty; // named seller — never anonymous
        public string FarmId = string.Empty;     // farm provenance off the lot (empty when the seller is the farm's agent)
        public int DayIndex;

        public GristMillPurchaseRecord() { }
    }

    /// <summary>
    /// D2E: one mill-owned product sale. Revenue is recorded, not moved:
    /// RevenueCents names what the ledger authority posts against the
    /// buyer; the dispensed MillLot names the source grain lots so the
    /// farm <- dealer <- mill chain survives the sale.
    /// </summary>
    [Serializable]
    public sealed class GristMillSaleRecord
    {
        public string SaleLotId = string.Empty; // the buyer lot's HF-1 id
        public string ProductKind = string.Empty; // "flour", "meal", "feed", "bran"
        public int Units;
        public int PricePerUnitCents; // the price actually booked (named price, else the policy "sell:{kind}" row)
        public int RevenueCents;      // booked revenue — the ledger authority posts it, never here
        public string BuyerId = string.Empty;   // named buyer — never anonymous
        public string BuyerName = string.Empty;
        public string SourceGrainLotIds = string.Empty; // from the dispensed lot's chain
        public int DayIndex;

        public GristMillSaleRecord() { }
    }

    /// <summary>
    /// D2E: the return value of a merchant product sale — the dispensed
    /// buyer lot plus the revenue the ledger authority must post.
    /// </summary>
    public sealed class GristMillMerchantSaleResult
    {
        public MillLot Lot; // the buyer's lot, dispensed FIFO from mill-owned stock
        public GristMillSaleRecord Record;
    }

    /// <summary>
    /// D2E: the merchant-model trade operations, owned by the
    /// GrainMillShopRuntime. Two entry points:
    ///
    /// - BuyMerchantLot: the mill buys grain from a NAMED seller (farm,
    ///   dealer, elevator) at a named price or the policy "buy:{crop}"
    ///   row. Refused loudly when the lot is bad (intake rules) or the
    ///   seller is unnamed. Returns the booked cost in cents for the
    ///   ledger authority to post; -1 on refusal (nothing booked).
    /// - SellProductLot: the mill sells mill-owned product (flour, meal,
    ///   feed, bran) to a NAMED buyer at a named price or the policy
    ///   "sell:{product-kind}" row. Shortfalls dispense nothing; unpriced
    ///   product kinds are refused (the merchant book never assumes a
    ///   price). Returns the sale result for the ledger authority to
    ///   post, or null on refusal.
    ///
    /// The non-trade intake paths (ReceiveMerchantLot for transfers that
    /// are not purchases, OfferProductLot for non-cash dispensing such as
    /// the W2A bakery flour supply) are unchanged.
    /// </summary>
    public static class GristMillMerchantTrade
    {
        /// <summary>
        /// Buys a merchant grain lot from a named seller. Returns the booked
        /// cost in cents for the ledger authority to post, or -1 on refusal.
        /// </summary>
        public static int BuyMerchantLot(
            GrainMillShopRuntime mill,
            CropLot lot,
            string sellerId,
            string sellerName,
            int dayIndex,
            List<string> diag,
            int? pricePerUnitCents = null)
        {
            diag = diag ?? new List<string>();
            if (mill == null)
            {
                diag.Add("GristMillMerchantTrade.BuyMerchantLot: no mill — the buy is refused.");
                return -1;
            }
            if (string.IsNullOrWhiteSpace(sellerId) || string.IsNullOrWhiteSpace(sellerName))
            {
                diag.Add("GristMillMerchantTrade.BuyMerchantLot: the seller must be named (farm / dealer / elevator) — "
                    + "the mill's grain traces to a real upstream source, never an anonymous intake.");
                return -1;
            }

            int price = pricePerUnitCents.HasValue
                ? Math.Max(0, pricePerUnitCents.Value)
                : mill.Policy.MerchantPrice("buy:" + (lot != null ? lot.Crop.ToString().ToLowerInvariant() : "unspecified"));
            if (!pricePerUnitCents.HasValue && price <= 0)
            {
                diag.Add("GristMillMerchantTrade.BuyMerchantLot: the policy books no 'buy:" 
                    + (lot != null ? lot.Crop.ToString().ToLowerInvariant() : "?") 
                    + "' price — booking at 0c (unnamed prices are never assumed, the record says so plainly).");
            }

            string refusal = mill.GrainStock.ReceiveMerchantLot(lot, diag);
            if (refusal != null)
            {
                diag.Add($"GristMillMerchantTrade.BuyMerchantLot: intake refused — {refusal}. No purchase booked.");
                return -1;
            }

            int costCents = Math.Max(0, lot.QuantityUnits) * price;
            mill.RecordMerchantPurchase(new GristMillPurchaseRecord
            {
                GrainLotId = lot.LotId.ToString(),
                Crop = lot.Crop,
                Units = lot.QuantityUnits,
                PricePerUnitCents = price,
                CostCents = costCents,
                SellerId = sellerId,
                SellerName = sellerName,
                FarmId = lot.FarmId ?? string.Empty,
                DayIndex = dayIndex,
            });

            diag.Add($"GristMillMerchantTrade: mill {mill.BusinessInstanceId} BOUGHT {lot.QuantityUnits}u {lot.Crop} grain "
                + $"(lot {lot.LotId}) from {sellerName} ({sellerId}) at {price}c/u = {costCents}c — "
                + "booked for the ledger authority, not posted here. Farm provenance: " 
                + (string.IsNullOrWhiteSpace(lot.FarmId) ? "via seller" : lot.FarmId) + ".");
            return costCents;
        }

        /// <summary>
        /// Sells mill-owned product to a named buyer. Returns the sale
        /// result (dispensed lot + revenue for the ledger authority to
        /// post), or null on refusal — shortfalls dispense nothing.
        /// </summary>
        public static GristMillMerchantSaleResult SellProductLot(
            GrainMillShopRuntime mill,
            string productKind,
            int units,
            string buyerId,
            string buyerName,
            int dayIndex,
            List<string> diag,
            int? pricePerUnitCents = null)
        {
            diag = diag ?? new List<string>();
            if (mill == null)
            {
                diag.Add("GristMillMerchantTrade.SellProductLot: no mill — the sale is refused.");
                return null;
            }
            if (string.IsNullOrWhiteSpace(productKind))
            {
                diag.Add("GristMillMerchantTrade.SellProductLot: no product kind named — the sale is refused.");
                return null;
            }
            if (string.IsNullOrWhiteSpace(buyerId) || string.IsNullOrWhiteSpace(buyerName))
            {
                diag.Add("GristMillMerchantTrade.SellProductLot: the buyer must be named — "
                    + "sale revenue is money with provenance (Canon XIII §13.2), never anonymous.");
                return null;
            }

            string normalized = productKind.Trim().ToLowerInvariant();
            int price = pricePerUnitCents.HasValue
                ? Math.Max(0, pricePerUnitCents.Value)
                : mill.Policy.MerchantPrice("sell:" + normalized);
            if (!pricePerUnitCents.HasValue && price <= 0)
            {
                diag.Add($"GristMillMerchantTrade.SellProductLot: no 'sell:{normalized}' price in the policy book — "
                    + "the merchant book never assumes a price; sale refused.");
                return null;
            }

            MillLot dispensed = mill.OfferProductLot(normalized, units, diag);
            if (dispensed == null)
            {
                diag.Add($"GristMillMerchantTrade.SellProductLot: sale to {buyerName} refused — no units conjured.");
                return null;
            }

            int revenueCents = dispensed.QuantityUnits * price;
            var result = new GristMillMerchantSaleResult
            {
                Lot = dispensed,
                Record = new GristMillSaleRecord
                {
                    SaleLotId = dispensed.LotId.ToString(),
                    ProductKind = normalized,
                    Units = dispensed.QuantityUnits,
                    PricePerUnitCents = price,
                    RevenueCents = revenueCents,
                    BuyerId = buyerId,
                    BuyerName = buyerName,
                    SourceGrainLotIds = dispensed.SourceGrainLotId ?? string.Empty,
                    DayIndex = dayIndex,
                },
            };
            mill.RecordMerchantSale(result.Record);

            diag.Add($"GristMillMerchantTrade: mill {mill.BusinessInstanceId} SOLD {dispensed.QuantityUnits}u '{normalized}' "
                + $"(lot {dispensed.LotId}) to {buyerName} ({buyerId}) at {price}c/u = {revenueCents}c — "
                + "booked for the ledger authority, not posted here. Source grain lot(s): "
                + (string.IsNullOrWhiteSpace(dispensed.SourceGrainLotId) ? "unnamed" : dispensed.SourceGrainLotId) + ".");
            return result;
        }
    }
}
