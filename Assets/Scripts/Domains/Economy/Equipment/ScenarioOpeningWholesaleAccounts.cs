using System;
using System.Collections.Generic;
using LandLedgers.Economy.Businesses.Bakery;
using LandLedgers.Population;

namespace LandLedgers.Economy.Equipment
{
    /// <summary>
    /// Plan 01 (2026-11-06): books the scenario-authored wholesale
    /// accounts for one bakery seller at NewGame. Mirrors
    /// ScenarioOpeningEquipmentEndowment: per-seller-key, scenario-owned,
    /// zero by default, NewGame-only, idempotent via the
    /// BusinessRuntimeState endowment marker, and never changes cash.
    /// A seller whose business key has no wholesale-account entry gets
    /// nothing from the config; a config that lists no wholesale
    /// accounts at all books the single demand-derived fallback account
    /// (§5 of the plan) so the bakery is not silently blocked, labelled
    /// calibration with D-1 open. The account id is assigned by
    /// <see cref="BakeryShopRuntime.AddWholesaleAccount"/>. No flour,
    /// no fuel, no bread: an account alone bakes nothing — it only makes
    /// the daily driver (BakeryWholesaleDailyDriver) able to plan.
    /// </summary>
    public static class ScenarioOpeningWholesaleAccounts
    {
        /// <summary>
        /// Demand-derived fallback price: 2c per loaf. Documented
        /// evidence, not invention — Docs/Review2VerticalD1Review.md
        /// ("wholesale 2c, retail 5c") and
        /// Docs/Regional_Item_Coverage_Register_2026-11-05.md ("2c
        /// wholesale"); the transfer tests pass 2c as the store
        /// definition's evidence value.
        /// </summary>
        public const int FallbackPricePerUnitCents = 2;

        public static bool TryApply(
            DevelopmentTownConfiguration configuration,
            string sellerBusinessKey,
            BakeryShopRuntime sellerRuntime,
            BusinessRuntimeState sellerBusinessRuntimeState,
            BusinessInstanceState buyerBusiness,
            int dayIndex,
            List<string> diag,
            out string report,
            out string error)
        {
            report = string.Empty;
            error = string.Empty;
            diag = diag ?? new List<string>();
            if (configuration == null || string.IsNullOrWhiteSpace(sellerBusinessKey)
                || !configuration.TryGetBusiness(sellerBusinessKey, out _)
                || sellerRuntime == null || sellerBusinessRuntimeState == null
                || buyerBusiness == null || buyerBusiness.RuntimeState == null
                || buyerBusiness.BusinessType != BusinessType.GeneralStore)
            {
                error = "Development wholesale accounts require a valid configuration, a configured seller business, a bakery runtime with a business runtime state, and a General Store buyer business with a runtime state.";
                return false;
            }

            string sellerInstanceId = sellerRuntime.BusinessInstanceId;
            if (string.IsNullOrWhiteSpace(sellerInstanceId))
            {
                error = "Development wholesale accounts require a bakery runtime with a business instance id.";
                return false;
            }

            // Collect this seller's entries and validate everything
            // before mutating anything, so a refusal books nothing.
            var entries = new List<DevelopmentTownConfiguration.BakeryWholesaleAccountDefinition>();
            bool hasAnyEntry = false;
            foreach (DevelopmentTownConfiguration.BakeryWholesaleAccountDefinition candidate in configuration.WholesaleAccounts)
            {
                if (candidate == null) continue;
                hasAnyEntry = true;
                if (!string.Equals(candidate.sellerBusinessKey, sellerBusinessKey, StringComparison.OrdinalIgnoreCase)) continue;
                entries.Add(candidate);
            }

            foreach (DevelopmentTownConfiguration.BakeryWholesaleAccountDefinition entry in entries)
            {
                if (string.IsNullOrWhiteSpace(entry.buyerBusinessKey)
                    || !configuration.TryGetBusiness(entry.buyerBusinessKey, out DevelopmentTownConfiguration.BusinessDefinition buyerDefinition))
                {
                    error = $"Wholesale account for '{sellerBusinessKey}' names an unknown buyer business key '{entry.buyerBusinessKey}' — nothing was booked.";
                    return false;
                }
                if (!string.Equals(buyerDefinition.operationId, "general-store", StringComparison.OrdinalIgnoreCase))
                {
                    error = $"Wholesale account for '{sellerBusinessKey}' names buyer '{entry.buyerBusinessKey}', which is not the General Store — nothing was booked.";
                    return false;
                }
                if (!BakeryBreadCatalog.IsKnownProduct(entry.productId))
                {
                    error = $"Wholesale account for '{sellerBusinessKey}' names an unknown product '{entry.productId}' — nothing was booked.";
                    return false;
                }
                if (entry.unitsPerDelivery <= 0)
                {
                    error = $"Wholesale account for '{sellerBusinessKey}' needs a positive units per delivery — nothing was booked.";
                    return false;
                }
                if (entry.pricePerUnitCents < 0)
                {
                    error = $"Wholesale account for '{sellerBusinessKey}' needs a non-negative price per unit — nothing was booked.";
                    return false;
                }
            }

            string endowmentId = $"development-town-wholesale-accounts:{configuration.ConfigurationId}:{sellerBusinessKey}:{sellerInstanceId}";
            if (sellerBusinessRuntimeState.HasAppliedScenarioEndowment(endowmentId))
            {
                report = $"Wholesale accounts already recorded for {sellerInstanceId}; no account re-booked.";
                return true;
            }

            var booked = new List<string>();

            if (!hasAnyEntry)
            {
                // Demand-derived fallback (plan 01 §5), only when the
                // config lists no wholesale accounts at all. Derivation:
                // day-1 household demand was 24 loaves (4 households x 6
                // — STRESS_TEST_ANALYSIS.md P0 evidence); one batch yields
                // exactly 24 loaves (BakeryBreadCatalog.BreadYieldPerBatch),
                // so 24/day is exactly one batch/day and
                // PlanWholesaleBatches derives the batch count from the
                // account (ceil(24/24) = 1) — the driver never chooses a
                // batch count itself. Labelled calibration; D-1 open.
                var fallback = new BakeryWholesaleAccount
                {
                    BuyerBusinessId = buyerBusiness.InstanceId,
                    BuyerType = BusinessType.GeneralStore,
                    ProductId = BakeryBreadCatalog.BreadLoafId,
                    UnitsPerDelivery = BakeryBreadCatalog.RetailReferenceDemandUnits,
                    DeliveryCadenceDays = 1,
                    PricePerUnitCents = FallbackPricePerUnitCents,
                    Freshness = BakeryWholesaleFreshness.FreshOrDayOld,
                    NextDeliveryDayIndex = dayIndex,
                };
                if (!BookIfNew(sellerRuntime, fallback, diag, booked, out error)) return false;
                diag.Add("ScenarioOpeningWholesaleAccounts: fallback account is demand-derived calibration (24 units/day = one 24-loaf batch/day, 2c); D-1 open — override in scenario config.");
            }
            else
            {
                foreach (DevelopmentTownConfiguration.BakeryWholesaleAccountDefinition entry in entries)
                {
                    var account = new BakeryWholesaleAccount
                    {
                        BuyerBusinessId = buyerBusiness.InstanceId,
                        BuyerType = BusinessType.GeneralStore,
                        ProductId = entry.productId.Trim(),
                        UnitsPerDelivery = entry.unitsPerDelivery,
                        DeliveryCadenceDays = Math.Max(1, entry.deliveryCadenceDays),
                        PricePerUnitCents = entry.pricePerUnitCents,
                        Freshness = entry.freshness,
                        NextDeliveryDayIndex = entry.firstDeliveryDayIndex,
                    };
                    if (!BookIfNew(sellerRuntime, account, diag, booked, out error)) return false;
                }
            }

            if (!sellerBusinessRuntimeState.RecordAppliedScenarioEndowment(endowmentId))
            {
                error = $"Could not record wholesale account allocation '{endowmentId}'.";
                return false;
            }

            report = booked.Count == 0
                ? $"No wholesale accounts are configured for seller '{sellerBusinessKey}' ({sellerInstanceId}); nothing was booked."
                : $"Booked wholesale account(s) for '{sellerBusinessKey}' ({sellerInstanceId}): {string.Join("; ", booked)}."
                    + (!hasAnyEntry ? " (demand-derived fallback; D-1 open — override in scenario config)" : string.Empty);
            return true;
        }

        private static bool BookIfNew(
            BakeryShopRuntime sellerRuntime,
            BakeryWholesaleAccount account,
            List<string> diag,
            List<string> booked,
            out string error)
        {
            error = string.Empty;

            // Defense in depth, independent of the marker (the marker is
            // persisted, but a runtime restored without it must still
            // never double-book): skip an account the bakery already
            // holds for the same buyer + product.
            foreach (BakeryWholesaleAccount existing in sellerRuntime.WholesaleAccounts)
            {
                if (existing != null
                    && string.Equals(existing.BuyerBusinessId, account.BuyerBusinessId, StringComparison.Ordinal)
                    && string.Equals(existing.ProductId, account.ProductId, StringComparison.Ordinal))
                {
                    diag.Add($"ScenarioOpeningWholesaleAccounts: bakery {sellerRuntime.BusinessInstanceId} already holds a wholesale account for {account.BuyerBusinessId} ({account.ProductId}); not re-booked.");
                    return true;
                }
            }

            string refusal = sellerRuntime.AddWholesaleAccount(account, diag);
            if (!string.IsNullOrWhiteSpace(refusal))
            {
                error = $"Wholesale account for '{account.BuyerBusinessId}' refused: {refusal}";
                return false;
            }
            booked.Add($"account {account.AccountId} — {account.UnitsPerDelivery}x {account.ProductId} " +
                $"every {account.DeliveryCadenceDays} day(s) to {account.BuyerBusinessId} " +
                $"at {account.PricePerUnitCents}c/unit, {account.Freshness}, first due day {account.NextDeliveryDayIndex}");
            return true;
        }
    }
}
