using System;
using System.Collections.Generic;
using System.Globalization;
using LandLedgers.Economy.Blacksmith;
using LandLedgers.Economy.Logistics;
using LandLedgers.Population;

namespace LandLedgers.Economy.Storage
{
    /// <summary>
    /// B5 (Agent B, expanded campaign 2026-11-16): cabinet/shelf storage +
    /// display policy (plan §7, slice B5). Binds the B0 furnishing register's
    /// installed cabinets, shelves, crates, barrels and kitchen counters to
    /// PhysicalStorageZone instances, so storage zones finally have
    /// production instantiations (G's wiring audit 1 found zero), and
    /// derives what a shelf shows from the household inventory authority —
    /// never from a second ledger.
    ///
    /// Visibility, per the owner brief and plan §7.2:
    ///   Closed — a cabinet. Contents are hidden; only the cabinet mesh
    ///     renders. The zone still enforces capacity and restrictions.
    ///   OpenDisplay — a shelf. Contents are representable: one pooled,
    ///     limited representative mesh per SKU, capped at
    ///     DisplayMeshCapPerSku (O-5 proposed default 12). A SKU with zero
    ///     units on hand displays zero meshes and gets no allocation at
    ///     all, so a shelf never shows an absent product.
    ///   Conditional — a fixture whose contents show only under an
    ///     inspection interaction that does not exist yet. Behaves like
    ///     Closed for display until that interaction lands; the zone still
    ///     stores.
    ///
    /// No-second-ledger rule: a zone holds StoredLotReference values only
    /// (authority + lot id + the load measured at placement). Item, quantity,
    /// owner, custody and holds live in the HouseholdInventory and nowhere
    /// else. Display allocations are derived from the inventory on every
    /// recompute: DisplayedUnits is the live QuantityUnits of the placed
    /// lots (physical presence, not availability — a held unit is still on
    /// the shelf), MeshCount is min(units, cap). A lot consumed to zero or
    /// removed from the inventory contributes zero units and its SKU drops
    /// out of the allocation entirely.
    ///
    /// Recompute is explicit. The inventory authority publishes no change
    /// event (and no event bus is introduced — repo rule), so display is
    /// recomputed on placement/removal and on an explicit RecomputeDisplays
    /// call, which the daily driver polls at the day boundary (Agent G's
    /// wiring). Staleness between an inventory change and the next recompute
    /// is a defect Validate() detects, not a feature: Validate derives the
    /// expected allocations and reports any mismatch.
    ///
    /// No save seam in this slice (B1/B4 precedent): bindings are
    /// re-derived from the register + inventory after Continue. Zone
    /// placements are durable user state, so the seam is a documented
    /// follow-up, not silent — the zone itself is [Serializable] and its
    /// placements are plain lot references, so persisting them is small.
    ///
    /// Retail exclusion (Canon §8.5A, plan §5.2): this binding is
    /// household-only. Retail fixtures use normalized allocation only; a
    /// store shelf must never be bound through this registry.
    ///
    /// Live-blocked, honestly: tables, chairs and shelves have NO tracked
    /// prefab in this checkout (Codex instruction 1), so no Unity claim is
    /// made. Closed cabinets, counters, crates and barrels have tracked
    /// prefabs and are placeable now. Codex authors one activity location
    /// per storage fixture (storage-&lt;n&gt;, type Storage, at the access
    /// point in front of the fixture, plan §9.2 instruction 4); the
    /// arrival-path check for storage access is a follow-up when a storage
    /// driver exists (Agent G).
    ///
    /// Wiring items for Agent G (recorded in plan §20): create one
    /// FurnishingStorageRegistry per household in hub/state (same standing
    /// sync gap as B0/B1/B2, covered by W-G1); pass the registry + the real
    /// HouseholdInventory into the daily drive; poll RecomputeDisplays at
    /// the day boundary; call RefreshInventory after Continue; drop a
    /// binding when its fixture is removed, moved or sold (B0 ends the
    /// placement binding; DropBindingForAsset clears the storage stamp).
    /// </summary>
    public enum StorageVisibility
    {
        Closed = 0,
        OpenDisplay = 1,
        Conditional = 2,
    }

    /// <summary>
    /// B5: one shelf-display allocation. Plain data, derived from the
    /// inventory authority on every recompute. DisplayedUnits is the live
    /// unit count on hand for the SKU; MeshCount is the pooled
    /// representative-mesh count, never more than DisplayedUnits and never
    /// more than the per-SKU cap. A zero-stock SKU has no allocation.
    /// </summary>
    [Serializable]
    public sealed class FurnishingDisplayAllocation
    {
        public string SkuId = string.Empty;
        public int DisplayedUnits;
        public int MeshCount;
    }

    /// <summary>
    /// B5: one installed storage fixture bound to its PhysicalStorageZone.
    /// The zone id is the B0 placement binding id; the zone location id is
    /// "building:&lt;instanceId&gt;:&lt;placementId&gt;". Exposure is Indoor
    /// for cabinets/shelves (plan §3.5); outdoor barrel/crate storage is a
    /// later slice if the owner wants it.
    ///
    /// The zone's existing-lot lookup resolves against this binding's
    /// Inventory at call time, so RefreshInventory after a restore keeps
    /// the lookup honest without rebuilding the zone.
    /// </summary>
    public sealed class FurnishingStorageBinding
    {
        public const string CabinetSmallAssetKind = "furniture.cabinet-small";
        public const string CabinetLargeAssetKind = "furniture.cabinet-large";
        public const string ShelfAssetKind = "furniture.shelf";
        public const string CrateAssetKind = "furniture.crate";
        public const string BarrelAssetKind = "furniture.barrel";
        public const string KitchenCounterAssetKind = "furniture.kitchen-counter";

        /// <summary>O-5 proposed default: at most 12 representative meshes per SKU on a shelf.</summary>
        public const int DefaultDisplayMeshCapPerSku = 12;

        public int HouseholdId;
        public string AssetId = string.Empty;
        public string ZoneId = string.Empty;
        public int BuildingInstanceId;
        public string PlacementId = string.Empty;
        public StorageVisibility Visibility;
        public int DisplayMeshCap = DefaultDisplayMeshCapPerSku;
        public string AllowedReserveCategoryId = string.Empty;
        public List<string> AllowedItemIds = new List<string>();
        public PhysicalStorageZone Zone;
        public HouseholdInventory Inventory;

        /// <summary>
        /// Measures a lot for capacity enforcement. Null means no measure is
        /// supplied, and the zone then refuses placement on any bounded axis
        /// rather than assuming a size (the zone's own rule). Production
        /// masses come from the item-spec authority when F wires them; until
        /// then the caller supplies the measure.
        /// </summary>
        public Func<HouseholdItemLot, StorageLoadMeasurement> MeasureLot;

        private List<FurnishingDisplayAllocation> allocations = new List<FurnishingDisplayAllocation>();

        public IReadOnlyList<FurnishingDisplayAllocation> Allocations => allocations;

        public static bool IsStorageFixtureKind(string kind)
        {
            switch (kind)
            {
                case CabinetSmallAssetKind:
                case CabinetLargeAssetKind:
                case ShelfAssetKind:
                case CrateAssetKind:
                case BarrelAssetKind:
                case KitchenCounterAssetKind:
                    return true;
                default:
                    return false;
            }
        }

        /// <summary>The zone's existing-lot lookup. Resolves against the live inventory.</summary>
        public bool ResolveLotReference(string authority, string lotId)
        {
            if (!string.Equals(authority, CustodyTransferLog.HouseholdInventoryAuthority, StringComparison.Ordinal))
                return false;
            if (!int.TryParse(lotId, NumberStyles.Integer, CultureInfo.InvariantCulture, out int id))
                return false;
            return FindLot(id) != null;
        }

        public HouseholdItemLot FindLot(int lotId)
        {
            if (Inventory == null || Inventory.Lots == null) return null;
            foreach (HouseholdItemLot lot in Inventory.Lots)
            {
                if (lot != null && lot.LotId == lotId) return lot;
            }
            return null;
        }

        public HouseholdItemLot FindLotByReference(StoredLotReference placed)
        {
            if (placed == null) return null;
            if (!int.TryParse(placed.LotId, NumberStyles.Integer, CultureInfo.InvariantCulture, out int id))
                return null;
            return FindLot(id);
        }

        /// <summary>
        /// Places a lot in this zone. Refuses loudly (returns the reason)
        /// when the lot does not resolve in the household inventory, holds
        /// no units, or violates the binding's category/item restrictions;
        /// the zone itself refuses duplicates and capacity overruns. On
        /// success the display is recomputed. The inventory is never
        /// changed — placement is a reference, not a transfer.
        /// Returns null on success.
        /// </summary>
        public string TryPlaceLot(int lotId)
        {
            if (Inventory == null)
                return $"FurnishingStorageBinding.TryPlaceLot: no household inventory connected for zone {ZoneId} (household {HouseholdId}).";
            if (Zone == null)
                return $"FurnishingStorageBinding.TryPlaceLot: no storage zone is bound for asset {AssetId}.";
            HouseholdItemLot lot = FindLot(lotId);
            if (lot == null)
                return $"FurnishingStorageBinding.TryPlaceLot: lot {lotId} does not resolve in household {HouseholdId} inventory ({CustodyTransferLog.HouseholdInventoryAuthority}).";
            if (lot.QuantityUnits <= 0)
                return $"FurnishingStorageBinding.TryPlaceLot: lot {lotId} ({lot.ItemId}) holds no units.";
            string restriction = CheckRestrictions(lot);
            if (restriction != null) return restriction;
            StorageLoadMeasurement load = MeasureLot != null
                ? MeasureLot(lot)
                : StorageLoadMeasurement.Unknown;
            var reference = new StoredLotReference(
                CustodyTransferLog.HouseholdInventoryAuthority,
                lotId.ToString(CultureInfo.InvariantCulture),
                load);
            string refusal;
            if (!Zone.TryPlace(reference, out refusal))
                return refusal;
            RecomputeDisplay();
            return null;
        }

        /// <summary>
        /// Removes a lot from this zone. The inventory is untouched — the
        /// lot keeps every unit it had; only the physical placement ends.
        /// Returns true when a placement was removed.
        /// </summary>
        public bool RemoveLot(int lotId)
        {
            if (Zone == null) return false;
            bool removed = Zone.Remove(
                CustodyTransferLog.HouseholdInventoryAuthority,
                lotId.ToString(CultureInfo.InvariantCulture));
            if (removed) RecomputeDisplay();
            return removed;
        }

        public bool ContainsLot(int lotId)
        {
            if (Zone == null) return false;
            return Zone.Contains(
                CustodyTransferLog.HouseholdInventoryAuthority,
                lotId.ToString(CultureInfo.InvariantCulture));
        }

        /// <summary>Re-derives the display allocations from the live inventory. No-op for closed/conditional visibility.</summary>
        public void RecomputeDisplay()
        {
            allocations = DeriveDisplayAllocations();
        }

        private List<FurnishingDisplayAllocation> DeriveDisplayAllocations()
        {
            var next = new List<FurnishingDisplayAllocation>();
            if (Zone == null) return next;
            if (Visibility != StorageVisibility.OpenDisplay) return next;
            if (Inventory == null) return next;
            var bySku = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (StoredLotReference placed in Zone.Placements)
            {
                if (placed == null) continue;
                HouseholdItemLot lot = FindLotByReference(placed);
                if (lot == null) continue; // stale placement: contributes nothing; Validate flags it.
                if (lot.QuantityUnits <= 0) continue;
                int units;
                bySku.TryGetValue(lot.ItemId, out units);
                bySku[lot.ItemId] = units + lot.QuantityUnits;
            }
            var skus = new List<string>(bySku.Keys);
            skus.Sort(StringComparer.Ordinal);
            foreach (string sku in skus)
            {
                int units = bySku[sku];
                if (units <= 0) continue;
                next.Add(new FurnishingDisplayAllocation
                {
                    SkuId = sku,
                    DisplayedUnits = units,
                    MeshCount = Math.Min(units, DisplayMeshCap),
                });
            }
            return next;
        }

        private string CheckRestrictions(HouseholdItemLot lot)
        {
            if (!string.IsNullOrEmpty(AllowedReserveCategoryId))
            {
                HouseholdItemDefinition spec = HouseholdItemCatalog.Get(lot.ItemId);
                string category = spec != null ? spec.ReserveCategoryId : string.Empty;
                if (!string.Equals(category, AllowedReserveCategoryId, StringComparison.OrdinalIgnoreCase))
                    return $"FurnishingStorageBinding.TryPlaceLot: lot {lot.LotId} ({lot.ItemId}) is reserve category '{category}', but zone {ZoneId} admits only '{AllowedReserveCategoryId}'.";
            }
            if (AllowedItemIds != null && AllowedItemIds.Count > 0)
            {
                bool allowed = false;
                foreach (string id in AllowedItemIds)
                {
                    if (string.Equals(id, lot.ItemId, StringComparison.Ordinal)) { allowed = true; break; }
                }
                if (!allowed)
                    return $"FurnishingStorageBinding.TryPlaceLot: lot {lot.LotId} ({lot.ItemId}) is not on the allowed-item list for zone {ZoneId}.";
            }
            return null;
        }

        private bool AllocationsMatch(List<FurnishingDisplayAllocation> expected)
        {
            if (allocations == null || allocations.Count != expected.Count) return false;
            for (int i = 0; i < expected.Count; i++)
            {
                FurnishingDisplayAllocation current = allocations[i];
                FurnishingDisplayAllocation want = expected[i];
                if (current == null || want == null) return false;
                if (current.SkuId != want.SkuId
                    || current.DisplayedUnits != want.DisplayedUnits
                    || current.MeshCount != want.MeshCount)
                    return false;
            }
            return true;
        }

        /// <summary>
        /// Binding-level problems. Never throws; a missing register or
        /// inventory is a problem, not a crash. Display staleness is a
        /// problem: the expected allocations are derived fresh and compared
        /// against the live ones, so an inventory change that has not been
        /// recomputed yet is caught here.
        /// </summary>
        public List<string> Validate(HouseholdFurnishingRegister register)
        {
            var problems = new List<string>();
            if (register == null)
            {
                problems.Add($"FurnishingStorageBinding.Validate: no furnishing register supplied for zone {ZoneId}.");
                return problems;
            }
            FurnishingPlacementBinding live = register.LiveBindingFor(AssetId);
            if (live == null)
            {
                problems.Add($"FurnishingStorageBinding.Validate: asset {AssetId} has no live placement binding (removed, moved or sold?) — zone {ZoneId} is orphaned.");
            }
            else
            {
                if (live.CapabilityKind != FurnishingCapabilityKind.StorageZone)
                    problems.Add($"FurnishingStorageBinding.Validate: asset {AssetId} is installed with capability {live.CapabilityKind}, not StorageZone — zone {ZoneId} is not the live capability.");
                if (live.CapabilityReferenceId != ZoneId)
                    problems.Add($"FurnishingStorageBinding.Validate: asset {AssetId} capability reference '{live.CapabilityReferenceId}' does not name zone {ZoneId}.");
                if (live.BuildingInstanceId != BuildingInstanceId)
                    problems.Add($"FurnishingStorageBinding.Validate: asset {AssetId} moved to building {live.BuildingInstanceId}; zone {ZoneId} still names building {BuildingInstanceId}.");
            }
            EquipmentAsset asset = register.FindAsset(AssetId);
            if (asset == null)
            {
                problems.Add($"FurnishingStorageBinding.Validate: asset {AssetId} is not in the furnishing register.");
            }
            else
            {
                if (!IsStorageFixtureKind(asset.Kind))
                    problems.Add($"FurnishingStorageBinding.Validate: asset {AssetId} is kind '{asset.Kind}', not a storage fixture.");
                if (!asset.IsUsable)
                    problems.Add($"FurnishingStorageBinding.Validate: asset {AssetId} is not usable (condition {asset.Condition01}).");
            }
            if (Inventory == null)
            {
                problems.Add($"FurnishingStorageBinding.Validate: no household inventory connected for zone {ZoneId}; display cannot be derived.");
            }
            else if (Zone != null)
            {
                foreach (StoredLotReference placed in Zone.Placements)
                {
                    if (placed == null) continue;
                    HouseholdItemLot lot = FindLotByReference(placed);
                    if (lot == null)
                    {
                        problems.Add($"FurnishingStorageBinding.Validate: placed lot {placed.LotId} no longer resolves in household {HouseholdId} inventory — prune it.");
                        continue;
                    }
                    string restriction = CheckRestrictions(lot);
                    if (restriction != null) problems.Add(restriction);
                }
                if (!AllocationsMatch(DeriveDisplayAllocations()))
                    problems.Add($"FurnishingStorageBinding.Validate: display allocations are stale for zone {ZoneId}; recompute.");
            }
            return problems;
        }
    }

    /// <summary>
    /// B5: one household's storage bindings, derived from its B0 register
    /// and its household inventory. One asset has at most one storage
    /// binding, and one lot has exactly one storage place across all of
    /// the household's zones — the cross-zone rule lives here because no
    /// single zone can see the others.
    ///
    /// No save seam in this slice (B1/B4 precedent): the registry is
    /// re-derived after Continue. Call RefreshInventory after a restore so
    /// the bindings point at the live inventory and the displays recompute.
    /// </summary>
    public sealed class FurnishingStorageRegistry
    {
        public int HouseholdId;
        public HouseholdFurnishingRegister Furnishings;
        public HouseholdInventory Inventory;
        public List<FurnishingStorageBinding> Bindings = new List<FurnishingStorageBinding>();

        public FurnishingStorageBinding BindingForAsset(string assetId)
        {
            if (string.IsNullOrEmpty(assetId) || Bindings == null) return null;
            foreach (FurnishingStorageBinding binding in Bindings)
            {
                if (binding != null && binding.AssetId == assetId) return binding;
            }
            return null;
        }

        public FurnishingStorageBinding BindingForZone(string zoneId)
        {
            if (string.IsNullOrEmpty(zoneId) || Bindings == null) return null;
            foreach (FurnishingStorageBinding binding in Bindings)
            {
                if (binding != null && binding.ZoneId == zoneId) return binding;
            }
            return null;
        }

        /// <summary>
        /// Installed, usable storage fixtures that have a live storage
        /// binding. A fixture installed without the StorageZone capability
        /// is placed but not a storage fixture (Validate flags it).
        /// </summary>
        public List<FurnishingPlacementBinding> StorageFixtures()
        {
            var fixtures = new List<FurnishingPlacementBinding>();
            if (Furnishings == null) return fixtures;
            foreach (FurnishingPlacementBinding live in Furnishings.Bindings)
            {
                if (live == null) continue;
                EquipmentAsset asset = Furnishings.FindAsset(live.AssetId);
                if (asset == null || !asset.IsUsable) continue;
                if (!FurnishingStorageBinding.IsStorageFixtureKind(asset.Kind)) continue;
                if (live.CapabilityKind != FurnishingCapabilityKind.StorageZone) continue;
                if (BindingForAsset(live.AssetId) == null) continue;
                fixtures.Add(live);
            }
            return fixtures;
        }

        /// <summary>
        /// Binds an installed storage fixture to a PhysicalStorageZone and
        /// stamps the B0 placement binding with the StorageZone capability
        /// and the zone id. At least one capacity axis must be known — a
        /// zone with no enforced axis is not a zone. Refuses loudly
        /// (returns the reason) when the asset is not installed, is not a
        /// storage fixture, is not usable, belongs to another owner, is
        /// already bound, or is installed under a different capability.
        /// Returns null on success.
        /// </summary>
        public string TryBindFixture(
            string assetId,
            StorageVisibility visibility,
            StorageCapacityLimit massCapacity,
            StorageCapacityLimit volumeCapacity,
            Func<HouseholdItemLot, StorageLoadMeasurement> measureLot,
            string allowedReserveCategoryId,
            IReadOnlyList<string> allowedItemIds,
            int displayMeshCap,
            out FurnishingStorageBinding binding)
        {
            binding = null;
            if (Furnishings == null)
                return "FurnishingStorageRegistry.TryBindFixture: no furnishing register connected.";
            if (Inventory == null)
                return "FurnishingStorageRegistry.TryBindFixture: no household inventory connected.";
            if (string.IsNullOrEmpty(assetId))
                return "FurnishingStorageRegistry.TryBindFixture: an asset id is required.";
            FurnishingStorageBinding existing = BindingForAsset(assetId);
            if (existing != null)
                return $"FurnishingStorageRegistry.TryBindFixture: asset {assetId} is already bound as storage zone {existing.ZoneId}; drop the binding first.";
            FurnishingPlacementBinding live = Furnishings.LiveBindingFor(assetId);
            if (live == null)
                return $"FurnishingStorageRegistry.TryBindFixture: asset {assetId} has no live placement binding; install it first.";
            EquipmentAsset asset = Furnishings.FindAsset(assetId);
            if (asset == null)
                return $"FurnishingStorageRegistry.TryBindFixture: asset {assetId} is not in the furnishing register.";
            if (asset.OwnerKind != HouseholdFurnishingRegister.OwnerKindHousehold
                || asset.OwnerId != HouseholdFurnishingRegister.OwnerIdForHousehold(HouseholdId))
                return $"FurnishingStorageRegistry.TryBindFixture: asset {assetId} belongs to another owner, not household {HouseholdId}.";
            if (!FurnishingStorageBinding.IsStorageFixtureKind(asset.Kind))
                return $"FurnishingStorageRegistry.TryBindFixture: asset {assetId} is kind '{asset.Kind}', not a storage fixture (cabinet, shelf, crate, barrel or counter).";
            if (!asset.IsUsable)
                return $"FurnishingStorageRegistry.TryBindFixture: asset {assetId} is not usable (condition {asset.Condition01}).";
            if (live.CapabilityKind != FurnishingCapabilityKind.None
                && live.CapabilityKind != FurnishingCapabilityKind.StorageZone)
                return $"FurnishingStorageRegistry.TryBindFixture: asset {assetId} is installed with capability {live.CapabilityKind}, not storage.";
            if (!massCapacity.IsKnown && !volumeCapacity.IsKnown)
                return "FurnishingStorageRegistry.TryBindFixture: at least one capacity axis (mass or volume) must be known.";
            if (!Enum.IsDefined(typeof(StorageVisibility), visibility))
                return $"FurnishingStorageRegistry.TryBindFixture: unknown storage visibility {(int)visibility}.";
            if (displayMeshCap <= 0)
                return "FurnishingStorageRegistry.TryBindFixture: the display mesh cap must be positive.";

            string zoneId = string.IsNullOrEmpty(live.BindingId) ? "storage-" + assetId : live.BindingId;
            string placementId = string.IsNullOrEmpty(live.PlacementId) ? assetId : live.PlacementId;
            string locationId = HouseholdFurnishingRegister.BuildingLocationId(live.BuildingInstanceId) + ":" + placementId;
            var definition = new PhysicalStorageZoneDefinition(
                zoneId,
                locationId,
                StorageExposure.Indoor,
                massCapacity,
                volumeCapacity);
            binding = new FurnishingStorageBinding
            {
                HouseholdId = HouseholdId,
                AssetId = assetId,
                ZoneId = zoneId,
                BuildingInstanceId = live.BuildingInstanceId,
                PlacementId = placementId,
                Visibility = visibility,
                DisplayMeshCap = displayMeshCap,
                AllowedReserveCategoryId = allowedReserveCategoryId ?? string.Empty,
                AllowedItemIds = allowedItemIds != null
                    ? new List<string>(allowedItemIds)
                    : new List<string>(),
                Inventory = Inventory,
                MeasureLot = measureLot,
            };
            binding.Zone = new PhysicalStorageZone(definition, binding.ResolveLotReference);
            live.CapabilityKind = FurnishingCapabilityKind.StorageZone;
            live.CapabilityReferenceId = zoneId;
            Bindings.Add(binding);
            return null;
        }

        /// <summary>
        /// Places a lot in a zone. Refuses loudly when the zone does not
        /// exist or the lot is already placed in another of this household's
        /// zones — one lot has exactly one storage place. Returns null on success.
        /// </summary>
        public string TryPlaceLot(string zoneId, int lotId)
        {
            FurnishingStorageBinding binding = BindingForZone(zoneId);
            if (binding == null)
                return $"FurnishingStorageRegistry.TryPlaceLot: no storage zone '{zoneId}' for household {HouseholdId}.";
            string lotKey = lotId.ToString(CultureInfo.InvariantCulture);
            foreach (FurnishingStorageBinding other in Bindings)
            {
                if (other == null || other == binding || other.Zone == null) continue;
                if (other.Zone.Contains(CustodyTransferLog.HouseholdInventoryAuthority, lotKey))
                    return $"FurnishingStorageRegistry.TryPlaceLot: lot {lotId} is already placed in zone {other.ZoneId}; one lot has exactly one storage place.";
            }
            return binding.TryPlaceLot(lotId);
        }

        /// <summary>Removes a lot from a zone. The inventory is untouched. Returns true when a placement was removed.</summary>
        public bool RemoveLot(string zoneId, int lotId)
        {
            FurnishingStorageBinding binding = BindingForZone(zoneId);
            if (binding == null) return false;
            return binding.RemoveLot(lotId);
        }

        /// <summary>Re-derives every binding's display allocations from the live inventory.</summary>
        public void RecomputeDisplays()
        {
            foreach (FurnishingStorageBinding binding in Bindings)
            {
                if (binding == null) continue;
                binding.RecomputeDisplay();
            }
        }

        /// <summary>
        /// Re-points the registry and every binding at the live inventory
        /// (after Continue) and recomputes the displays.
        /// </summary>
        public void RefreshInventory(HouseholdInventory inventory)
        {
            Inventory = inventory;
            foreach (FurnishingStorageBinding binding in Bindings)
            {
                if (binding == null) continue;
                binding.Inventory = inventory;
            }
            RecomputeDisplays();
        }

        /// <summary>
        /// Drops a storage binding (fixture removed, moved or sold). The
        /// B0 placement binding's storage stamp is cleared when it still
        /// names this zone. The inventory is untouched. Returns true when a
        /// binding was dropped.
        /// </summary>
        public bool DropBindingForAsset(string assetId)
        {
            FurnishingStorageBinding binding = BindingForAsset(assetId);
            if (binding == null) return false;
            Bindings.Remove(binding);
            if (Furnishings != null)
            {
                FurnishingPlacementBinding live = Furnishings.LiveBindingFor(assetId);
                if (live != null
                    && live.CapabilityKind == FurnishingCapabilityKind.StorageZone
                    && live.CapabilityReferenceId == binding.ZoneId)
                {
                    live.CapabilityKind = FurnishingCapabilityKind.None;
                    live.CapabilityReferenceId = string.Empty;
                }
            }
            return true;
        }

        /// <summary>
        /// Removes placements whose lots no longer resolve in the household
        /// inventory (consumed to zero, or otherwise gone) and recomputes
        /// the affected displays. Returns the number of placements pruned.
        /// </summary>
        public int PruneStalePlacements()
        {
            int removed = 0;
            foreach (FurnishingStorageBinding binding in Bindings)
            {
                if (binding == null || binding.Zone == null) continue;
                var stale = new List<string>();
                foreach (StoredLotReference placed in binding.Zone.Placements)
                {
                    if (placed == null) continue;
                    if (binding.FindLotByReference(placed) == null) stale.Add(placed.LotId);
                }
                foreach (string lotId in stale)
                {
                    if (binding.Zone.Remove(CustodyTransferLog.HouseholdInventoryAuthority, lotId)) removed++;
                }
                if (stale.Count > 0) binding.RecomputeDisplay();
            }
            return removed;
        }

        /// <summary>
        /// Registry-level problems: missing register/inventory, fixtures
        /// installed without a zone, storage references with no binding,
        /// the same lot placed in two zones, plus every binding's own
        /// problems. Never throws.
        /// </summary>
        public List<string> Validate()
        {
            var problems = new List<string>();
            if (Furnishings == null)
                problems.Add("FurnishingStorageRegistry.Validate: no furnishing register connected.");
            if (Inventory == null)
                problems.Add("FurnishingStorageRegistry.Validate: no household inventory connected.");
            if (Furnishings != null)
            {
                foreach (FurnishingPlacementBinding live in Furnishings.Bindings)
                {
                    if (live == null) continue;
                    EquipmentAsset asset = Furnishings.FindAsset(live.AssetId);
                    if (asset == null || !asset.IsUsable) continue;
                    if (!FurnishingStorageBinding.IsStorageFixtureKind(asset.Kind)) continue;
                    if (live.CapabilityKind == FurnishingCapabilityKind.None)
                        problems.Add($"FurnishingStorageRegistry.Validate: fixture {live.AssetId} ({asset.Kind}) is installed in building {live.BuildingInstanceId} but has no storage zone — placed, not stored.");
                    else if (live.CapabilityKind == FurnishingCapabilityKind.StorageZone
                        && BindingForAsset(live.AssetId) == null)
                        problems.Add($"FurnishingStorageRegistry.Validate: fixture {live.AssetId} carries a storage reference '{live.CapabilityReferenceId}' but has no storage binding — bind it or clear the reference.");
                }
                var seen = new Dictionary<string, string>(StringComparer.Ordinal);
                foreach (FurnishingStorageBinding binding in Bindings)
                {
                    if (binding == null || binding.Zone == null) continue;
                    foreach (StoredLotReference placed in binding.Zone.Placements)
                    {
                        if (placed == null) continue;
                        string key = placed.InventoryAuthorityId + "/" + placed.LotId;
                        string firstZone;
                        if (seen.TryGetValue(key, out firstZone))
                            problems.Add($"FurnishingStorageRegistry.Validate: lot {placed.LotId} is placed in both zone {firstZone} and zone {binding.ZoneId}.");
                        else
                            seen.Add(key, binding.ZoneId);
                    }
                }
            }
            foreach (FurnishingStorageBinding binding in Bindings)
            {
                if (binding == null) continue;
                problems.AddRange(binding.Validate(Furnishings));
            }
            return problems;
        }
    }
}
