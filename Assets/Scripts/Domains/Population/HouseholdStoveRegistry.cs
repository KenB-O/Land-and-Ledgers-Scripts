using System;
using System.Collections.Generic;
using System.Globalization;
using LandLedgers.Economy.Blacksmith;
using LandLedgers.Economy.Equipment;
using LandLedgers.Economy.Equipment.Workstations;

namespace LandLedgers.Population
{
    /// <summary>
    /// B2 (Agent B, expanded campaign 2026-11-16): how a household's
    /// meal-prep task is cooked. The stove gate never blocks eating:
    /// a ready stove means stove cooking, anything else means the
    /// documented cold-food fallback (cold food from stores — the meals
    /// the need authority already counted from HouseholdInventory).
    /// Unspecified means no stove registry was consulted (legacy
    /// callers that do not supply one yet — Agent G's wiring).
    /// </summary>
    public enum HouseholdStoveMealPrepMode
    {
        Unspecified = 0,
        StoveCooking = 1,
        ColdFoodFallback = 2,
    }

    /// <summary>
    /// B2: one installed household stove — an installed usable asset of
    /// kind furniture.cooking-stove whose B0 binding carries the
    /// StoveWorkstation capability. Derived from the household
    /// furnishing register (B0), never invented, never a second
    /// ownership record. FuelWoodUnitsOnHand is the fuel physically in
    /// this stove (the stove fuel store, plan §3.4): it is custody, not
    /// a second reserve — the household fuel-wood reserve stays the
    /// authority for what the household owns, and feeding the stove
    /// moves units reserve -&gt; store, never copies them.
    /// </summary>
    [Serializable]
    public sealed class HouseholdStove
    {
        public string StoveAssetId = string.Empty;
        public int BuildingInstanceId;
        /// <summary>Authored activity location id on the home authority ("stove"). Empty when the binding carries none.</summary>
        public string ActivityLocationId = string.Empty;
        public string WorkstationInstanceId = string.Empty;
        public int FuelWoodUnitsOnHand;
    }

    /// <summary>
    /// B2: one stove fuel store as saved — the persistent half of the
    /// stove fuel custody (plan §3.4). The stove instances themselves
    /// are derived from the B0 register on load and need no saved
    /// section; only the fuel in each stove does. Old saves carry no
    /// entries and restore with empty stores — the honest old-save
    /// default (fuel that was never tracked was never in a stove).
    /// </summary>
    [Serializable]
    public sealed class HouseholdStoveFuelStoreDto
    {
        public int HouseholdId;
        public string StoveAssetId = string.Empty;
        public int FuelWoodUnits;
    }

    /// <summary>
    /// B2: the per-household stove registry (plan §3.4, §5). One
    /// registry per household: the stove instances derived from the B0
    /// furnishing register, the fuel store per stove, the stove-tier
    /// derivation (owner decision O-3), and the meal-prep gate that
    /// runs domestic.meal-prep through the EquipmentTaskGate.
    ///
    /// A stove instance is an installed usable asset of kind
    /// furniture.cooking-stove + a WorkstationInstance of the
    /// household-stove catalog definition. The workstation instance is
    /// built on demand from the stove (it is derived state, like a
    /// sleeping place — it is never persisted and never a second
    /// ownership record). One household may own several stoves; the
    /// gate checks the first currently usable one as the
    /// representative, so a broken stove never blocks a working one.
    ///
    /// Fuel custody (plan §3.4, §5.1): the household fuel-wood reserve
    /// is the only fuel stock. Feeding a stove moves units reserve -&gt;
    /// stove store; cooking/heating completion (B3) debits the store;
    /// a unit is in exactly one place at any time — TotalFuelWoodUnits
    /// (reserve + installed stores + fuel in uninstalled stoves) is
    /// the reconciliation read, and it never double-counts. Fuel in a
    /// removed stove travels with the stove: it is kept as orphaned
    /// fuel, reported by Validate, reclaimable with
    /// TryReturnStoveFuelToReserve, and restored into the stove if it
    /// is re-installed. Fuel is never silently dropped by a rebuild.
    ///
    /// stoveTier derivation (O-3, plan §5.2): the tier is a DERIVED
    /// view of the installed stove instance — instance -&gt; tier, one
    /// direction, the tier never creates an instance. An installed
    /// standard cooking stove derives Basic. A household with no
    /// installed stove keeps its stored HouseholdState.stoveTier field
    /// as the legacy fallback (legacy saves, scenario households not
    /// yet endowed). The efficient stove product variant is Agent F's
    /// later work; when it exists it maps here (see TierForStoveKind).
    /// Agent G wires the derivation into the heating-evaluator call
    /// site; this registry only computes it.
    ///
    /// The meal-prep gate (plan §5.1 step 1): CheckMealPrep runs the
    /// EquipmentTaskGate directly (CheckCodes, the gate's direct-call
    /// use — NOT the TaskAuthority choke point, whose production gate
    /// has no workstation suppliers yet; adding a workstation code to
    /// the meal-prep task definition would refuse every meal-prep
    /// until Agent G wires those suppliers). A ready stove with fuel
    /// available passes as StoveCooking; anything else refuses LOUDLY
    /// as ColdFoodFallback with the reason — the driver records the
    /// fallback on the planned task, never silently, and never blocks
    /// eating. Fuel support counts fuel-wood in the reserve (supplied
    /// by the caller — Agent G wires it from the household reserve)
    /// plus fuel already in the stove stores. A caller that does not
    /// supply the reserve (units -1) gets the store-only answer,
    /// documented in the method.
    /// </summary>
    public sealed class HouseholdStoveRegistry
    {
        /// <summary>The stove asset kind (plan §3.1). The stove is iron — Agent F owns the product line; B owns the contract kind.</summary>
        public const string StoveAssetKind = "furniture.cooking-stove";
        /// <summary>The household-stove workstation definition id (WorkstationCatalog.HouseholdStove).</summary>
        public const string WorkstationId = "household-stove";
        /// <summary>The capability the household stove grants (same capability id as the commercial kitchens — the gate resolves by holder, not by a second capability id).</summary>
        public const string CookMealsCapability = "cook-meals";
        /// <summary>Fuel per cooking session (plan §3.4 default, Agent F calibration later): 1 oven-wood unit per meal-prep session.</summary>
        public const int FuelUnitsPerMealPrepSession = 1;

        public int HouseholdId;
        /// <summary>The household's B0 furnishing register (stoves are derived from it). Null means no stoves.</summary>
        public HouseholdFurnishingRegister Furnishings;
        /// <summary>The household's home building instance id.</summary>
        public int HomeBuildingInstanceId = -1;

        private readonly List<HouseholdStove> stoves = new List<HouseholdStove>();
        /// <summary>Fuel in stoves that are no longer installed, by asset id — it travels with the stove, never silently dropped.</summary>
        private readonly Dictionary<string, int> orphanedFuelWoodUnits = new Dictionary<string, int>(StringComparer.Ordinal);

        /// <summary>Installed stoves, derived from the B0 register.</summary>
        public IReadOnlyList<HouseholdStove> Stoves => stoves;

        public HouseholdStoveRegistry() { }

        public HouseholdStoveRegistry(int householdId, HouseholdFurnishingRegister furnishings)
        {
            HouseholdId = householdId;
            Furnishings = furnishings;
        }

        /// <summary>
        /// B8 (Agent B, expanded campaign 2026-11-16): the creation seam
        /// for a household's stove registry — construct from the B0
        /// furnishing register and immediately re-derive the installed
        /// stoves from it. The stoves list is materialized ONLY by
        /// RebuildFromRegister, so a registry built without the rebuild
        /// reads empty even when a stove is installed; every creator
        /// must go through here (the hub accessor does). A null
        /// furnishing register derives an empty registry, never a throw.
        /// </summary>
        public static HouseholdStoveRegistry CreateDerived(int householdId, HouseholdFurnishingRegister furnishings)
        {
            var registry = new HouseholdStoveRegistry(householdId, furnishings);
            registry.RebuildFromRegister();
            return registry;
        }

        /// <summary>Fuel physically in one stove (installed store, else orphaned fuel for an uninstalled stove, else 0).</summary>
        public int FuelWoodUnitsOnHand(string stoveAssetId)
        {
            if (string.IsNullOrWhiteSpace(stoveAssetId)) return 0;
            foreach (HouseholdStove stove in stoves)
            {
                if (stove != null && stove.StoveAssetId == stoveAssetId)
                    return Math.Max(0, stove.FuelWoodUnitsOnHand);
            }
            int orphaned;
            return orphanedFuelWoodUnits.TryGetValue(stoveAssetId, out orphaned) ? Math.Max(0, orphaned) : 0;
        }

        /// <summary>
        /// Every fuel-wood unit the household owns: the reserve plus
        /// fuel in installed stoves plus fuel in uninstalled stoves.
        /// A unit is in exactly one place — this is the reconciliation
        /// read, and it never double-counts (plan §3.4).
        /// </summary>
        public int TotalFuelWoodUnits(HouseholdState household)
        {
            int total = 0;
            HouseholdReserveState reserve = household != null
                ? household.GetReserve(HouseholdReserveCatalog.FuelWoodCategoryId) : null;
            if (reserve != null) total += Math.Max(0, reserve.currentUnits);
            foreach (HouseholdStove stove in stoves)
            {
                if (stove != null) total += Math.Max(0, stove.FuelWoodUnitsOnHand);
            }
            foreach (int units in orphanedFuelWoodUnits.Values)
                total += Math.Max(0, units);
            return total;
        }

        /// <summary>
        /// The tier of an installed stove product kind. Only the
        /// standard cooking stove exists today (Basic). The efficient
        /// stove product variant is Agent F's later work — when it
        /// exists, its kind maps to Efficient here. Unknown kinds
        /// return null (no tier claimed).
        /// </summary>
        public static HouseholdStoveTier? TierForStoveKind(string kind)
        {
            if (string.Equals(kind, StoveAssetKind, StringComparison.Ordinal))
                return HouseholdStoveTier.Basic;
            return null;
        }

        /// <summary>
        /// O-3 stoveTier derivation (plan §5.2): the tier of the
        /// household's installed stove when one exists, else the
        /// household's stored stoveTier field as the legacy fallback
        /// (legacy saves, unendowed households). One-directional:
        /// instance -&gt; tier. A null household derives Basic.
        /// </summary>
        public HouseholdStoveTier DeriveStoveTier(HouseholdState household)
        {
            foreach (HouseholdStove stove in stoves)
            {
                if (stove == null) continue;
                EquipmentAsset asset = Furnishings != null ? Furnishings.FindAsset(stove.StoveAssetId) : null;
                if (asset == null || !asset.IsUsable) continue;
                HouseholdStoveTier? tier = TierForStoveKind(asset.Kind);
                if (tier.HasValue) return tier.Value;
            }
            return household != null ? household.stoveTier : HouseholdStoveTier.Basic;
        }

        /// <summary>
        /// Re-derives the installed stoves from the B0 register: an
        /// installed usable asset of kind furniture.cooking-stove whose
        /// binding carries the StoveWorkstation capability. Fuel is
        /// never silently dropped by a rebuild: a stove that is still
        /// installed keeps its store; a stove that vanished keeps its
        /// fuel as orphaned fuel (reported by Validate, reclaimable,
        /// restored into the stove if it is re-installed); a stove
        /// that returns takes its orphaned fuel back. The workstation
        /// representative is refreshed with the derivation.
        /// </summary>
        public void RebuildFromRegister()
        {
            var keptFuel = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (HouseholdStove stove in stoves)
            {
                if (stove != null && stove.FuelWoodUnitsOnHand > 0)
                    keptFuel[stove.StoveAssetId] = stove.FuelWoodUnitsOnHand;
            }
            stoves.Clear();
            if (Furnishings != null && Furnishings.Bindings != null)
            {
                foreach (FurnishingPlacementBinding binding in Furnishings.Bindings)
                {
                    if (binding == null || binding.CapabilityKind != FurnishingCapabilityKind.StoveWorkstation) continue;
                    EquipmentAsset asset = Furnishings.FindAsset(binding.AssetId);
                    if (asset == null || asset.Kind != StoveAssetKind || !asset.IsUsable) continue;
                    int fuel = 0;
                    if (!keptFuel.TryGetValue(asset.AssetId, out fuel))
                        orphanedFuelWoodUnits.TryGetValue(asset.AssetId, out fuel);
                    if (fuel > 0) orphanedFuelWoodUnits.Remove(asset.AssetId);
                    stoves.Add(new HouseholdStove
                    {
                        StoveAssetId = asset.AssetId,
                        BuildingInstanceId = binding.BuildingInstanceId,
                        ActivityLocationId = binding.CapabilityReferenceId ?? string.Empty,
                        WorkstationInstanceId = WorkstationId + ":" + asset.AssetId,
                        FuelWoodUnitsOnHand = Math.Max(0, fuel),
                    });
                }
            }
            // Stoves that vanished keep their fuel as orphaned fuel.
            foreach (KeyValuePair<string, int> kept in keptFuel)
            {
                bool stillInstalled = false;
                foreach (HouseholdStove stove in stoves)
                {
                    if (stove != null && stove.StoveAssetId == kept.Key) { stillInstalled = true; break; }
                }
                if (!stillInstalled && kept.Value > 0)
                    orphanedFuelWoodUnits[kept.Key] = kept.Value;
            }
        }

        /// <summary>
        /// Moves fuel-wood units from the household reserve into one
        /// installed stove's store (the retrieval step's custody move,
        /// plan §5.1 step 3 — the trip itself is B3). The reserve is
        /// debited exactly what the store is credited: a unit is in
        /// exactly one place. Refuses loudly when the stove is not
        /// installed, the units are not positive, there is no household
        /// or no fuel-wood reserve, or the reserve is short — and moves
        /// nothing on refusal. Returns null on success.
        /// </summary>
        public string TryFeedStove(string stoveAssetId, int units, HouseholdState household, List<string> diagnostics)
        {
            diagnostics = diagnostics ?? new List<string>();
            if (string.IsNullOrWhiteSpace(stoveAssetId))
                return "HouseholdStoveRegistry.TryFeedStove: stove asset id is required.";
            if (units <= 0)
                return $"HouseholdStoveRegistry.TryFeedStove: {units} units is not a positive feed for stove {stoveAssetId}.";
            HouseholdStove stove = FindStove(stoveAssetId);
            if (stove == null)
                return $"HouseholdStoveRegistry.TryFeedStove: stove {stoveAssetId} is not installed in household {HouseholdId} — feed only an installed stove.";
            if (household == null)
                return "HouseholdStoveRegistry.TryFeedStove: household is required — the fuel comes from its reserve.";
            HouseholdReserveState reserve = household.GetReserve(HouseholdReserveCatalog.FuelWoodCategoryId);
            if (reserve == null)
                return $"HouseholdStoveRegistry.TryFeedStove: household {HouseholdId} has no fuel-wood reserve — nothing to feed stove {stoveAssetId} from.";
            if (reserve.currentUnits < units)
                return $"HouseholdStoveRegistry.TryFeedStove: household {HouseholdId} reserve holds {reserve.currentUnits} fuel-wood units, fewer than the {units} requested for stove {stoveAssetId} — nothing moved.";
            reserve.currentUnits -= units;
            stove.FuelWoodUnitsOnHand += units;
            diagnostics.Add($"HouseholdStoveRegistry: fed {units} fuel-wood unit(s) from household {HouseholdId} reserve to stove {stoveAssetId} (store now {stove.FuelWoodUnitsOnHand}).");
            return null;
        }

        /// <summary>
        /// Debits one installed stove's store (B3 calls this at cooking
        /// / heating completion — the store is debited, never the
        /// reserve, so one burn is one debit). Refuses loudly when the
        /// stove is not installed, the units are not positive, or the
        /// store is short — and debits nothing on refusal. Returns null
        /// on success.
        /// </summary>
        public string TryConsumeStoveFuel(string stoveAssetId, int units, List<string> diagnostics)
        {
            diagnostics = diagnostics ?? new List<string>();
            if (string.IsNullOrWhiteSpace(stoveAssetId))
                return "HouseholdStoveRegistry.TryConsumeStoveFuel: stove asset id is required.";
            if (units <= 0)
                return $"HouseholdStoveRegistry.TryConsumeStoveFuel: {units} units is not a positive consumption for stove {stoveAssetId}.";
            HouseholdStove stove = FindStove(stoveAssetId);
            if (stove == null)
                return $"HouseholdStoveRegistry.TryConsumeStoveFuel: stove {stoveAssetId} is not installed in household {HouseholdId} — consume only from an installed stove's store.";
            if (stove.FuelWoodUnitsOnHand < units)
                return $"HouseholdStoveRegistry.TryConsumeStoveFuel: stove {stoveAssetId} store holds {stove.FuelWoodUnitsOnHand} fuel-wood unit(s), fewer than the {units} requested — nothing consumed.";
            stove.FuelWoodUnitsOnHand -= units;
            return null;
        }

        /// <summary>
        /// Empties one stove's store back into the household reserve —
        /// the "empty the stove" action, for a removed stove's orphaned
        /// fuel or for fuel an installed stove no longer needs. Moves
        /// the whole store, never a partial copy; a stove with an empty
        /// store is a no-op success. Refuses loudly with no household
        /// or no fuel-wood reserve. Returns null on success.
        /// </summary>
        public string TryReturnStoveFuelToReserve(string stoveAssetId, HouseholdState household, List<string> diagnostics)
        {
            diagnostics = diagnostics ?? new List<string>();
            if (string.IsNullOrWhiteSpace(stoveAssetId))
                return "HouseholdStoveRegistry.TryReturnStoveFuelToReserve: stove asset id is required.";
            if (household == null)
                return "HouseholdStoveRegistry.TryReturnStoveFuelToReserve: household is required — the fuel returns to its reserve.";
            HouseholdReserveState reserve = household.GetReserve(HouseholdReserveCatalog.FuelWoodCategoryId);
            if (reserve == null)
                return $"HouseholdStoveRegistry.TryReturnStoveFuelToReserve: household {HouseholdId} has no fuel-wood reserve — nowhere to return stove {stoveAssetId} fuel to.";
            int units = FuelWoodUnitsOnHand(stoveAssetId);
            if (units <= 0) return null;
            HouseholdStove stove = FindStove(stoveAssetId);
            if (stove != null)
                stove.FuelWoodUnitsOnHand = 0;
            else
                orphanedFuelWoodUnits.Remove(stoveAssetId);
            reserve.currentUnits += units;
            diagnostics.Add($"HouseholdStoveRegistry: returned {units} fuel-wood unit(s) from stove {stoveAssetId} to household {HouseholdId} reserve.");
            return null;
        }

        /// <summary>
        /// The meal-prep gate (plan §5.1 step 1): runs the
        /// EquipmentTaskGate directly against this household's stove —
        /// workstation:household-stove, holder household, with the stove
        /// asset as the component and fuel-wood on hand (reserve +
        /// stove stores) as the support context. Returns StoveCooking
        /// when the gate passes (a ready stove with fuel available) and
        /// ColdFoodFallback with the loud refusal reason otherwise.
        /// The reason is also appended to diagnostics, so the fallback
        /// is never silent. fuelWoodReserveUnits is the household's
        /// fuel-wood reserve units as the caller reads them (Agent G
        /// wires it from the household reserve); -1 means unknown, in
        /// which case only fuel already in the stove stores counts.
        /// Never blocks eating: the caller records the fallback on the
        /// planned task and the household eats cold food from stores.
        /// </summary>
        public HouseholdStoveMealPrepMode CheckMealPrep(int dayIndex, int fuelWoodReserveUnits, List<string> diagnostics, out string reason)
        {
            diagnostics = diagnostics ?? new List<string>();
            reason = null;
            var gate = new EquipmentTaskGate
            {
                AssetsSupplier = () => Furnishings != null && Furnishings.Assets != null
                    ? new List<EquipmentAsset>(Furnishings.Assets) : new List<EquipmentAsset>(),
                WorkstationsFor = (holderKind, holderId) => string.Equals(holderKind, "household", StringComparison.Ordinal)
                    ? BuildWorkstationsHolder() : null,
                FindWorkstationDefinition = id => string.Equals(id, WorkstationId, StringComparison.Ordinal)
                    ? WorkstationCatalog.HouseholdStove : null,
                FindComponent = assetId =>
                {
                    EquipmentAsset asset = Furnishings != null ? Furnishings.FindAsset(assetId) : null;
                    if (asset == null) return null;
                    return new WorkstationComponentView
                    {
                        AssetId = asset.AssetId,
                        Kind = asset.Kind,
                        Condition01 = asset.Condition01,
                        IsUsable = asset.IsUsable,
                    };
                },
                SupportSupplier = () =>
                {
                    var support = new SupportContext();
                    int onHand = 0;
                    if (fuelWoodReserveUnits >= 0) onHand += fuelWoodReserveUnits;
                    foreach (HouseholdStove stove in stoves)
                    {
                        if (stove != null) onHand += Math.Max(0, stove.FuelWoodUnitsOnHand);
                    }
                    support.ConsumableUnits[SupportContext.Key("fuel", "oven-wood")] = onHand;
                    return support;
                },
            };
            string refusal = gate.CheckCodes(
                new List<string> { EquipmentRequirementCodes.Workstation(WorkstationId) },
                "household", HouseholdId.ToString(CultureInfo.InvariantCulture), dayIndex, diagnostics);
            if (refusal == null) return HouseholdStoveMealPrepMode.StoveCooking;
            reason = refusal;
            diagnostics.Add($"HouseholdStoveRegistry household {HouseholdId}: meal-prep falls back to cold food — {refusal}");
            return HouseholdStoveMealPrepMode.ColdFoodFallback;
        }

        /// <summary>
        /// The workstation holder the gate reads: one representative
        /// household-stove instance — the first currently usable stove
        /// (a broken stove never blocks a working one). Built fresh per
        /// call from the derived stoves, so it can never go stale
        /// between rebuilds. Agent G's production wiring uses this same
        /// holder for the gate's WorkstationsFor supplier.
        /// </summary>
        public BusinessWorkstations BuildWorkstationsHolder()
        {
            var holder = new BusinessWorkstations(HouseholdId.ToString(CultureInfo.InvariantCulture));
            HouseholdStove representative = null;
            foreach (HouseholdStove stove in stoves)
            {
                if (stove == null) continue;
                EquipmentAsset asset = Furnishings != null ? Furnishings.FindAsset(stove.StoveAssetId) : null;
                if (asset == null || !asset.IsUsable) continue;
                representative = stove;
                break;
            }
            WorkstationInstance instance = holder.GetOrCreate(WorkstationId);
            if (instance == null) return holder;
            if (representative != null)
            {
                instance.SpaceId = HouseholdFurnishingRegister.BuildingLocationId(representative.BuildingInstanceId);
                instance.ComponentAssetIds.Clear();
                instance.InstallComponent(representative.StoveAssetId);
            }
            else
            {
                instance.SpaceId = HouseholdFurnishingRegister.BuildingLocationId(HomeBuildingInstanceId);
                instance.ComponentAssetIds.Clear();
            }
            return holder;
        }

        /// <summary>
        /// The installed stove for one asset id, or null when it is not
        /// installed (in custody, removed, or never a stove).
        /// </summary>
        public HouseholdStove FindStove(string stoveAssetId)
        {
            if (string.IsNullOrWhiteSpace(stoveAssetId)) return null;
            foreach (HouseholdStove stove in stoves)
            {
                if (stove != null && stove.StoveAssetId == stoveAssetId) return stove;
            }
            return null;
        }

        /// <summary>
        /// The stove fuel stores as save data: one entry per installed
        /// stove with fuel and one per orphaned store. Fresh list, fresh
        /// entries — the caller may mutate the result freely.
        /// </summary>
        public List<HouseholdStoveFuelStoreDto> CaptureFuelStores()
        {
            var captured = new List<HouseholdStoveFuelStoreDto>();
            foreach (HouseholdStove stove in stoves)
            {
                if (stove == null || stove.FuelWoodUnitsOnHand <= 0) continue;
                captured.Add(new HouseholdStoveFuelStoreDto
                {
                    HouseholdId = HouseholdId,
                    StoveAssetId = stove.StoveAssetId,
                    FuelWoodUnits = stove.FuelWoodUnitsOnHand,
                });
            }
            foreach (KeyValuePair<string, int> orphaned in orphanedFuelWoodUnits)
            {
                if (orphaned.Value <= 0) continue;
                captured.Add(new HouseholdStoveFuelStoreDto
                {
                    HouseholdId = HouseholdId,
                    StoveAssetId = orphaned.Key,
                    FuelWoodUnits = orphaned.Value,
                });
            }
            return captured;
        }

        /// <summary>
        /// Restores the stove fuel stores from save data
        /// (clear-then-load, idempotent): an entry for a currently
        /// installed stove fills its store; an entry for any other
        /// asset becomes orphaned fuel. Entries for another household
        /// are ignored loudly by the caller (this method only sees
        /// this household's entries — the save seam filters by
        /// HouseholdId). Fuel for stoves not in the saved data is
        /// cleared — a load replaces, never merges.
        /// </summary>
        public void RestoreFuelStores(IEnumerable<HouseholdStoveFuelStoreDto> saved)
        {
            foreach (HouseholdStove stove in stoves)
            {
                if (stove != null) stove.FuelWoodUnitsOnHand = 0;
            }
            orphanedFuelWoodUnits.Clear();
            if (saved == null) return;
            foreach (HouseholdStoveFuelStoreDto entry in saved)
            {
                if (entry == null || string.IsNullOrWhiteSpace(entry.StoveAssetId) || entry.FuelWoodUnits <= 0) continue;
                HouseholdStove stove = FindStove(entry.StoveAssetId);
                if (stove != null)
                    stove.FuelWoodUnitsOnHand = entry.FuelWoodUnits;
                else
                    orphanedFuelWoodUnits[entry.StoveAssetId] = entry.FuelWoodUnits;
            }
        }

        /// <summary>
        /// Structural diagnostics: no furnishing register; installed
        /// stoves whose asset is gone or unusable (a stale derivation —
        /// rebuild to clear); orphaned fuel (fuel in a stove that is no
        /// longer installed — return it to the reserve or reinstall the
        /// stove; the sale flow must settle it before the asset leaves
        /// the register). Empty means the registry is internally
        /// consistent.
        /// </summary>
        public List<string> Validate()
        {
            var problems = new List<string>();
            if (Furnishings == null)
            {
                problems.Add($"HouseholdStoveRegistry {HouseholdId}: no furnishing register — no stoves can be derived.");
                return problems;
            }
            foreach (HouseholdStove stove in stoves)
            {
                if (stove == null) { problems.Add($"HouseholdStoveRegistry {HouseholdId}: null stove entry."); continue; }
                EquipmentAsset asset = Furnishings.FindAsset(stove.StoveAssetId);
                if (asset == null)
                    problems.Add($"HouseholdStoveRegistry {HouseholdId}: stove {stove.StoveAssetId} is not in this register — rebuild to clear it.");
                else if (!asset.IsUsable)
                    problems.Add($"HouseholdStoveRegistry {HouseholdId}: stove {stove.StoveAssetId} is unusable (condition {asset.Condition01:0.00}) — it no longer cooks; rebuild to clear it.");
            }
            foreach (KeyValuePair<string, int> orphaned in orphanedFuelWoodUnits)
            {
                if (orphaned.Value <= 0) continue;
                problems.Add($"HouseholdStoveRegistry {HouseholdId}: {orphaned.Value} fuel-wood unit(s) sit in uninstalled stove {orphaned.Key} — return them to the reserve (TryReturnStoveFuelToReserve) or reinstall the stove.");
            }
            return problems;
        }
    }
}
