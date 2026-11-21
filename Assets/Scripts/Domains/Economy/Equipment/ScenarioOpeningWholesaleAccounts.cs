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
    /// nothing from the config. Lane 9a (§19 retail-first, H-1): there is
    /// NO demand-derived fallback — a config that lists no wholesale
    /// accounts at all books nothing, so New Game creates no wholesale
    /// account and the bakery sells direct to consumers. Wholesale after
    /// New Game is opt-in only: an explicit config entry, or a runtime
    /// decision recorded through
    /// <see cref="BakeryShopRuntime.AddWholesaleAccount"/>. Accounts
    /// already booked in existing saves stay booked (the endowment
    /// marker prevents re-booking; removing the creation code un-books
    /// nothing). The account id is assigned by
    /// <see cref="BakeryShopRuntime.AddWholesaleAccount"/>. No flour,
    /// no fuel, no bread: an account alone bakes nothing — it only makes
    /// the daily driver (BakeryWholesaleDailyDriver) able to plan.
    /// </summary>
    public static class ScenarioOpeningWholesaleAccounts
    {

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
                || !configuration.TryGetBusiness(sellerBusinessKey, out DevelopmentTownConfiguration.BusinessDefinition sellerBusiness)
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
            // K2: direct owner-record iteration. The deleted
            // WholesaleAccounts view projected every business's opening
            // wholesaleAgreements and stamped the seller key onto them.
            // This seller's own opening record is the entry set. Lane 9a
            // (H-1): there is no demand-derived fallback — a seller with
            // no entries of its own books nothing, whether or not any
            // other seller has entries.
            var entries = new List<DevelopmentTownConfiguration.BakeryWholesaleAccountDefinition>();
            if (sellerBusiness?.opening?.wholesaleAgreements != null)
            {
                foreach (DevelopmentTownConfiguration.BakeryWholesaleAccountDefinition candidate in sellerBusiness.opening.wholesaleAgreements)
                {
                    if (candidate == null) continue;
                    entries.Add(candidate);
                }
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

            // Lane 9a (H-1): only explicit config entries book an
            // account. A config with no entries books nothing — no
            // demand-derived fallback exists anymore.
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
                if (!BookIfNew(sellerRuntime, account, dayIndex, diag, booked, out error)) return false;
            }

            if (!sellerBusinessRuntimeState.RecordAppliedScenarioEndowment(endowmentId))
            {
                error = $"Could not record wholesale account allocation '{endowmentId}'.";
                return false;
            }

            report = booked.Count == 0
                ? $"No wholesale accounts are configured for seller '{sellerBusinessKey}' ({sellerInstanceId}); nothing was booked."
                : $"Booked wholesale account(s) for '{sellerBusinessKey}' ({sellerInstanceId}): {string.Join("; ", booked)}.";
            return true;
        }

        private static bool BookIfNew(
            BakeryShopRuntime sellerRuntime,
            BakeryWholesaleAccount account,
            int dayIndex,
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

            // Lane 10a (H-3/H-6): an explicit config entry is the opt-in —
            // the booking is recorded with its day and the "scenario"
            // decider, so the account's provenance is on the record.
            string refusal = sellerRuntime.AddWholesaleAccount(account, dayIndex, BakeryChannelDecision.ScenarioDecider, diag);
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
