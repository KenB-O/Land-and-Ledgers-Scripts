using System;
using System.Collections.Generic;
using LandLedgers.Economy.Farming.Crops;
using UnityEngine;

namespace LandLedgers.Economy.Businesses.GrainMill
{
    /// <summary>
    /// W5A: the two grist businesses a frontier mill runs side by side —
    /// custom (toll) milling and merchant milling. Canon R6 (business form
    /// follows actual function): these are separable commercial forms, so
    /// the mode is POLICY DATA on each batch, not a different business type.
    /// </summary>
    public enum GristBatchMode
    {
        /// <summary>
        /// Custom milling: the customer's grain, in the mill's custody. The
        /// customer keeps the flour/meal; the mill takes a toll (cash per
        /// unit and/or an in-kind share of the flour/meal output). Grain
        /// ownership never changes hands — toll grain is never merchant
        /// inventory and merchant grain is never ground as toll.
        /// </summary>
        Toll = 0,
        /// <summary>
        /// Merchant milling: the mill grinds its own (merchant) grain and
        /// sells the products. The mill bears the working-capital exposure
        /// on the grain and the market risk on the flour/meal.
        /// </summary>
        Merchant = 1,
    }

    /// <summary>
    /// W5A: one named merchant price row. Prices are calibration data
    /// (Canon Part XV); the mill never moves money itself — prices name
    /// what the ledger authorities post when grain is bought or products
    /// sell.
    /// </summary>
    [Serializable]
    public sealed class GristMillPriceRow
    {
        public string Key = string.Empty; // e.g. "buy:wheat", "sell:flour"
        public int PriceCentsPerUnit;

        public GristMillPriceRow() { }

        public GristMillPriceRow(string key, int priceCentsPerUnit)
        {
            Key = key ?? string.Empty;
            PriceCentsPerUnit = Math.Max(0, priceCentsPerUnit);
        }
    }

    /// <summary>
    /// W5A: the mill's toll/merchant policy — the economic form of the grist
    /// business as DATA. The miller sets these at opening (or adjusts them
    /// later through the business's policy UI, a later package); every
    /// batch reads the policy in force when it runs.
    ///
    /// TOLL FORM: on a toll grind the customer pays a cash toll per grain
    /// unit ground, and the mill may also keep an in-kind share of the
    /// flour/meal output (the historical miller's toll). Both default to
    /// calibration values; setting either to zero disables that toll.
    ///
    /// MERCHANT FORM: the mill buys grain and sells products at the named
    /// merchant prices (keyed "buy:{crop}" / "sell:{product-kind}").
    ///
    /// The runtime never posts ledgers — it returns toll and price data on
    /// each batch result so the ledger authorities record the money.
    /// </summary>
    [Serializable]
    public sealed class GristMillPolicy
    {
        /// <summary>Calibration: cash toll per grain unit ground on a toll grind (0 = no cash toll).</summary>
        public int TollCashCentsPerUnit = 2;

        /// <summary>
        /// Calibration: share of the toll grind's flour/meal output the mill
        /// keeps in kind (0..1; 0 = no in-kind toll). Default 1/16, the
        /// historical miller's toll.
        /// </summary>
        public float TollInKindFlourShare01 = 1f / 16f;

        /// <summary>Merchant price book: "buy:wheat"/"sell:flour" rows.</summary>
        public List<GristMillPriceRow> MerchantPrices = new List<GristMillPriceRow>();

        public GristMillPolicy()
        {
            // Calibration opening book (Canon Part XV): the mill buys wheat
            // cheap at the farm gate / from the dealer and sells flour at a
            // milling margin; corn meal and feed sell cheaper.
            MerchantPrices.Add(new GristMillPriceRow("buy:wheat", 6));
            MerchantPrices.Add(new GristMillPriceRow("buy:corn", 4));
            MerchantPrices.Add(new GristMillPriceRow("buy:oats", 3));
            MerchantPrices.Add(new GristMillPriceRow("sell:flour", 12));
            MerchantPrices.Add(new GristMillPriceRow("sell:meal", 9));
            MerchantPrices.Add(new GristMillPriceRow("sell:feed", 3));
            MerchantPrices.Add(new GristMillPriceRow("sell:bran", 2));
        }

        /// <summary>Returns the named merchant price, or 0 when unnamed — callers never assume a price.</summary>
        public int MerchantPrice(string key)
        {
            if (string.IsNullOrWhiteSpace(key) || MerchantPrices == null) return 0;
            foreach (var row in MerchantPrices)
            {
                if (row != null && string.Equals(row.Key, key, StringComparison.OrdinalIgnoreCase))
                    return Math.Max(0, row.PriceCentsPerUnit);
            }
            return 0;
        }

        /// <summary>
        /// The in-kind toll flour units the mill keeps from a toll grind
        /// producing `mainUnits` of flour/meal. Rounded down; the customer
        /// keeps the rest.
        /// </summary>
        public int TollInKindUnits(int mainUnits)
        {
            float share = Mathf.Clamp01(TollInKindFlourShare01);
            if (mainUnits <= 0 || share <= 0f) return 0;
            return Math.Min(mainUnits, (int)Math.Floor(mainUnits * share));
        }
    }
}
