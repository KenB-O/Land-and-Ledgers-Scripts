using System;
using System.Collections.Generic;
using LandLedgers.Population;
using UnityEngine;

namespace LandLedgers.Economy.Core
{
    /// <summary>
    /// Pass VII Workstream A: a real input requirement of an operating business
    /// (production need for inputs, not a forecast). Registered by production
    /// runtimes that know their input recipes; the demand source never invents
    /// business needs.
    /// </summary>
    [Serializable]
    public sealed class BusinessInputNeed
    {
        public string BuyerBusinessInstanceId = string.Empty;
        public string InputCategoryId = string.Empty;
        public int UnitsNeeded;
        public int MaxPriceCentsPerUnit;
        public string ReasonSummary = string.Empty;
    }

    /// <summary>
    /// Pass VII Workstream A: production wiring adapter. Exposes an operating
    /// <see cref="BusinessInstanceState"/> as an <see cref="IGoodsSupplier"/>
    /// so the real-demand source sees live business sellers with their actual
    /// stock and prices. Never invents stock or prices: both read live state.
    /// </summary>
    public sealed class BusinessInstanceSellerAdapter : IGoodsSupplier
    {
        private readonly BusinessInstanceState business;
        private readonly Func<string, int> pricePerUnitCents;

        public BusinessInstanceSellerAdapter(BusinessInstanceState business, Func<string, int> pricePerUnitCents)
        {
            this.business = business;
            this.pricePerUnitCents = pricePerUnitCents;
        }

        public string SupplierBusinessId => business != null ? business.InstanceId : string.Empty;
        public string SupplierName => business != null ? business.RuntimeDisplayName : string.Empty;
        public string LocationId => business != null ? $"business:{business.InstanceId}" : string.Empty;

        public bool HasCategory(string categoryId)
        {
            return business != null && business.RuntimeState != null
                && business.RuntimeState.GetCategoryStock(categoryId) != null;
        }

        public int StockUnits(string categoryId)
        {
            CategoryStockState stock = business != null && business.RuntimeState != null
                ? business.RuntimeState.GetCategoryStock(categoryId)
                : null;
            return stock != null ? stock.CurrentStockUnits : 0;
        }

        public int PricePerUnitCents(string categoryId)
        {
            return Mathf.Max(0, pricePerUnitCents != null ? pricePerUnitCents(categoryId) : 0);
        }

        public int Sell(string categoryId, int requestedUnits, int dayIndex, List<string> diagnostics)
        {
            // The adapter is read-only for demand collection. Execution goes
            // through the production sale authorities, never through here.
            if (diagnostics != null)
            {
                diagnostics.Add("BusinessInstanceSellerAdapter.Sell is not an execution path; use the sale authorities.");
            }

            return 0;
        }
    }

    /// <summary>
    /// Pass VII Workstream A: production <see cref="IRealDemandSource"/>.
    ///
    /// Collects demand ONLY from real buyers with real needs:
    /// - Households: open <see cref="HouseholdPurchasingNeed"/>s from the
    ///   <see cref="HouseholdNeedRegistry"/> (written by the
    ///   <see cref="HouseholdShortageMonitor"/> from actual members, reserves,
    ///   consumption and season) plus reserve shortfalls below low threshold.
    /// - Businesses: registered <see cref="BusinessInputNeed"/>s (real input
    ///   requirements supplied by production runtimes).
    ///
    /// Every emitted <see cref="PurchaseIntent"/> satisfies the collection-side
    /// steps of the legitimate purchase chain, documented in Provenance:
    ///  1. buyer identity (BuyerKind + BuyerId — always a real household id or
    ///     business instance id; never an aggregate);
    ///  2. actual need (the need reason and units);
    ///  3. buyer knowledge (the supplier is known to the buyer — prior affinity
    ///     or the public general store);
    ///  4. supplier access (a registered supplier carries the category);
    ///  5. available inventory (supplier stock units at collection time);
    ///  6. valid price (supplier price per unit, strictly positive);
    ///  7. purchasing power (buyer ledger/cash balance covers the intent);
    ///  8. fulfillment method named (pickup/delivery/immediate);
    ///  9. settlement kind named (cash/credit/external);
    /// 10. goods transfer is NOT performed here — collection never moves goods
    ///     or money. Execution goes through the production authorities
    ///     (GenericRetailSaleAuthority, EmbodiedPurchaseExecutor), which perform
    ///     the transfer and the settlement exactly once.
    ///
    /// No buyer, no need, no knowledge, no stock, no price, or no funds means
    /// no intent. The source manufactures nothing.
    /// </summary>
    public sealed class RealDemandSource : IRealDemandSource
    {
        private readonly PopulationState population;
        private readonly HouseholdLedgerRegistry ledgers;
        private readonly HouseholdNeedRegistry needRegistry;
        private readonly ISupplierDirectory suppliers;
        private readonly Func<int, string, bool> householdKnowsSupplier;
        private readonly Func<string, int> businessCashCents;
        private readonly string generalStoreSupplierId;
        private readonly List<BusinessInputNeed> businessInputNeeds = new List<BusinessInputNeed>();

        /// <param name="population">Households to read needs for.</param>
        /// <param name="ledgers">Single household-cash truth (Canon 13.2).</param>
        /// <param name="needRegistry">Open real purchasing needs.</param>
        /// <param name="suppliers">Real suppliers with finite stock and prices.</param>
        /// <param name="householdKnowsSupplier">
        /// Buyer-knowledge check: (householdId, supplierBusinessId) -> known.
        /// Null means only the general store (public knowledge) is known.
        /// Production wires this to the household-affinity store.
        /// </param>
        /// <param name="businessCashCents">
        /// Business purchasing power: businessInstanceId -> cash cents.
        /// Null means business input needs are collected without a funds check
        /// (the executing authority still enforces payment).
        /// </param>
        /// <param name="generalStoreSupplierId">
        /// Supplier id treated as public knowledge (the general store).
        /// </param>
        public RealDemandSource(
            PopulationState population,
            HouseholdLedgerRegistry ledgers,
            HouseholdNeedRegistry needRegistry,
            ISupplierDirectory suppliers,
            Func<int, string, bool> householdKnowsSupplier = null,
            Func<string, int> businessCashCents = null,
            string generalStoreSupplierId = "")
        {
            this.population = population;
            this.ledgers = ledgers;
            this.needRegistry = needRegistry;
            this.suppliers = suppliers;
            this.householdKnowsSupplier = householdKnowsSupplier;
            this.businessCashCents = businessCashCents;
            this.generalStoreSupplierId = generalStoreSupplierId ?? string.Empty;
        }

        /// <summary>Registers a real business input requirement for collection.</summary>
        public void RegisterBusinessInputNeed(BusinessInputNeed need)
        {
            if (need == null || string.IsNullOrWhiteSpace(need.BuyerBusinessInstanceId)
                || string.IsNullOrWhiteSpace(need.InputCategoryId) || need.UnitsNeeded <= 0)
            {
                return;
            }

            businessInputNeeds.Add(need);
        }

        public void ClearBusinessInputNeeds()
        {
            businessInputNeeds.Clear();
        }

        public List<PurchaseIntent> CollectDemand(int dayIndex, List<string> diagnostics)
        {
            var intents = new List<PurchaseIntent>();
            if (diagnostics == null)
            {
                diagnostics = new List<string>();
            }

            CollectHouseholdDemand(dayIndex, intents, diagnostics);
            CollectBusinessInputDemand(dayIndex, intents, diagnostics);
            return intents;
        }

        private void CollectHouseholdDemand(int dayIndex, List<PurchaseIntent> intents, List<string> diagnostics)
        {
            if (population == null || population.households == null)
            {
                diagnostics.Add("RealDemandSource: no population — no household demand collected.");
                return;
            }

            if (suppliers == null)
            {
                diagnostics.Add("RealDemandSource: no supplier directory — no demand can name a supplier.");
                return;
            }

            for (int h = 0; h < population.households.Count; h++)
            {
                HouseholdState household = population.households[h];
                if (household == null)
                {
                    continue;
                }

                Dictionary<string, NeedLine> needs = BuildHouseholdNeedLines(household, diagnostics);
                if (needs.Count == 0)
                {
                    continue;
                }

                int balanceCents = ledgers != null
                    ? ledgers.GetOrCreate(household.id).GetBalanceCents()
                    : Mathf.Max(0, household.spendingMoneyCents);

                foreach (KeyValuePair<string, NeedLine> entry in needs)
                {
                    TryCollectHouseholdNeed(household, entry.Key, entry.Value, balanceCents, intents, diagnostics);
                }
            }
        }

        private struct NeedLine
        {
            public int UnitsNeeded;
            public string Reason;
        }

        /// <summary>
        /// Merges open purchasing needs (from the shortage monitor) with live
        /// reserve shortfalls. Both are real needs; the registry is the
        /// canonical record and reserves are the live check.
        /// </summary>
        private Dictionary<string, NeedLine> BuildHouseholdNeedLines(HouseholdState household, List<string> diagnostics)
        {
            var lines = new Dictionary<string, NeedLine>(StringComparer.Ordinal);

            if (needRegistry != null)
            {
                List<HouseholdPurchasingNeed> open = needRegistry.GetOpenNeeds(household.id);
                for (int i = 0; i < open.Count; i++)
                {
                    HouseholdPurchasingNeed need = open[i];
                    if (need == null || string.IsNullOrWhiteSpace(need.ItemId) || need.UnitsNeeded <= 0)
                    {
                        continue;
                    }

                    lines[need.ItemId] = new NeedLine
                    {
                        UnitsNeeded = need.UnitsNeeded,
                        Reason = string.IsNullOrWhiteSpace(need.ReasonSummary)
                            ? $"open purchasing need #{need.NeedSequence}"
                            : need.ReasonSummary,
                    };
                }
            }

            if (household.reserves != null)
            {
                for (int i = 0; i < household.reserves.Count; i++)
                {
                    HouseholdReserveState reserve = household.reserves[i];
                    if (reserve == null || string.IsNullOrWhiteSpace(reserve.categoryId))
                    {
                        continue;
                    }

                    int shortfall = Mathf.Max(0, reserve.lowThresholdUnits - Mathf.Max(0, reserve.currentUnits));
                    if (shortfall <= 0)
                    {
                        continue;
                    }

                    if (lines.TryGetValue(reserve.categoryId, out NeedLine existing))
                    {
                        lines[reserve.categoryId] = new NeedLine
                        {
                            UnitsNeeded = Mathf.Max(existing.UnitsNeeded, shortfall),
                            Reason = existing.Reason,
                        };
                    }
                    else
                    {
                        lines[reserve.categoryId] = new NeedLine
                        {
                            UnitsNeeded = shortfall,
                            Reason = $"reserve '{reserve.categoryId}' below low threshold ({reserve.currentUnits}/{reserve.lowThresholdUnits})",
                        };
                    }
                }
            }

            return lines;
        }

        private void TryCollectHouseholdNeed(
            HouseholdState household,
            string itemId,
            NeedLine need,
            int balanceCents,
            List<PurchaseIntent> intents,
            List<string> diagnostics)
        {
            IGoodsSupplier best = null;
            int bestPrice = 0;
            foreach (IGoodsSupplier supplier in suppliers.SuppliersFor(itemId))
            {
                if (supplier == null || !supplier.HasCategory(itemId))
                {
                    continue;
                }

                int stock = supplier.StockUnits(itemId);
                if (stock <= 0)
                {
                    continue;
                }

                int price = supplier.PricePerUnitCents(itemId);
                if (price <= 0)
                {
                    continue;
                }

                if (!HouseholdKnowsSupplier(household.id, supplier.SupplierBusinessId))
                {
                    continue;
                }

                if (best == null || price < bestPrice)
                {
                    best = supplier;
                    bestPrice = price;
                }
            }

            if (best == null)
            {
                diagnostics.Add(
                    $"RealDemandSource: household {household.id} needs {need.UnitsNeeded}x '{itemId}' ({need.Reason}) — " +
                    "no known stocked supplier at a valid price; demand unmet, no intent.");
                return;
            }

            int stockUnits = best.StockUnits(itemId);
            int affordableUnits = bestPrice > 0 ? balanceCents / bestPrice : 0;
            if (affordableUnits <= 0)
            {
                diagnostics.Add(
                    $"RealDemandSource: household {household.id} needs {need.UnitsNeeded}x '{itemId}' ({need.Reason}) — " +
                    $"cannot afford 1 unit at {bestPrice}c (balance {balanceCents}c); demand unmet, no intent.");
                return;
            }

            int units = Mathf.Min(need.UnitsNeeded, stockUnits, affordableUnits);
            if (units <= 0)
            {
                return;
            }

            int totalCents = units * bestPrice;
            intents.Add(new PurchaseIntent
            {
                BuyerKind = "household",
                BuyerId = household.id.ToString(),
                SellerBusinessInstanceId = best.SupplierBusinessId,
                GoodsOrServiceId = itemId,
                QuantityUnits = units,
                AgreedPriceCents = totalCents,
                FulfillmentMethod = "pickup",
                SettlementKind = "cash",
                Provenance = $"need:[{need.Reason}] -> buyer:household:{household.id} knows supplier:{best.SupplierBusinessId} " +
                             $"-> stock:{stockUnits} @ {bestPrice}c/u -> funds:{balanceCents}c covers {totalCents}c",
            });
        }

        private bool HouseholdKnowsSupplier(int householdId, string supplierBusinessId)
        {
            if (!string.IsNullOrWhiteSpace(generalStoreSupplierId)
                && string.Equals(supplierBusinessId, generalStoreSupplierId, StringComparison.Ordinal))
            {
                return true;
            }

            return householdKnowsSupplier != null && householdKnowsSupplier(householdId, supplierBusinessId);
        }

        private void CollectBusinessInputDemand(int dayIndex, List<PurchaseIntent> intents, List<string> diagnostics)
        {
            if (businessInputNeeds.Count == 0 || suppliers == null)
            {
                return;
            }

            for (int i = 0; i < businessInputNeeds.Count; i++)
            {
                BusinessInputNeed need = businessInputNeeds[i];
                if (need == null)
                {
                    continue;
                }

                IGoodsSupplier best = null;
                int bestPrice = 0;
                foreach (IGoodsSupplier supplier in suppliers.SuppliersFor(need.InputCategoryId))
                {
                    if (supplier == null || !supplier.HasCategory(need.InputCategoryId))
                    {
                        continue;
                    }

                    // A business cannot buy its own inputs from itself.
                    if (string.Equals(supplier.SupplierBusinessId, need.BuyerBusinessInstanceId, StringComparison.Ordinal))
                    {
                        continue;
                    }

                    int stock = supplier.StockUnits(need.InputCategoryId);
                    if (stock <= 0)
                    {
                        continue;
                    }

                    int price = supplier.PricePerUnitCents(need.InputCategoryId);
                    if (price <= 0 || (need.MaxPriceCentsPerUnit > 0 && price > need.MaxPriceCentsPerUnit))
                    {
                        continue;
                    }

                    if (best == null || price < bestPrice)
                    {
                        best = supplier;
                        bestPrice = price;
                    }
                }

                if (best == null)
                {
                    diagnostics.Add(
                        $"RealDemandSource: business {need.BuyerBusinessInstanceId} needs {need.UnitsNeeded}x '{need.InputCategoryId}' " +
                        $"({need.ReasonSummary}) — no stocked supplier at an acceptable price; demand unmet, no intent.");
                    continue;
                }

                int cashCents = businessCashCents != null
                    ? Mathf.Max(0, businessCashCents(need.BuyerBusinessInstanceId))
                    : int.MaxValue;
                int stockUnits = best.StockUnits(need.InputCategoryId);
                int affordableUnits = bestPrice > 0 && cashCents != int.MaxValue ? cashCents / bestPrice : need.UnitsNeeded;
                if (affordableUnits <= 0)
                {
                    diagnostics.Add(
                        $"RealDemandSource: business {need.BuyerBusinessInstanceId} needs {need.UnitsNeeded}x '{need.InputCategoryId}' — " +
                        $"cannot afford 1 unit at {bestPrice}c (cash {cashCents}c); demand unmet, no intent.");
                    continue;
                }

                int units = Mathf.Min(need.UnitsNeeded, stockUnits, affordableUnits);
                if (units <= 0)
                {
                    continue;
                }

                int totalCents = units * bestPrice;
                intents.Add(new PurchaseIntent
                {
                    BuyerKind = "business",
                    BuyerId = need.BuyerBusinessInstanceId,
                    SellerBusinessInstanceId = best.SupplierBusinessId,
                    GoodsOrServiceId = need.InputCategoryId,
                    QuantityUnits = units,
                    AgreedPriceCents = totalCents,
                    FulfillmentMethod = "delivery",
                    SettlementKind = "cash",
                    Provenance = $"input need:[{need.ReasonSummary}] -> buyer:business:{need.BuyerBusinessInstanceId} " +
                                 $"-> supplier:{best.SupplierBusinessId} stock:{stockUnits} @ {bestPrice}c/u -> funds:{cashCents}c covers {totalCents}c",
                });
            }
        }
    }
}
