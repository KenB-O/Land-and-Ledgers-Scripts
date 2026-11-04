// Review 2 vertical slice Pass D3 — save adoption for the D2
// owner-addable horse-feed registrations.
//
// D2 left this open, stated plainly: runtime household-item
// registrations and store feed registrations were in-memory
// only, so a restored world fell back to null / unknown-item
// with diagnostics, never phantom feed. This file closes that
// gap in the Pass 9 / Pass 10 pattern, additive only:
//   - ONE new field on PassXlSliceSaveDto (review2VerticalD3),
//     null on old saves, which restore as empty with a diagnostic.
//   - Coordinator overloads in PassXlSliceSaveCoordinator.
//   - This state class, with Capture (deep copy) and Restore
//     (clear-then-load, idempotent).
//   - A SaveLoadManager property, following the Pass 11 pattern
//     exactly. Assigning the live state before the first save
//     and load is Codex work, exactly as for Pass 9 / Pass 10,
//     and Unity compilation of that Unity-side edit is Codex /
//     Unity AI work, not claimed by the Roslyn harness here
//     (which covers the coordinator overloads and this state).
//
// WHAT IS PERSISTED, verbatim, never invented:
//   - Runtime household items the driver registered through
//     HouseholdItemCatalog.RegisterRuntimeItem (D2): id, display
//     name, unit label, reserve category, meal serving units,
//     is-food flag. Prices are NOT persisted here: prices live
//     at the selling store's offer, as in D2. No feed item id,
//     price, or unit is canonised by persisting it: the DTO
//     carries exactly the caller-supplied registration values.
//   - Store feed registrations (D2 Review2StoreFeedItem):
//     business instance id, feed category id, feed item id,
//     display name, unit label, store units per feed unit, and
//     an optional owner-entered retail price. A zero price remains
//     unpriced and cannot produce a household offer.
//     Again caller-supplied values verbatim, never a canon item.
//
// WHAT IS NOT PERSISTED, and why that is honest, not a hole:
//   - Store category STOCK is not in this section. Stock lives
//     in the business runtime save. This section restores the
//     registration (which category holds feed, at what
//     conversion); the runtime save restores how many units are
//     in that category. A test that restores registrations
//     against a runtime that still holds stock proves feeding
//     resumes; a test that restores registrations alone must
//     NOT claim stock survived, because stock was never in
//     this section.
//   - The static HouseholdItemCatalog runtime dictionary is
//     per-process, not per-world (D2 design, unchanged here).
//     Restore re-registers each DTO item into that dictionary:
//     an identical re-registration is idempotent, a conflicting
//     definition is refused with a diagnostic and the existing
//     definition wins. The state lists themselves ARE
//     clear-then-load, so restoring twice yields the same
//     lists; the static catalog simply cannot be cleared by
//     this class, and no method here pretends otherwise. A
//     driver running two worlds in one process must register
//     identical items in both, or accept the conflict
//     diagnostic. That limitation is stated here so Codex
//     does not mistake a state restore for a catalog reset.
//   - Feed-need origination (who opens a feed need, at what
//     quantity) is NOT in this section and is NOT decided here:
//     it is a driver decision, left open as in D2. This section
//     persists registrations only; a restored registration
//     does not by itself open a need.
//   - The SimulationDrivers branch that calls the store
//     resolver, the per-day household adapter construction,
//     and CareDay wiring are Unity-side and are NOT tested by
//     the Roslyn harness (the driver is a MonoBehaviour,
//     excluded from the harness, as in D1/D2). The domain
//     resolvers it calls ARE tested, including after restore.
//
// CONTRACT (same as Pass 9 / Pass 10): clear-then-load,
// idempotent. Restore clears the live state lists and the
// feed registry first, then loads the DTO, so a second
// restore yields the same state, never doubles. A null DTO,
// or a DTO with null lists, restores empty with a diagnostic
// — the old-save default, not data loss: an old save never
// had D3 state to lose. Invalid entries (empty ids, or a
// conversion of 0 or less, which means "not supplied", never
// a silent 1:1) are skipped with a diagnostic, never stored
// as a usable registration.
using System;
using System.Collections.Generic;
using LandLedgers.Economy;
using LandLedgers.Population;

namespace LandLedgers.Persistence
{
    /// <summary>One runtime household item, as registered by the driver (D2).</summary>
    [Serializable]
    public sealed class Review2D3RuntimeItemDto
    {
        public string ItemId = string.Empty;
        public string DisplayName = string.Empty;
        public string UnitLabel = string.Empty;
        public string ReserveCategoryId = string.Empty;
        public int MealServingUnits;
        public bool IsFood;
    }

    /// <summary>One owner-added store feed item (D2 Review2StoreFeedItem), verbatim.</summary>
    [Serializable]
    public sealed class Review2D3StoreFeedDto
    {
        public string BusinessInstanceId = string.Empty;
        public string FeedCategoryId = string.Empty;
        public string FeedItemId = string.Empty;
        public string DisplayName = string.Empty;
        public string UnitLabel = string.Empty;
        public int StoreUnitsPerFeedUnit;
        public int FeedUnitsPerStoreUnitNumerator;
        public int FeedUnitsPerStoreUnitDenominator = 1;
        public int UnusedFeedUnits;
        public int RetailPriceCents;
    }

    [Serializable]
    public sealed class Review2D3HorseFeedCarryDto
    {
        public string HorseId = string.Empty;
        public int UnusedFeedUnits;
    }

    /// <summary>Review 2 vertical D3 save section.</summary>
    [Serializable]
    public sealed class Review2VerticalD3SaveDto
    {
        public List<Review2D3RuntimeItemDto> RuntimeItems = new List<Review2D3RuntimeItemDto>();
        public List<Review2D3StoreFeedDto> StoreFeeds = new List<Review2D3StoreFeedDto>();
        public List<Review2D3HorseFeedCarryDto> HorseFeedCarries = new List<Review2D3HorseFeedCarryDto>();
    }

    /// <summary>
    /// Review 2 vertical D3 save state: the live registration
    /// lists a driver owns, plus the feed registry it feeds from.
    /// Capture deep-copies; Restore clear-then-loads and
    /// re-registers runtime items into the static catalog.
    /// </summary>
    public sealed class Review2VerticalD3SaveState
    {
        public List<Review2D3RuntimeItemDto> RuntimeItems = new List<Review2D3RuntimeItemDto>();

        /// <summary>
        /// Mirror of the feed registry, refreshed by Capture and
        /// loaded by Restore. The registry itself, not this list,
        /// is what the store resolver reads.
        /// </summary>
        public List<Review2D3StoreFeedDto> StoreFeeds = new List<Review2D3StoreFeedDto>();
        public List<Review2D3HorseFeedCarryDto> HorseFeedCarries = new List<Review2D3HorseFeedCarryDto>();

        /// <summary>The live feed registry (D2). Drivers that add a store feed item must add it through this registry (via Review2StoreHorseFeed.AddFeedItem) so Capture sees it.</summary>
        public Review2StoreFeedRegistry FeedRegistry = new Review2StoreFeedRegistry();

        public List<string> LastCaptureDiagnostics = new List<string>();

        public int GetHorseFeedCarry(string horseId)
        {
            if (string.IsNullOrWhiteSpace(horseId)) return 0;
            Review2D3HorseFeedCarryDto entry = HorseFeedCarries.Find(x => x != null
                && string.Equals(x.HorseId, horseId, StringComparison.Ordinal));
            return entry != null ? Math.Max(0, entry.UnusedFeedUnits) : 0;
        }

        public void SetHorseFeedCarry(string horseId, int unusedFeedUnits)
        {
            if (string.IsNullOrWhiteSpace(horseId)) return;
            HorseFeedCarries.RemoveAll(x => x == null || string.Equals(x.HorseId, horseId, StringComparison.Ordinal));
            int amount = Math.Max(0, unusedFeedUnits);
            if (amount > 0) HorseFeedCarries.Add(new Review2D3HorseFeedCarryDto { HorseId = horseId, UnusedFeedUnits = amount });
        }

        private static Review2D3RuntimeItemDto CopyRuntimeItem(Review2D3RuntimeItemDto source)
        {
            if (source == null) return new Review2D3RuntimeItemDto();
            return new Review2D3RuntimeItemDto
            {
                ItemId = source.ItemId ?? string.Empty,
                DisplayName = source.DisplayName ?? string.Empty,
                UnitLabel = source.UnitLabel ?? string.Empty,
                ReserveCategoryId = source.ReserveCategoryId ?? string.Empty,
                MealServingUnits = source.MealServingUnits,
                IsFood = source.IsFood,
            };
        }

        private static Review2D3StoreFeedDto CopyStoreFeed(Review2D3StoreFeedDto source)
        {
            if (source == null) return new Review2D3StoreFeedDto();
            return new Review2D3StoreFeedDto
            {
                BusinessInstanceId = source.BusinessInstanceId ?? string.Empty,
                FeedCategoryId = source.FeedCategoryId ?? string.Empty,
                FeedItemId = source.FeedItemId ?? string.Empty,
                DisplayName = source.DisplayName ?? string.Empty,
                UnitLabel = source.UnitLabel ?? string.Empty,
                StoreUnitsPerFeedUnit = source.StoreUnitsPerFeedUnit,
                FeedUnitsPerStoreUnitNumerator = source.FeedUnitsPerStoreUnitNumerator,
                FeedUnitsPerStoreUnitDenominator = source.FeedUnitsPerStoreUnitDenominator,
                UnusedFeedUnits = source.UnusedFeedUnits,
                RetailPriceCents = source.RetailPriceCents,
            };
        }

        private static Review2D3StoreFeedDto CopyStoreFeed(Review2StoreFeedItem source)
        {
            if (source == null) return new Review2D3StoreFeedDto();
            return new Review2D3StoreFeedDto
            {
                BusinessInstanceId = source.BusinessInstanceId ?? string.Empty,
                FeedCategoryId = source.FeedCategoryId ?? string.Empty,
                FeedItemId = source.FeedItemId ?? string.Empty,
                DisplayName = source.DisplayName ?? string.Empty,
                UnitLabel = source.UnitLabel ?? string.Empty,
                StoreUnitsPerFeedUnit = source.StoreUnitsPerFeedUnit,
                FeedUnitsPerStoreUnitNumerator = source.FeedUnitsPerStoreUnitNumerator,
                FeedUnitsPerStoreUnitDenominator = source.FeedUnitsPerStoreUnitDenominator,
                UnusedFeedUnits = source.UnusedFeedUnits,
                RetailPriceCents = source.RetailPriceCents,
            };
        }

        private static bool SameRuntimeItem(Review2D3RuntimeItemDto a, Review2D3RuntimeItemDto b)
        {
            return a != null && b != null
                && string.Equals(a.ItemId, b.ItemId, StringComparison.OrdinalIgnoreCase)
                && string.Equals(a.DisplayName, b.DisplayName, StringComparison.Ordinal)
                && string.Equals(a.UnitLabel, b.UnitLabel, StringComparison.Ordinal)
                && string.Equals(a.ReserveCategoryId, b.ReserveCategoryId, StringComparison.Ordinal)
                && a.MealServingUnits == b.MealServingUnits
                && a.IsFood == b.IsFood;
        }

        /// <summary>
        /// Driver helper: registers a runtime item in the static
        /// catalog AND records it in this state, in one call, so
        /// Capture cannot miss a registration the driver made.
        /// Returns the catalog's diagnostic on refusal, null on
        /// success (including an identical re-registration, which
        /// is idempotent and is not recorded twice).
        /// </summary>
        public string RegisterRuntimeItem(HouseholdItemDefinition definition, List<string> diag)
        {
            if (definition == null) return "Review2VerticalD3Save: no runtime item definition supplied.";
            string refusal = HouseholdItemCatalog.RegisterRuntimeItem(definition);
            if (refusal != null) return refusal;
            // Store the clamped values the catalog actually holds
            // (the constructor clamps meal serving units to >= 1),
            // so a restored definition compares equal to the live one.
            var dto = new Review2D3RuntimeItemDto
            {
                ItemId = definition.ItemId ?? string.Empty,
                DisplayName = definition.DisplayName ?? string.Empty,
                UnitLabel = definition.UnitLabel ?? string.Empty,
                ReserveCategoryId = definition.ReserveCategoryId ?? string.Empty,
                MealServingUnits = definition.MealServingUnits,
                IsFood = definition.IsFood,
            };
            foreach (Review2D3RuntimeItemDto existing in RuntimeItems)
            {
                if (SameRuntimeItem(existing, dto)) return null;
            }
            RuntimeItems.Add(dto);
            diag?.Add($"Review2VerticalD3Save: runtime item '{dto.ItemId}' recorded for save.");
            return null;
        }

        public Review2VerticalD3SaveDto Capture(List<string> diag)
        {
            diag = diag ?? new List<string>();
            LastCaptureDiagnostics = diag;
            var dto = new Review2VerticalD3SaveDto();
            foreach (Review2D3RuntimeItemDto item in RuntimeItems ?? new List<Review2D3RuntimeItemDto>())
                dto.RuntimeItems.Add(CopyRuntimeItem(item));
            // Store feeds are captured from the registry, not
            // from the mirror list, so a feed item added straight
            // through Review2StoreHorseFeed.AddFeedItem is seen
            // even if the driver never touched StoreFeeds directly.
            var feeds = new List<Review2D3StoreFeedDto>();
            if (FeedRegistry != null)
            {
                foreach (Review2StoreFeedItem item in FeedRegistry.All)
                    feeds.Add(CopyStoreFeed(item));
            }
            feeds.Sort((a, b) => string.Compare(a.BusinessInstanceId, b.BusinessInstanceId, StringComparison.OrdinalIgnoreCase));
            dto.StoreFeeds = feeds;
            dto.HorseFeedCarries = new List<Review2D3HorseFeedCarryDto>();
            foreach (Review2D3HorseFeedCarryDto carry in HorseFeedCarries ?? new List<Review2D3HorseFeedCarryDto>())
                if (carry != null && !string.IsNullOrWhiteSpace(carry.HorseId) && carry.UnusedFeedUnits > 0)
                    dto.HorseFeedCarries.Add(new Review2D3HorseFeedCarryDto { HorseId = carry.HorseId, UnusedFeedUnits = carry.UnusedFeedUnits });
            dto.HorseFeedCarries.Sort((a, b) => string.Compare(a.HorseId, b.HorseId, StringComparison.Ordinal));
            StoreFeeds = new List<Review2D3StoreFeedDto>();
            foreach (Review2D3StoreFeedDto feed in feeds)
                StoreFeeds.Add(CopyStoreFeed(feed));
            diag.Add("Review2VerticalD3Save: captured " + dto.RuntimeItems.Count + " runtime item(s) and " +
                dto.StoreFeeds.Count + " store feed registration(s).");
            return dto;
        }

        /// <summary>
        /// Clear-then-load restore. A null DTO, or a DTO with null
        /// lists, restores empty with a diagnostic (old-save
        /// default). Restoring twice yields the same state.
        /// Runtime items are re-registered into the static
        /// catalog; a conflicting definition already in the
        /// catalog is refused with a diagnostic and the existing
        /// definition wins, while the state list still records
        /// what the save said, so the divergence is visible,
        /// never silently overwritten. Store feed entries with
        /// empty ids or a conversion of 0 or less are skipped
        /// with a diagnostic, never stored as usable.
        /// </summary>
        public void Restore(Review2VerticalD3SaveDto dto, List<string> diag)
        {
            diag = diag ?? new List<string>();
            RuntimeItems.Clear();
            StoreFeeds.Clear();
            HorseFeedCarries.Clear();
            if (FeedRegistry == null) FeedRegistry = new Review2StoreFeedRegistry();
            FeedRegistry.Clear();
            if (dto == null)
            {
                diag.Add("Review2VerticalD3Save: no D3 section in save; runtime items and store feed registrations restored as empty (old-save default).");
                return;
            }
            foreach (Review2D3RuntimeItemDto item in dto.RuntimeItems ?? new List<Review2D3RuntimeItemDto>())
            {
                if (item == null || string.IsNullOrWhiteSpace(item.ItemId))
                {
                    diag.Add("Review2VerticalD3Save: runtime item with no item id skipped on restore.");
                    continue;
                }
                var copy = CopyRuntimeItem(item);
                RuntimeItems.Add(copy);
                var definition = new HouseholdItemDefinition(
                    copy.ItemId, copy.DisplayName, copy.UnitLabel, copy.ReserveCategoryId, copy.MealServingUnits, copy.IsFood);
                string refusal = HouseholdItemCatalog.RegisterRuntimeItem(definition);
                if (refusal != null)
                {
                    diag.Add("Review2VerticalD3Save: runtime item '" + copy.ItemId + "' not re-registered into the catalog (" + refusal + "); the existing catalog definition wins and the save record is kept in the state list for diagnosis.");
                }
            }
            foreach (Review2D3StoreFeedDto feed in dto.StoreFeeds ?? new List<Review2D3StoreFeedDto>())
            {
                if (feed == null
                    || string.IsNullOrWhiteSpace(feed.BusinessInstanceId)
                    || string.IsNullOrWhiteSpace(feed.FeedCategoryId)
                    || string.IsNullOrWhiteSpace(feed.FeedItemId))
                {
                    diag.Add("Review2VerticalD3Save: store feed registration with an empty id skipped on restore.");
                    continue;
                }
                int numerator = feed.FeedUnitsPerStoreUnitNumerator > 0
                    ? feed.FeedUnitsPerStoreUnitNumerator : 1;
                int denominator = feed.FeedUnitsPerStoreUnitNumerator > 0
                    ? feed.FeedUnitsPerStoreUnitDenominator : feed.StoreUnitsPerFeedUnit;
                if (numerator <= 0 || denominator <= 0)
                {
                    diag.Add("Review2VerticalD3Save: store feed item '" + feed.FeedItemId + "' has no positive rational conversion on restore — skipped, never a silent 1:1.");
                    continue;
                }
                if (feed.RetailPriceCents < 0)
                {
                    diag.Add("Review2VerticalD3Save: store feed item '" + feed.FeedItemId + "' has a negative owner-entered price on restore — skipped.");
                    continue;
                }
                Review2StoreFeedItem existing = FeedRegistry.Find(feed.BusinessInstanceId);
                if (existing != null)
                {
                    if (string.Equals(existing.FeedItemId, feed.FeedItemId, StringComparison.OrdinalIgnoreCase)
                        && string.Equals(existing.FeedCategoryId, feed.FeedCategoryId, StringComparison.OrdinalIgnoreCase)
                    && Review2StoreHorseFeed.EffectiveNumerator(existing) == numerator
                    && Review2StoreHorseFeed.EffectiveDenominator(existing) == denominator
                    && existing.RetailPriceCents == feed.RetailPriceCents)
                    {
                        continue;
                    }
                    diag.Add("Review2VerticalD3Save: store '" + feed.BusinessInstanceId + "' already carries a different feed item on restore ('" + existing.FeedItemId + "'); the restored item '" + feed.FeedItemId + "' is skipped, never silently overwritten.");
                    continue;
                }
                FeedRegistry.Add(new Review2StoreFeedItem
                {
                    BusinessInstanceId = feed.BusinessInstanceId.Trim(),
                    FeedCategoryId = feed.FeedCategoryId.Trim(),
                    FeedItemId = feed.FeedItemId.Trim(),
                    DisplayName = feed.DisplayName ?? string.Empty,
                    UnitLabel = feed.UnitLabel ?? string.Empty,
                    StoreUnitsPerFeedUnit = feed.StoreUnitsPerFeedUnit,
                    FeedUnitsPerStoreUnitNumerator = numerator,
                    FeedUnitsPerStoreUnitDenominator = denominator,
                    UnusedFeedUnits = Math.Max(0, feed.UnusedFeedUnits),
                    RetailPriceCents = feed.RetailPriceCents,
                });
                StoreFeeds.Add(CopyStoreFeed(feed));
            }
            foreach (Review2D3HorseFeedCarryDto carry in dto.HorseFeedCarries ?? new List<Review2D3HorseFeedCarryDto>())
            {
                if (carry == null || string.IsNullOrWhiteSpace(carry.HorseId) || carry.UnusedFeedUnits <= 0)
                {
                    diag.Add("Review2VerticalD3Save: invalid animal feed carry skipped on restore.");
                    continue;
                }
                SetHorseFeedCarry(carry.HorseId.Trim(), carry.UnusedFeedUnits);
            }
            diag.Add("Review2VerticalD3Save: restore complete (" + RuntimeItems.Count + " runtime item(s), " +
                StoreFeeds.Count + " store feed registration(s), clear-then-load, idempotent).");
        }
    }
}
