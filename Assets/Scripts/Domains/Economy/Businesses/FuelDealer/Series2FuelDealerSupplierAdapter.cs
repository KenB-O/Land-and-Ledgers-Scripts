// Series 2 Pass 7 (selling): the Fuel Dealer as a real supplier in the
// SupplierDirectory, over the real FuelYardByproductIntake (Pass 2's
// dispense API). This adapter does NOT create a Fuel Dealer runtime —
// there is still no per-instance Fuel Dealer business package, and
// Pass 8 (freight + fuel chain) owns that. What this adapter owns is
// the selling surface over intake lots that already exist with
// provenance, so a household can buy fuel from a named dealer through
// the embodied shopping loop instead of the dealer being invisible
// to shoppers. Production binds this adapter to an existing formed
// BusinessInstanceState; the adapter never creates that business.
//
// Item identity: households buy household item "fuel_wood"
// (HouseholdItemCatalog.FuelWoodId). Intake lots hold fuel-wood units.
// The conversion between intake units and household bundles is a
// caller parameter (FuelUnitsPerHouseholdUnit, default 1), NOT a
// decided game rate: the bundle-to-reserve conversion is still an
// open Pass 8 decision, and this adapter must not silently fix it.
// With the default, 1 intake unit = 1 household fuel_wood unit, and
// every test in this pass uses that default and says so.
//
// Pricing: the dealer price per household unit is caller-supplied
// (a Pass 3 margin price in production, a test parameter in tests),
// never a price list in this file. The price is uniform, so the
// loop's uniform-price settlement and this adapter's posted revenue
// agree exactly: revenue = units sold x price.
//
// Dispense semantics are the intake's, not this adapter's: FIFO with
// provenance, shortfall dispenses NOTHING (never a partial lot), and
// emptied lots are removed and never re-sold. This adapter never
// deducts FIFO in place (the ThreeBusinessSlice bridge pattern, left
// untouched for its orchestrator tests).
//
// Cash: business cash moves only through the caller-supplied
// IBusinessCashPort, which production maps to BusinessRuntimeState.
// Trade credit, when the caller enables it with a limit, is a real
// FinancialObligationAuthority obligation created by the loop before
// SellItem is called; this adapter then posts NO cash for a credit
// sale. Credit eligibility is checked BEFORE any dispense, so a
// credit sale this dealer does not offer refuses with stock intact.
using System;
using System.Collections.Generic;
using LandLedgers.Economy;
using LandLedgers.Economy.Businesses.GeneralStore;
using LandLedgers.Economy.Businesses.Sawmill;
using LandLedgers.Population;

namespace LandLedgers.Economy.Businesses.FuelDealer
{
    /// <summary>Fuel Dealer supplier adapter over provenance-tracked intake lots.</summary>
    public sealed class Series2FuelDealerSupplierAdapter : IItemResolvingSupplier, IItemCatalogSupplier, IOffersTradeCredit
    {
        private readonly FuelYardByproductIntake intake;
        private readonly IBusinessCashPort cash;
        private readonly Func<string> tradingCheck;
        private readonly List<string> log = new List<string>();
        private string lastReceiptId = string.Empty;
        private int receiptSequence;

        public Series2FuelDealerSupplierAdapter(
            FuelYardByproductIntake intake,
            IBusinessCashPort cash,
            string dealerBusinessId,
            string dealerName,
            string locationId,
            int pricePerHouseholdUnitCents,
            Func<string> tradingCheck = null)
        {
            this.intake = intake ?? throw new ArgumentNullException(nameof(intake));
            this.cash = cash ?? throw new ArgumentNullException(nameof(cash));
            SupplierBusinessId = dealerBusinessId ?? string.Empty;
            SupplierName = dealerName ?? string.Empty;
            LocationId = locationId ?? string.Empty;
            PricePerHouseholdUnitCents = Math.Max(0, pricePerHouseholdUnitCents);
            this.tradingCheck = tradingCheck;
        }

        public IReadOnlyList<string> Log => log;

        /// <summary>Dealer price per household fuel_wood unit, caller-supplied, never canon.</summary>
        public int PricePerHouseholdUnitCents { get; set; }

        /// <summary>Intake units per household unit. Default 1; the bundle conversion is a Pass 8 decision.</summary>
        public int FuelUnitsPerHouseholdUnit { get; set; } = 1;

        /// <summary>Trade credit offered by this dealer. Off by default; a caller limit is required to use it.</summary>
        public bool OffersTradeCreditValue { get; set; } = false;

        /// <summary>Trade-credit cap in cents. 0 means no credit sale is accepted even when enabled.</summary>
        public int TradeCreditLimitCentsValue { get; set; } = 0;

        public string SupplierBusinessId { get; }
        public string SupplierName { get; }
        public string LocationId { get; }
        public string LastReceiptId => lastReceiptId;
        public int IntakeFuelWoodUnits => intake.TotalFuelWoodUnitsFor(SupplierBusinessId);

        public static void RegisterIn(SupplierDirectory directory, Series2FuelDealerSupplierAdapter adapter)
        {
            if (directory == null || adapter == null) return;
            directory.Register(adapter);
        }

        /// <summary>
        /// Builds the retail surface from an already-formed Fuel Dealer. The
        /// business identity, owner, premises and financial runtime are all
        /// validated from that live instance; this method never creates any of
        /// them and never seeds its intake.
        /// </summary>
        public static bool TryCreateForBusiness(
            BusinessInstanceState business,
            FuelYardByproductIntake intake,
            IBusinessCashPort cash,
            int configuredPricePerBundleCents,
            out Series2FuelDealerSupplierAdapter adapter,
            out string refusal)
        {
            adapter = null;
            refusal = string.Empty;
            if (business == null)
            {
                refusal = "Fuel Dealer business instance is missing.";
                return false;
            }
            if (business.BusinessType != BusinessType.FuelDealer)
            {
                refusal = $"Business '{business.InstanceId}' is {business.BusinessType}, not Fuel Dealer.";
                return false;
            }
            if (string.IsNullOrWhiteSpace(business.InstanceId)
                || business.Owner == null
                || business.AssignedBuildingId < 0
                || business.RuntimeState == null
                || !string.Equals(business.RuntimeState.BusinessId, business.InstanceId, StringComparison.Ordinal))
            {
                refusal = $"Fuel Dealer '{business.InstanceId}' lacks a stable identity, owner, assigned premises, or matching business runtime.";
                return false;
            }
            if (intake == null || cash == null)
            {
                refusal = "Fuel Dealer requires its existing yard inventory and business cash authorities.";
                return false;
            }
            if (cash is IBusinessIdentityCashPort identityCash
                && !string.Equals(identityCash.BusinessId, business.InstanceId, StringComparison.Ordinal))
            {
                refusal = $"Fuel Dealer cash authority belongs to '{identityCash.BusinessId}', not '{business.InstanceId}'.";
                return false;
            }
            if (configuredPricePerBundleCents <= 0)
            {
                refusal = "Fuel Dealer has no positive configured sale price; no retail offer registered.";
                return false;
            }

            adapter = new Series2FuelDealerSupplierAdapter(
                intake,
                cash,
                business.InstanceId,
                business.RuntimeDisplayName,
                $"building:{business.AssignedBuildingId}",
                configuredPricePerBundleCents);
            // The intake quantity-to-household-bundle factor remains an
            // explicit configuration decision. Do not apply this adapter's
            // legacy convenience default to live gameplay.
            adapter.FuelUnitsPerHouseholdUnit = 0;
            return true;
        }

        // ---- IGoodsSupplier (category surface: fuel_wood) ----

        public bool HasCategory(string categoryId)
        {
            return string.Equals(categoryId, "fuel_wood", StringComparison.OrdinalIgnoreCase);
        }

        public int StockUnits(string categoryId)
        {
            return HasCategory(categoryId) ? ItemStockUnits(HouseholdItemCatalog.FuelWoodId) : 0;
        }

        public int PricePerUnitCents(string categoryId)
        {
            return HasCategory(categoryId) ? ItemPricePerUnitCents(HouseholdItemCatalog.FuelWoodId) : 0;
        }

        public int Sell(string categoryId, int requestedUnits, int dayIndex, List<string> diagnostics)
        {
            if (!HasCategory(categoryId)) return 0;
            var settlement = new ShoppingSettlement { Mode = ShoppingSettlementMode.CashFromLedger };
            return SellItem(HouseholdItemCatalog.FuelWoodId, requestedUnits, dayIndex, settlement, diagnostics);
        }

        // ---- IItemResolvingSupplier (item surface: household "fuel_wood" only) ----

        public bool OffersItem(string itemId)
        {
            return RecognizesItem(itemId)
                && ItemStockUnits(itemId) > 0
                && ItemPricePerUnitCents(itemId) > 0;
        }

        public bool RecognizesItem(string itemId)
        {
            return string.Equals(itemId, HouseholdItemCatalog.FuelWoodId, StringComparison.OrdinalIgnoreCase);
        }

        public string CategoryForItem(string itemId)
        {
            return OffersItem(itemId) ? "fuel_wood" : string.Empty;
        }

        public int ItemStockUnits(string itemId)
        {
            if (!RecognizesItem(itemId)) return 0;
            int perUnit = FuelUnitsPerHouseholdUnit;
            if (perUnit <= 0) return 0;
            return intake.TotalFuelWoodUnitsFor(SupplierBusinessId) / perUnit;
        }

        public int ItemPricePerUnitCents(string itemId)
        {
            return RecognizesItem(itemId) ? Math.Max(0, PricePerHouseholdUnitCents) : 0;
        }

        public string CheckTradingCapability(int absoluteDayIndex, int minuteOfDay)
        {
            if (tradingCheck != null) return tradingCheck();
            return null;
        }

        public int SellItem(
            string itemId, int requestedUnits, int dayIndex, ShoppingSettlement settlement, List<string> diagnostics)
        {
            if (!RecognizesItem(itemId) || requestedUnits <= 0) return 0;
            if (FuelUnitsPerHouseholdUnit <= 0)
            {
                diagnostics?.Add($"'{SupplierName}' cannot sell household fuel bundles: intake-to-bundle quantity is not configured; stock and cash untouched.");
                return 0;
            }
            string tradingProblem = CheckTradingCapability(dayIndex, 0);
            if (tradingProblem != null)
            {
                diagnostics?.Add($"'{SupplierName}' cannot trade: {tradingProblem}");
                return 0;
            }
            int unitPrice = ItemPricePerUnitCents(itemId);
            if (unitPrice <= 0)
            {
                diagnostics?.Add($"'{SupplierName}' has no legitimate fuel price — free goods are not commerce.");
                return 0;
            }

            if (ItemStockUnits(itemId) < requestedUnits)
            {
                diagnostics?.Add($"'{SupplierName}' cannot fill {requestedUnits}u fuel_wood: real stock exhausted or short; no sale settled.");
                return 0;
            }
            if (settlement != null && settlement.Mode == ShoppingSettlementMode.TradeCreditObligation)
            {
                if (!OffersTradeCreditValue || TradeCreditLimitCentsValue <= 0 || requestedUnits * unitPrice > TradeCreditLimitCentsValue)
                {
                    diagnostics?.Add($"'{SupplierName}' does not offer trade credit covering {requestedUnits * unitPrice}c — sale refused, stock untouched.");
                    return 0;
                }
            }

            int intakeUnits = requestedUnits * FuelUnitsPerHouseholdUnit;
            List<FuelYardByproductIntake.FuelYardDispenseLine> lines =
                intake.TryDispenseUnits(SupplierBusinessId, intakeUnits, dayIndex, diagnostics);
            if (lines == null || lines.Count == 0)
            {
                diagnostics?.Add($"'{SupplierName}' cannot fill {requestedUnits}u fuel_wood: real stock exhausted or short — nothing dispensed, no sale faked.");
                return 0;
            }
            int dispensed = 0;
            var provenances = new List<string>();
            foreach (FuelYardByproductIntake.FuelYardDispenseLine line in lines)
            {
                if (line == null) continue;
                dispensed += Math.Max(0, line.UnitsTaken);
                if (!string.IsNullOrWhiteSpace(line.ProvenanceChain)) provenances.Add(line.ProvenanceChain);
            }
            int sold = dispensed / FuelUnitsPerHouseholdUnit;
            if (sold <= 0) return 0;
            int revenue = sold * unitPrice;

            if (settlement != null && settlement.Mode == ShoppingSettlementMode.TradeCreditObligation)
            {
                log.Add($"Day {dayIndex}: sold {sold}u fuel_wood on trade credit {settlement.ObligationId} ({revenue}c receivable, cash untouched).");
            }
            else
            {
                string problem = cash.Credit(revenue, $"fuel dealer retail sale {sold}u fuel_wood");
                if (problem != null)
                    diagnostics?.Add($"'{SupplierName}' WARNING: dealer cash port refused {revenue}c credit ({problem}) — dispense already moved; investigate.");
                log.Add($"Day {dayIndex}: sold {sold}u fuel_wood for {revenue}c cash (provenance: {string.Join(" | ", provenances)}).");
            }

            lastReceiptId = $"rcpt-{SupplierBusinessId}-{dayIndex}-{receiptSequence++}";
            diagnostics?.Add($"'{SupplierName}' sold {sold}u fuel_wood ({lastReceiptId}).");
            return sold;
        }

        // ---- IOffersTradeCredit ----

        public bool OffersTradeCredit => OffersTradeCreditValue;
        public int TradeCreditLimitCents => Math.Max(0, TradeCreditLimitCentsValue);
    }
}
