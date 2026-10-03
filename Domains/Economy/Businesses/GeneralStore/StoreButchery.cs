using System;
using System.Collections.Generic;
using LandLedgers.Economy.Butcher;
using LandLedgers.Economy.Creation;
using UnityEngine;

namespace LandLedgers.Economy.GeneralStore
{
    /// <summary>
    /// FRM-3: the butchery capabilities a general store can gain. Meat retail fits a
    /// storefront (customer-facing + food handling); slaughter needs a yard with
    /// sanitation (yard storage + animal housing + food handling + minimum area) and
    /// must be rejected on a DedicatedStorefront per the BIZ-1 form validation.
    /// </summary>
    public static class StoreButcheryCapabilities
    {
        public const string MeatRetailCapabilityId = "meat-retail";
        public const string SlaughterCapabilityId = "slaughter";

        /// <summary>
        /// Registers the butchery capabilities. Capabilities are data (Tech X §3.1);
        /// the registry stays the single authority.
        /// </summary>
        public static void RegisterAll(BusinessCapabilityRegistry registry, List<string> diagnostics)
        {
            diagnostics ??= new List<string>();
            if (registry == null)
            {
                diagnostics.Add("No capability registry — butchery capabilities not registered.");
                return;
            }

            registry.Register(new BusinessCapability(
                MeatRetailCapabilityId,
                "Meat Retail (Butchery Counter)",
                taskMethodIds: new[] { "sell_meat_retail", "receive_wholesale_cuts" },
                spaceRequirement: new PremisesRequirement(
                    customerFacing: true,
                    foodHandling: true)),
                diagnostics);

            registry.Register(new BusinessCapability(
                SlaughterCapabilityId,
                "Slaughter",
                taskMethodIds: new[] { "slaughter_livestock", "break_carcass" },
                spaceRequirement: new PremisesRequirement(
                    animalHousing: true,
                    yardStorage: true,
                    foodHandling: true,
                    minAreaSqFt: 400)),
                diagnostics);
        }
    }

    /// <summary>
    /// FRM-3: an executed wholesale cut purchase (butcher → general store). The store
    /// retails the cuts through its normal retail flow at its markup; the margin
    /// between wholesale cost and retail price is the butchery counter's profit.
    /// </summary>
    [Serializable]
    public sealed class WholesaleCutPurchase
    {
        public string PurchaseId = string.Empty;
        public string ButcherLotId = string.Empty;
        public int LbsTaken;
        public int WholesaleCostCents;
        public int WholesaleCostPerLbCents;
        public int RetailPricePerLbCents;
        public int DayIndex;
        public string ButcherBusinessId = string.Empty;
        public string StoreBusinessId = string.Empty;
    }

    /// <summary>
    /// FRM-3: the general store's butchery counter. The store buys wholesale cuts
    /// through the BIZ-4 wholesale link and retails them. Its own slaughter is only
    /// possible when premises resolve per the BIZ-1 validation (yard + sanitation);
    /// a storefront slaughterhouse is rejected, not fudged.
    /// </summary>
    public static class StoreButchery
    {
        /// <summary>
        /// Store buys wholesale cuts from the butcher. Returns the purchase record, or
        /// null with a diagnostic when the purchase cannot proceed.
        /// </summary>
        public static WholesaleCutPurchase BuyWholesaleCuts(
            ButcherRuntime butcher,
            string lotId,
            int requestedLbs,
            BusinessRuntimeState storeState,
            string storeBusinessId,
            string storeBusinessName,
            string butcherBusinessName,
            float retailMarkupMultiplier,
            int dayIndex,
            List<string> diagnostics)
        {
            diagnostics ??= new List<string>();
            if (butcher == null)
            {
                diagnostics.Add("No butcher runtime to buy from.");
                return null;
            }

            if (storeState == null)
            {
                diagnostics.Add("No store runtime state to receive the cuts.");
                return null;
            }

            if (requestedLbs <= 0)
            {
                diagnostics.Add("Requested lbs must be positive.");
                return null;
            }

            ButcherLot lotBefore = FindLot(butcher, lotId);
            if (lotBefore == null)
            {
                diagnostics.Add($"Unknown butcher lot '{lotId}'.");
                return null;
            }

            int lbsBefore = lotBefore.QuantityLbs;
            int wholesaleCostCents = butcher.SellWholesale(lotId, requestedLbs, storeBusinessId, diagnostics);
            if (wholesaleCostCents <= 0)
            {
                diagnostics.Add($"Wholesale purchase from lot '{lotId}' failed.");
                return null;
            }

            ButcherLot lotAfter = FindLot(butcher, lotId);
            int lbsTaken = Mathf.Max(0, lbsBefore - (lotAfter != null ? lotAfter.QuantityLbs : 0));
            if (lbsTaken <= 0)
            {
                diagnostics.Add($"Wholesale purchase from lot '{lotId}' moved no meat.");
                return null;
            }

            // The store retails the cuts through its meat category.
            const string meatCategoryId = "meat";
            storeState.EnsureCategoryStock(meatCategoryId, 0, Mathf.Max(lbsTaken, 100));
            int accepted = storeState.AddCategoryStockUnits(meatCategoryId, lbsTaken);
            if (accepted <= 0)
            {
                diagnostics.Add("Store could not receive the wholesale cuts (meat category full).");
                return null;
            }

            storeState.AddWeeklyLocalTransferCost(wholesaleCostCents,
                $"bought {lbsTaken} lbs wholesale cuts from {butcherBusinessName} for {wholesaleCostCents}c (lot {lotId})");

            int costPerLb = wholesaleCostCents / Mathf.Max(1, lbsTaken);
            float markup = Mathf.Max(1f, retailMarkupMultiplier);
            int retailPerLb = Mathf.CeilToInt(costPerLb * markup);

            var purchase = new WholesaleCutPurchase
            {
                PurchaseId = $"WCP-{dayIndex}-{lotId}",
                ButcherLotId = lotId,
                LbsTaken = lbsTaken,
                WholesaleCostCents = wholesaleCostCents,
                WholesaleCostPerLbCents = costPerLb,
                RetailPricePerLbCents = retailPerLb,
                DayIndex = dayIndex,
                ButcherBusinessId = butcherBusinessName ?? string.Empty,
                StoreBusinessId = storeBusinessId ?? string.Empty,
            };

            diagnostics.Add($"Store {storeBusinessName} bought {lbsTaken} lbs wholesale cuts " +
                $"from {butcherBusinessName} for {wholesaleCostCents}c; retails at {retailPerLb}c/lb " +
                $"(margin {retailPerLb - costPerLb}c/lb).");
            return purchase;
        }

        private static ButcherLot FindLot(ButcherRuntime butcher, string lotId)
        {
            foreach (ButcherLot lot in butcher.Lots)
            {
                if (string.Equals(lot.LotId, lotId, StringComparison.Ordinal))
                {
                    return lot;
                }
            }

            return null;
        }
    }
}
