// Review 2 vertical slice Pass D2 — owner-decided horse feed.
//
// Kennedy's decision, Nov 2, 2026: Kennedy can sell horse feed at
// the General Store; by default it is NOT an item the store sells,
// but it can be added in, as any player should be able to add
// items to sell. Interpretation, stated here so it cannot drift:
// feed source = a player-addable General Store item, not a
// hard-coded default catalog item and not a Kennedy-only special
// path. No feed item id, price, or unit is canonised anywhere in
// this file: every one of those is a caller-supplied parameter,
// and the tests pass their own values in and label them test
// parameters. No .asset is changed by this pass, and no feed item
// is added to any profile or definition default list.
//
// How it works, in the order a driver must call it:
//   1. AddFeedItem: the owner operation. It records, for one store
//      business instance, which runtime category holds feed, which
//      item id the store offers, and how many store units back
//      one horse feed unit. It creates NO stock: the owner must
//      stock the category separately (ReceiveStock / off-map
//      intake), or the adapter below correctly reports 0 feed.
//      Adding an item twice with the same definition is
//      idempotent; adding it with a different definition is
//      refused, never silently overwritten.
//   2. FeedStockForBusiness: given a store runtime and its
//      registration, returns the store-category adapter, or null
//      with a diagnostic when the store carries no added feed item.
//      A store without the added item still resolves null —
//      exactly the pre-D2 behaviour — with a named reason, never
//      a crash and never phantom feed.
//   3. FeedStockForHorse: the production-resolver shape. It
//      handles ONLY the General Store case. Farm and livery cases
//      stay exactly where they are (SimulationDrivers, first
//      branches); this resolver is the third branch, consulted
//      after them, and it must never be used to replace them.
//      A horse owned by a business with no feed registration
//      resolves null with a diagnostic, as before.
//   4. Household feed: a person-owned horse does not eat from a
//      store category directly. The household buys feed through
//      the normal shopping loop (the item must first be runtime-
//      registered as a household item — see HouseholdItemCatalog
//      .RegisterRuntimeItem — and offered by the store port),
//      the loop puts real lots into household inventory, and
//      Review2HouseholdHorseFeed (Domains/Population) draws those
//      lots for CareDay. The driver must supply that horse's
//      household inventory; this file does not guess a
//      person-to-household mapping, and a missing inventory
//      resolves null with a diagnostic, never a guessed household.
//
// Persistence gap, stated plainly for Codex: registrations live
// in the in-memory Review2StoreFeedRegistry. They are NOT saved
// by this pass. A driver that registers feed items must persist
// them (save DTO, Pass 9/10 style) or re-register them on load
// from the store's offers, or a restored world falls back to
// null for store horses until re-registration. That fallback is
// honest (diagnosed, no phantom feed), but it is not yet a
// saved registration, and no test here claims otherwise.
//
// Plain C#, no Unity dependency, Roslyn-testable. Simulation-
// Drivers delegates to FeedStockForHorse; the delegation itself
// is Unity-side and is Codex/Unity verification work, exactly
// as D1's manager-wrapper step was.
using System;
using System.Collections.Generic;
using LandLedgers.Animals;
using LandLedgers.Economy.Farming.Integration;

namespace LandLedgers.Economy
{
    /// <summary>
    /// One owner-added store feed item. All fields are caller
    /// parameters; none is a canon price, id, or unit.
    /// </summary>
    public sealed class Review2StoreFeedItem
    {
        public string BusinessInstanceId = string.Empty;
        public string FeedCategoryId = string.Empty;
        public string FeedItemId = string.Empty;
        public string DisplayName = string.Empty;
        public string UnitLabel = string.Empty;

        /// <summary>
        /// Store units backing one horse feed unit. Must be &gt; 0;
        /// zero or negative means "not supplied" and the adapter
        /// holds/takes 0, never a silent 1:1.
        /// </summary>
        public int StoreUnitsPerFeedUnit;
    }

    /// <summary>In-memory registry of owner-added store feed items, keyed by business instance id.</summary>
    public sealed class Review2StoreFeedRegistry
    {
        private readonly Dictionary<string, Review2StoreFeedItem> byBusiness =
            new Dictionary<string, Review2StoreFeedItem>(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Review 2 vertical D3, additive only: clears every
        /// registration. Used by save Restore (clear-then-load)
        /// only; it changes no other registry behaviour.
        /// </summary>
        public void Clear()
        {
            byBusiness.Clear();
        }

        public Review2StoreFeedItem Find(string businessInstanceId)
        {
            if (string.IsNullOrWhiteSpace(businessInstanceId)) return null;
            return byBusiness.TryGetValue(businessInstanceId.Trim(), out Review2StoreFeedItem item) ? item : null;
        }

        /// <summary>
        /// Stores a registration validated by AddFeedItem. Callers
        /// must validate first; this method never overwrites a
        /// different registration for the same business.
        /// </summary>
        public void Add(Review2StoreFeedItem item)
        {
            if (item == null || string.IsNullOrWhiteSpace(item.BusinessInstanceId)) return;
            if (byBusiness.TryGetValue(item.BusinessInstanceId.Trim(), out Review2StoreFeedItem existing)
                && !string.Equals(existing.FeedItemId, item.FeedItemId, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            byBusiness[item.BusinessInstanceId.Trim()] = item;
        }

        public IReadOnlyCollection<Review2StoreFeedItem> All => byBusiness.Values;
    }

    public static class Review2StoreHorseFeed
    {
        /// <summary>
        /// The owner operation: add a feed item to a store. Creates
        /// the runtime category entry (0 current stock unless it
        /// already exists, in which case its stock is untouched)
        /// and records the registration. Creates NO feed stock and
        /// sets NO price: prices live at the store's own offer.
        /// Returns a diagnostic on refusal, null on success.
        /// </summary>
        public static string AddFeedItem(
            BusinessRuntimeState storeRuntime,
            Review2StoreFeedRegistry registry,
            Review2StoreFeedItem item,
            List<string> diag)
        {
            if (storeRuntime == null) return "AddFeedItem: no store runtime — nothing added.";
            if (registry == null) return "AddFeedItem: no feed registry — nothing added.";
            if (item == null) return "AddFeedItem: no feed item — nothing added.";
            if (string.IsNullOrWhiteSpace(item.BusinessInstanceId)) return "AddFeedItem: a feed item needs a store business instance id.";
            if (string.IsNullOrWhiteSpace(item.FeedCategoryId)) return "AddFeedItem: a feed item needs a store category id.";
            if (string.IsNullOrWhiteSpace(item.FeedItemId)) return "AddFeedItem: a feed item needs an item id.";
            if (item.StoreUnitsPerFeedUnit <= 0) return $"AddFeedItem: feed item '{item.FeedItemId}' needs a positive store-units-per-feed-unit conversion; 0 or negative is 'not supplied', not a silent 1:1.";

            Review2StoreFeedItem existing = registry.Find(item.BusinessInstanceId);
            if (existing != null)
            {
                if (string.Equals(existing.FeedCategoryId, item.FeedCategoryId, StringComparison.OrdinalIgnoreCase)
                    && string.Equals(existing.FeedItemId, item.FeedItemId, StringComparison.OrdinalIgnoreCase)
                    && existing.StoreUnitsPerFeedUnit == item.StoreUnitsPerFeedUnit)
                {
                    return null;
                }

                return $"AddFeedItem: store '{item.BusinessInstanceId}' already carries feed item '{existing.FeedItemId}' in category '{existing.FeedCategoryId}'; a different feed item cannot silently replace it.";
            }

            // Ensure the category exists so the adapter has a stock
            // entry to draw from; existing stock is never reset.
            if (storeRuntime.GetCategoryStock(item.FeedCategoryId) == null)
            {
                storeRuntime.EnsureCategoryStock(item.FeedCategoryId, 0, 0);
            }

            // Registry is keyed by business id; the stored copy is
            // the caller's values, not a canonised item.
            var stored = new Review2StoreFeedItem
            {
                BusinessInstanceId = item.BusinessInstanceId.Trim(),
                FeedCategoryId = item.FeedCategoryId.Trim(),
                FeedItemId = item.FeedItemId.Trim(),
                DisplayName = item.DisplayName ?? string.Empty,
                UnitLabel = item.UnitLabel ?? string.Empty,
                StoreUnitsPerFeedUnit = item.StoreUnitsPerFeedUnit,
            };
            registry.Add(stored);
            diag?.Add($"Store '{stored.BusinessInstanceId}' now offers feed item '{stored.FeedItemId}' from category '{stored.FeedCategoryId}' at {stored.StoreUnitsPerFeedUnit} store units per feed unit (caller parameters, not canon).");
            return null;
        }

        /// <summary>
        /// Store-category adapter for a registered store, or null
        /// with a diagnostic when the store carries no added feed
        /// item. A null here is the honest pre-D2 result, not an
        /// error to be hidden: the horse degrades and the driver
        /// logs the reason.
        /// </summary>
        public static IHorseFeedStock FeedStockForBusiness(
            BusinessRuntimeState storeRuntime,
            Review2StoreFeedItem registration,
            List<string> diag)
        {
            if (storeRuntime == null || registration == null)
            {
                diag?.Add("Store horse feed: no store runtime or no added feed item registered — no feed stock.");
                return null;
            }

            if (registration.StoreUnitsPerFeedUnit <= 0)
            {
                diag?.Add($"Store horse feed: feed item '{registration.FeedItemId}' has no conversion supplied — no feed stock, never a silent 1:1.");
                return null;
            }

            return new StoreHorseFeedStockAdapter(storeRuntime, registration.FeedCategoryId, registration.StoreUnitsPerFeedUnit);
        }

        /// <summary>
        /// Production-resolver shape for General Store horses only.
        /// Farm and livery branches run first in SimulationDrivers
        /// and are never replaced by this method. businessRuntime
        /// is a caller-supplied lookup (instance id -> runtime);
        /// a missing runtime or registration resolves null with a
        /// diagnostic. Person/household horses are NOT handled
        /// here: they eat from household inventory via
        /// Review2HouseholdHorseFeed, supplied by the driver.
        /// </summary>
        public static IHorseFeedStock FeedStockForHorse(
            AnimalState horse,
            Func<string, BusinessRuntimeState> businessRuntimeByInstanceId,
            Review2StoreFeedRegistry registry,
            List<string> diag)
        {
            if (horse == null)
            {
                diag?.Add("Store horse feed: no horse — no feed stock.");
                return null;
            }

            if (horse.OwnerKind != AnimalOwnerKind.Business)
            {
                diag?.Add($"Store horse feed: horse {horse.AnimalId} is not business-owned (owner kind {horse.OwnerKind}) — the store resolver does not apply.");
                return null;
            }

            if (registry == null)
            {
                diag?.Add($"Store horse feed: no feed registry — horse {horse.AnimalId} owned by '{horse.OwnerId}' resolves null.");
                return null;
            }

            Review2StoreFeedItem registration = registry.Find(horse.OwnerId);
            if (registration == null)
            {
                diag?.Add($"Store horse feed: business '{horse.OwnerId}' carries no added feed item — horse {horse.AnimalId} resolves null, as before D2.");
                return null;
            }

            BusinessRuntimeState runtime = businessRuntimeByInstanceId != null
                ? businessRuntimeByInstanceId(horse.OwnerId)
                : null;
            if (runtime == null)
            {
                diag?.Add($"Store horse feed: no live runtime for business '{horse.OwnerId}' — horse {horse.AnimalId} resolves null.");
                return null;
            }

            return FeedStockForBusiness(runtime, registration, diag);
        }
    }
}
