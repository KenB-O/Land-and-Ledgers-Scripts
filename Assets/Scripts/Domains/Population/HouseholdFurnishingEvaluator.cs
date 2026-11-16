using System;
using System.Collections.Generic;
using LandLedgers.Economy.Blacksmith;
using LandLedgers.Economy.Equipment;

namespace LandLedgers.Population
{
    /// <summary>
    /// B6 (Agent B, expanded campaign 2026-11-16): why a household wants a
    /// piece of furniture. The ranked want list the acquisition path
    /// consumes (plan §8) — a want that cannot be afforded stays a want
    /// (visible in diagnostics), never forced, never auto-bought. Furniture
    /// already owned is never re-bought: the evaluator reads the register
    /// and the B1/B4 need signals first.
    ///
    /// Priority (documented, owner-correctable): 1 bed for floor sleepers,
    /// 2 stove for a no-stove household, 3 table when chairs exist without
    /// a table (the table is the root cause — more chairs cannot help) or
    /// chair when a table exists and diners fall back, 4 chair when the
    /// household has neither table nor chairs, 5 closed storage when the
    /// household holds lots and has no storage fixture at all.
    /// </summary>
    public enum FurnishingWantReason
    {
        Unspecified = 0,
        /// <summary>Members sleeping on the floor (B1 UncoveredSleepers).</summary>
        BedForFloorSleeper = 1,
        /// <summary>No usable stove installed (cold-food fallback, B2).</summary>
        StoveForNoStove = 2,
        /// <summary>Diners falling back although a table exists (B4 UncoveredDiners).</summary>
        ChairForFallbackDiner = 3,
        /// <summary>Chairs exist but no table makes them seats.</summary>
        TableForChairsWithoutTable = 4,
        /// <summary>Lots on hand and no storage fixture at all (B5).</summary>
        CabinetForNoStorage = 5,
    }

    /// <summary>
    /// B6: how a want is acquired. New is the default; Secondhand is the
    /// poor household's first channel (plan §8, O-F6) — a want that cannot
    /// be afforded new stays a want with the secondhand channel noted,
    /// never a used price (the evaluator does not price used goods; the
    /// UsedEquipmentMarket does).
    /// </summary>
    public enum FurnishingWantChannel
    {
        Unspecified = 0,
        New = 1,
        Secondhand = 2,
    }

    /// <summary>
    /// B6: why a household might sell a piece of furniture. A decision
    /// input only — nothing here sells anything; the sale executes
    /// through the staged sale / secondhand market path.
    /// </summary>
    public enum FurnishingResaleReason
    {
        Unspecified = 0,
        /// <summary>More installed than the household can use.</summary>
        Surplus = 1,
        /// <summary>At or below the usable threshold — salvage/part-out only (Canon §11.6).</summary>
        Wrecked = 2,
        /// <summary>In custody, never installed, and already sufficiently installed.</summary>
        UnusedInCustody = 3,
    }

    /// <summary>B6: one ranked furnishing want. EstimatedCostCents is -1 when no price data was supplied.</summary>
    [Serializable]
    public sealed class FurnishingWant
    {
        public string ItemKind = string.Empty;
        public FurnishingWantReason Reason;
        public int PersonsAffected;
        public int Priority;
        public int EstimatedCostCents = -1;
        public bool Affordable;
        public FurnishingWantChannel Channel;
        public string Note = string.Empty;
    }

    /// <summary>B6: one resale candidate. GuidanceValueCents is -1 when no price guidance exists.</summary>
    [Serializable]
    public sealed class FurnishingResaleLine
    {
        public string AssetId = string.Empty;
        public string Kind = string.Empty;
        public FurnishingResaleReason Reason;
        public float Condition01;
        public int GuidanceValueCents = -1;
        public string Note = string.Empty;
    }

    /// <summary>
    /// B6: inputs to the furnishing evaluation. Registries are optional —
    /// a missing registry means that want class is not evaluated, never
    /// guessed. Cash is read-only here; buying moves cash only through
    /// the HouseholdLedger. Replacement costs come from the caller (Agent
    /// F's prices); a null func means every want is unpriced.
    /// </summary>
    [Serializable]
    public sealed class HouseholdFurnishingEvaluationInputs
    {
        public int HouseholdId;
        public List<int> MemberIds = new List<int>();
        public int CashCents;
        public HouseholdFurnishingRegister Furnishings;
        public SleepingPlaceRegistry Sleeping;
        public DiningPlaceRegistry Dining;
        public HouseholdStoveRegistry Stoves;
        /// <summary>True when the household has at least one live storage binding (B5).</summary>
        public bool HasStorageZone;
        /// <summary>Lots on hand (live QuantityUnits summed) — the storage want needs something to store.</summary>
        public int StoredLotUnits;
        public Func<string, int> ReplacementCostCentsForKind;
        public int DayIndex;
    }

    /// <summary>
    /// B6: the household furnishing evaluator (plan §8). Pure functions
    /// over real state — no cash moved, no assets created, no supplier
    /// invented, no item forced. Secondhand is a first-class channel.
    /// </summary>
    public static class HouseholdFurnishingEvaluator
    {
        /// <summary>Morning meal window (DailyActivityPlanner defaults 07:30, 30 min) — the representative dining signal.</summary>
        public const int EvaluationMealStartMinute = 450;
        public const int EvaluationMealDurationMinutes = 30;

        /// <summary>
        /// B6: the ranked want list for one household, highest priority
        /// first. Each want is priced from the caller's replacement costs
        /// (-1 when unpriced) and affordability-gated against cash on hand;
        /// an unaffordable want stays in the list with Affordable false
        /// and the Secondhand channel noted.
        /// </summary>
        public static List<FurnishingWant> EvaluateWants(HouseholdFurnishingEvaluationInputs inputs)
        {
            var wants = new List<FurnishingWant>();
            if (inputs == null) return wants;
            int members = inputs.MemberIds != null ? inputs.MemberIds.Count : 0;

            if (inputs.Sleeping != null && inputs.MemberIds != null && inputs.MemberIds.Count > 0)
            {
                List<int> uncovered = inputs.Sleeping.UncoveredSleepers(inputs.MemberIds, inputs.DayIndex);
                if (uncovered != null && uncovered.Count > 0)
                    wants.Add(MakeWant(FurnishingClassification.BedKind, FurnishingWantReason.BedForFloorSleeper,
                        uncovered.Count, 1, inputs));
            }

            bool hasStove = HasUsableStove(inputs);
            if (inputs.Furnishings != null || inputs.Stoves != null)
            {
                if (!hasStove)
                    wants.Add(MakeWant(FurnishingClassification.CookingStoveKind, FurnishingWantReason.StoveForNoStove,
                        Math.Max(1, members), 2, inputs));
            }

            if (inputs.Dining != null && inputs.MemberIds != null && inputs.MemberIds.Count > 0)
            {
                List<int> uncovered = inputs.Dining.UncoveredDiners(
                    inputs.MemberIds, inputs.DayIndex,
                    EvaluationMealStartMinute, EvaluationMealDurationMinutes);
                if (uncovered != null && uncovered.Count > 0)
                {
                    // Tables that make chairs seats (the dining registry's
                    // own derivation — B4). Chairs owned are counted
                    // capability-agnostic: a chair without the DiningSeat
                    // capability is still a chair the household owns, and
                    // buying another one cannot fix a missing table.
                    int tables = inputs.Dining.InstalledTableCount();
                    int chairsOwned = InstalledUsableCount(inputs.Furnishings, FurnishingClassification.ChairKind);
                    if (tables == 0)
                    {
                        wants.Add(MakeWant(FurnishingClassification.TableKind, FurnishingWantReason.TableForChairsWithoutTable,
                            Math.Max(1, members), 3, inputs));
                        if (chairsOwned == 0)
                            wants.Add(MakeWant(FurnishingClassification.ChairKind, FurnishingWantReason.ChairForFallbackDiner,
                                uncovered.Count, 4, inputs));
                    }
                    else
                    {
                        wants.Add(MakeWant(FurnishingClassification.ChairKind, FurnishingWantReason.ChairForFallbackDiner,
                            uncovered.Count, 3, inputs));
                    }
                }
            }

            if (!inputs.HasStorageZone && inputs.StoredLotUnits > 0)
                wants.Add(MakeWant(FurnishingClassification.CabinetSmallKind, FurnishingWantReason.CabinetForNoStorage,
                    Math.Max(1, members), 5, inputs));

            wants.Sort((a, b) =>
            {
                int byPriority = a.Priority.CompareTo(b.Priority);
                return byPriority != 0 ? byPriority : b.PersonsAffected.CompareTo(a.PersonsAffected);
            });
            return wants;
        }

        /// <summary>
        /// B6: resale candidates for one household — surplus installed
        /// pieces, wrecked pieces, and custody pieces already sufficiently
        /// installed. One line per asset. Guidance comes from the used-
        /// equipment price guide (Canon §11.6 condition-weighted value);
        /// -1 when no guidance exists. Nothing is sold here.
        /// </summary>
        public static List<FurnishingResaleLine> EvaluateResale(
            HouseholdFurnishingRegister register, int memberCount,
            Func<string, int> replacementCostCentsForKind)
        {
            var lines = new List<FurnishingResaleLine>();
            if (register == null || register.Assets == null) return lines;
            int installedBeds = InstalledUsableCount(register, FurnishingClassification.BedKind);
            int installedChairs = InstalledUsableCount(register, FurnishingClassification.ChairKind);
            int installedTables = InstalledUsableCount(register, FurnishingClassification.TableKind);
            var seen = new HashSet<string>(StringComparer.Ordinal);

            foreach (EquipmentAsset asset in register.Assets)
            {
                if (asset == null || string.IsNullOrWhiteSpace(asset.AssetId)) continue;
                if (!seen.Add(asset.AssetId)) continue;
                bool installed = register.LiveBindingFor(asset.AssetId) != null;
                if (!asset.IsUsable)
                {
                    lines.Add(MakeResaleLine(asset, FurnishingResaleReason.Wrecked,
                        "unusable — salvage/part-out only.", replacementCostCentsForKind));
                    continue;
                }
                if (installed)
                {
                    int allowed = SurplusAllowance(asset.Kind, memberCount);
                    if (allowed < 0) continue;
                    int installedOfKind = InstalledUsableCount(register, asset.Kind);
                    if (installedOfKind > allowed)
                    {
                        int excess = installedOfKind - allowed;
                        int rank = RankAmongInstalled(register, asset);
                        if (rank >= allowed && excess > 0)
                            lines.Add(MakeResaleLine(asset, FurnishingResaleReason.Surplus,
                                $"installed {installedOfKind}, household needs {allowed}.", replacementCostCentsForKind));
                    }
                    continue;
                }
                int sufficient = SurplusAllowance(asset.Kind, memberCount);
                if (sufficient >= 0 && InstalledUsableCount(register, asset.Kind) >= sufficient && sufficient > 0)
                    lines.Add(MakeResaleLine(asset, FurnishingResaleReason.UnusedInCustody,
                        "in custody, never installed, and already sufficiently installed.", replacementCostCentsForKind));
            }
            return lines;
        }

        private static FurnishingWant MakeWant(
            string itemKind, FurnishingWantReason reason, int personsAffected, int priority,
            HouseholdFurnishingEvaluationInputs inputs)
        {
            int cost = -1;
            bool affordable = false;
            if (inputs.ReplacementCostCentsForKind != null)
            {
                cost = Math.Max(0, inputs.ReplacementCostCentsForKind(itemKind));
                if (cost <= 0) cost = -1;
                else affordable = inputs.CashCents >= cost;
            }
            return new FurnishingWant
            {
                ItemKind = itemKind,
                Reason = reason,
                PersonsAffected = Math.Max(0, personsAffected),
                Priority = priority,
                EstimatedCostCents = cost,
                Affordable = affordable,
                Channel = affordable ? FurnishingWantChannel.New : FurnishingWantChannel.Secondhand,
                Note = cost < 0
                    ? "no replacement cost supplied — unpriced."
                    : affordable
                        ? string.Empty
                        : $"cash {inputs.CashCents}c is short of {cost}c — secondhand is the first channel.",
            };
        }

        private static FurnishingResaleLine MakeResaleLine(
            EquipmentAsset asset, FurnishingResaleReason reason, string note,
            Func<string, int> replacementCostCentsForKind)
        {
            int guidance = -1;
            if (reason == FurnishingResaleReason.Wrecked)
            {
                // Canon §11.6, the same rule as TransferableValueCents: an
                // asset at or below the usable threshold contributes nothing.
                // The price guide's condition-weighted number is a floor for
                // usable assets only (its own note says ~0c for wrecked).
                guidance = 0;
            }
            else if (replacementCostCentsForKind != null)
            {
                UsedEquipmentPriceGuidance g = UsedEquipmentPriceGuide.GuidanceFor(asset, replacementCostCentsForKind);
                if (g != null && g.HasGuidance) guidance = g.ConditionWeightedValueCents;
            }
            return new FurnishingResaleLine
            {
                AssetId = asset.AssetId,
                Kind = asset.Kind,
                Reason = reason,
                Condition01 = asset.Condition01,
                GuidanceValueCents = guidance,
                Note = note,
            };
        }

        /// <summary>
        /// B6: a usable stove is one the stove registry derives, or — when
        /// no stove registry is supplied — an installed usable cooking-stove
        /// asset bound with the StoveWorkstation capability (B2's install
        /// path sets it). A stove installed without the capability is
        /// placed but not a working stove.
        /// </summary>
        private static bool HasUsableStove(HouseholdFurnishingEvaluationInputs inputs)
        {
            if (inputs.Stoves != null) return inputs.Stoves.Stoves != null && inputs.Stoves.Stoves.Count > 0;
            if (inputs.Furnishings == null || inputs.Furnishings.Bindings == null) return false;
            foreach (FurnishingPlacementBinding binding in inputs.Furnishings.Bindings)
            {
                if (binding == null || binding.CapabilityKind != FurnishingCapabilityKind.StoveWorkstation) continue;
                EquipmentAsset asset = inputs.Furnishings.FindAsset(binding.AssetId);
                if (asset != null && asset.Kind == FurnishingClassification.CookingStoveKind && asset.IsUsable) return true;
            }
            return false;
        }

        /// <summary>B6: installed usable assets of one kind, capability-agnostic — an owned piece is an owned piece.</summary>
        private static int InstalledUsableCount(HouseholdFurnishingRegister register, string kind)
        {
            if (register == null || register.Assets == null) return 0;
            int count = 0;
            foreach (EquipmentAsset asset in register.Assets)
            {
                if (asset == null || asset.Kind != kind || !asset.IsUsable) continue;
                if (register.LiveBindingFor(asset.AssetId) != null) count++;
            }
            return count;
        }

        /// <summary>
        /// B6: how many installed pieces of a kind a household can use —
        /// beds and chairs one per member, tables one per household.
        /// Other kinds have no surplus rule (-1).
        /// </summary>
        private static int SurplusAllowance(string kind, int memberCount)
        {
            if (kind == FurnishingClassification.BedKind) return Math.Max(0, memberCount);
            if (kind == FurnishingClassification.ChairKind) return Math.Max(0, memberCount);
            if (kind == FurnishingClassification.TableKind) return 1;
            return -1;
        }

        /// <summary>
        /// B6: the asset's rank among installed usable assets of its kind,
        /// in register order — the first N (the allowance) are the keepers,
        /// the rest are the surplus. Deterministic, no sorting by
        /// condition: the seller chooses which piece to sell, this only
        /// counts how many are surplus.
        /// </summary>
        private static int RankAmongInstalled(HouseholdFurnishingRegister register, EquipmentAsset asset)
        {
            int rank = 0;
            foreach (EquipmentAsset other in register.Assets)
            {
                if (other == null || other.Kind != asset.Kind || !other.IsUsable) continue;
                if (register.LiveBindingFor(other.AssetId) == null) continue;
                if (other.AssetId == asset.AssetId) return rank;
                rank++;
            }
            return rank;
        }
    }
}
