using System;
using System.Collections.Generic;
using UnityEngine;

namespace LandLedgers.Population
{
    /// <summary>
    /// Phase B (Real People): a household-consumable item. Units are whole
    /// counts in the item's unit label (lb, loaf, jar, ...). MealServingUnits
    /// is the default quantity that constitutes one substantial meal serving;
    /// the MealSchedulingPolicy may override it per scenario (configurable,
    /// never a universal constant).
    /// </summary>
    public sealed class HouseholdItemDefinition
    {
        public HouseholdItemDefinition(
            string itemId,
            string displayName,
            string unitLabel,
            string reserveCategoryId,
            int mealServingUnits,
            bool isFood)
        {
            ItemId = string.IsNullOrWhiteSpace(itemId) ? "item" : itemId;
            DisplayName = string.IsNullOrWhiteSpace(displayName) ? ItemId : displayName;
            UnitLabel = string.IsNullOrWhiteSpace(unitLabel) ? "unit" : unitLabel;
            ReserveCategoryId = reserveCategoryId ?? string.Empty;
            MealServingUnits = Mathf.Max(1, mealServingUnits);
            IsFood = isFood;
        }

        public string ItemId { get; }
        public string DisplayName { get; }
        public string UnitLabel { get; }
        public string ReserveCategoryId { get; }
        public int MealServingUnits { get; }
        public bool IsFood { get; }
    }

    /// <summary>
    /// Phase B: the household item catalog. Item ids are stable strings
    /// (append-only: new items are added, never renamed).
    /// </summary>
    public static class HouseholdItemCatalog
    {
        public const string FlourId = "flour";
        public const string BreadId = "bread";
        public const string PotatoesId = "potatoes";
        public const string PreservedFoodId = "preserved_food";
        public const string PreservedMeatId = "preserved_meat";
        public const string FuelWoodId = "fuel_wood";
        public const string SoapId = "soap";
        public const string ClothingTextilesId = "clothing_textiles";
        public const string SaltId = "salt";

        /// <summary>
        /// Migration-only item: legacy reserve units had no composition, so
        /// the one-time reserve-to-lot conversion books them honestly as
        /// unspecified staple food rather than inventing flour/bread/potatoes.
        /// New purchases must use real items.
        /// </summary>
        public const string UnspecifiedStapleFoodId = "staple_food_unspecified";

        private static readonly HouseholdItemDefinition[] Definitions =
        {
            new(FlourId, "Flour", "lb", "staple_food", 2, true),
            new(BreadId, "Bread", "loaf", "staple_food", 1, true),
            new(PotatoesId, "Potatoes", "lb", "staple_food", 2, true),
            new(PreservedFoodId, "Preserved Food", "jar", "staple_food", 1, true),
            new(PreservedMeatId, "Preserved Meat", "lb", "meat", 1, true),
            new(FuelWoodId, "Fuel Wood", "bundle", "fuel_wood", 0, false),
            new(SoapId, "Soap", "bar", "household_goods", 0, false),
            new(ClothingTextilesId, "Clothing/Textiles", "yd", "clothing", 0, false),
            new(SaltId, "Salt", "lb", "household_goods", 0, false),
            new(UnspecifiedStapleFoodId, "Staple Food (legacy, composition unspecified)", "unit", "staple_food", 2, true),
        };

        private static readonly Dictionary<string, HouseholdItemDefinition> ById = BuildIndex();

        private static Dictionary<string, HouseholdItemDefinition> BuildIndex()
        {
            var index = new Dictionary<string, HouseholdItemDefinition>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < Definitions.Length; i++)
            {
                index[Definitions[i].ItemId] = Definitions[i];
            }

            return index;
        }

        public static HouseholdItemDefinition Get(string itemId)
        {
            if (string.IsNullOrWhiteSpace(itemId))
            {
                return null;
            }

            return ById.TryGetValue(itemId, out HouseholdItemDefinition definition) ? definition : null;
        }

        public static IReadOnlyList<HouseholdItemDefinition> All => Definitions;
    }

    /// <summary>
    /// Phase B: one real lot of a household item. Quantity is whole units;
    /// HeldUnits are reserved for a sale/shipment commitment and are NOT
    /// available for consumption — a unit is never simultaneously available
    /// for consumption, sale, and shipment.
    /// </summary>
    [Serializable]
    public sealed class HouseholdItemLot
    {
        public int LotId;
        public string ItemId = string.Empty;
        public int QuantityUnits;
        public string UnitLabel = string.Empty;
        public int AcquiredDayIndex;
        public string SourceLabel = string.Empty;
        public int HeldUnits;
        // Pass IX WS6 (IDENT-001, additive): lot-identity facets used by
        // split/merge compatibility. Lots created before this pass carry
        // the defaults (empty band/kind, owner -1) and merge only with
        // lots sharing those exact facets.
        public string QualityBand = string.Empty;
        public string SourceKind = string.Empty;
        public int OwnerPersonId = -1;
        /// <summary>Lineage: ids of lots this lot descends from (split parents, merged constituents).</summary>
        public List<int> AncestryLotIds = new List<int>();

        public int AvailableUnits => Mathf.Max(0, QuantityUnits - Mathf.Max(0, HeldUnits));
    }

    /// <summary>Phase B: one traceable consumption of a lot.</summary>
    [Serializable]
    public sealed class HouseholdConsumptionRecord
    {
        public int DayIndex;
        public int HouseholdId;
        public string ItemId = string.Empty;
        public int LotId;
        public int QuantityUnits;
        public string Purpose = string.Empty;
        public string ConsumerLabel = string.Empty;
    }

    /// <summary>Phase B: per-household inventory save state.</summary>
    [Serializable]
    public sealed class HouseholdInventoryState
    {
        public int HouseholdId;
        public int NextLotSequence;
        public bool SeededFromReserves;
        public List<HouseholdItemLot> Lots = new List<HouseholdItemLot>();
    }

    /// <summary>
    /// Phase B (Real People): a household's real inventory — actual item lots
    /// with quantities and units. Consumption removes real quantities (FIFO
    /// across lots). Lots carry acquisition provenance; sourceless additions
    /// are rejected, never silently booked (Canon 13.2 upstream-provenance
    /// doctrine: real lots, real suppliers, no synthetic stock).
    /// </summary>
    public sealed class HouseholdInventory
    {
        private readonly HouseholdInventoryState state;
        private readonly List<string> diagnostics = new List<string>();

        public HouseholdInventory(HouseholdInventoryState state)
        {
            this.state = state ?? throw new ArgumentNullException(nameof(state));
        }

        public HouseholdInventory(int householdId)
            : this(new HouseholdInventoryState { HouseholdId = householdId })
        {
        }

        public IReadOnlyList<string> Diagnostics => diagnostics;
        public int HouseholdId => state.HouseholdId;
        public HouseholdInventoryState State => state;
        public IReadOnlyList<HouseholdItemLot> Lots => state.Lots;

        /// <summary>
        /// Adds a lot. Rejects unknown items, non-positive quantities, and
        /// sourceless additions (no synthetic stock).
        /// </summary>
        public string AddLot(string itemId, int quantityUnits, int dayIndex, string sourceLabel,
            string qualityBand = "", string sourceKind = "", int ownerPersonId = -1)
        {
            HouseholdItemDefinition definition = HouseholdItemCatalog.Get(itemId);
            if (definition == null)
            {
                return Reject($"Rejected lot: unknown item '{itemId}'.");
            }

            if (quantityUnits <= 0)
            {
                return Reject($"Rejected lot of {definition.ItemId}: quantity must be positive.");
            }

            if (string.IsNullOrWhiteSpace(sourceLabel))
            {
                return Reject($"Rejected lot of {definition.ItemId}: source is required (no synthetic stock).");
            }

            state.Lots.Add(new HouseholdItemLot
            {
                LotId = state.NextLotSequence++,
                ItemId = definition.ItemId,
                QuantityUnits = quantityUnits,
                UnitLabel = definition.UnitLabel,
                AcquiredDayIndex = Mathf.Max(0, dayIndex),
                SourceLabel = sourceLabel,
                QualityBand = qualityBand ?? string.Empty,
                SourceKind = sourceKind ?? string.Empty,
                OwnerPersonId = ownerPersonId,
            });
            return null;
        }

        /// <summary>
        /// Pass IX WS6 (IDENT-001): splits units off a lot into a new lot
        /// with the same identity facets. Splits draw on AVAILABLE units
        /// only (held units stay committed to their purpose) and must
        /// leave the source non-empty. The child records the parent in its
        /// ancestry (plus the parent's own ancestry). Returns the new lot
        /// id, or -1 with a diagnostic when refused.
        /// </summary>
        public int SplitLot(int sourceLotId, int units, int dayIndex)
        {
            HouseholdItemLot source = null;
            for (int i = 0; i < state.Lots.Count; i++)
            {
                if (state.Lots[i] != null && state.Lots[i].LotId == sourceLotId)
                {
                    source = state.Lots[i];
                    break;
                }
            }
            if (source == null)
            {
                Reject($"SplitLot: no lot {sourceLotId} in household {state.HouseholdId}.");
                return -1;
            }
            if (units <= 0 || units >= source.QuantityUnits || units > source.AvailableUnits)
            {
                Reject($"SplitLot: cannot split {units} units from lot {sourceLotId} " +
                    $"({source.QuantityUnits} units, {source.AvailableUnits} available) — a split must be positive, partial, and unheld.");
                return -1;
            }

            var child = new HouseholdItemLot
            {
                LotId = state.NextLotSequence++,
                ItemId = source.ItemId,
                QuantityUnits = units,
                UnitLabel = source.UnitLabel,
                AcquiredDayIndex = Mathf.Max(0, dayIndex),
                SourceLabel = source.SourceLabel,
                QualityBand = source.QualityBand ?? string.Empty,
                SourceKind = source.SourceKind ?? string.Empty,
                OwnerPersonId = source.OwnerPersonId,
            };
            if (source.AncestryLotIds != null)
                child.AncestryLotIds.AddRange(source.AncestryLotIds);
            child.AncestryLotIds.Add(source.LotId);
            source.QuantityUnits -= units;
            state.Lots.Add(child);
            return child.LotId;
        }

        /// <summary>
        /// Pass IX WS6 (IDENT-001): merges lots that share item, quality
        /// band, source kind, and owner into their OLDEST lot (earliest
        /// acquisition day, then lowest lot id). Lots with held units never
        /// merge (their commitment bookkeeping stays distinct). The
        /// survivor's ancestry becomes the union of every merged lot's
        /// ancestry plus the merged lots' own ids — lineage is preserved,
        /// never dropped. Returns the number of lots merged away.
        /// </summary>
        public int MergeCompatibleLots(int dayIndex)
        {
            var groups = new Dictionary<string, List<HouseholdItemLot>>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < state.Lots.Count; i++)
            {
                HouseholdItemLot lot = state.Lots[i];
                if (lot == null || lot.HeldUnits > 0 || lot.QuantityUnits <= 0) continue;
                string key = (lot.ItemId ?? string.Empty) + "|" + (lot.QualityBand ?? string.Empty) + "|" +
                    (lot.SourceKind ?? string.Empty) + "|" + lot.OwnerPersonId;
                if (!groups.TryGetValue(key, out List<HouseholdItemLot> group))
                {
                    group = new List<HouseholdItemLot>();
                    groups[key] = group;
                }
                group.Add(lot);
            }

            int mergedAway = 0;
            foreach (KeyValuePair<string, List<HouseholdItemLot>> entry in groups)
            {
                List<HouseholdItemLot> group = entry.Value;
                if (group.Count < 2) continue;
                group.Sort((a, b) => a.AcquiredDayIndex != b.AcquiredDayIndex
                    ? a.AcquiredDayIndex.CompareTo(b.AcquiredDayIndex)
                    : a.LotId.CompareTo(b.LotId));
                HouseholdItemLot survivor = group[0];
                survivor.AncestryLotIds ??= new List<int>();
                for (int i = 1; i < group.Count; i++)
                {
                    HouseholdItemLot other = group[i];
                    survivor.QuantityUnits += other.QuantityUnits;
                    if (other.AncestryLotIds != null)
                    {
                        for (int a = 0; a < other.AncestryLotIds.Count; a++)
                        {
                            if (!survivor.AncestryLotIds.Contains(other.AncestryLotIds[a]))
                                survivor.AncestryLotIds.Add(other.AncestryLotIds[a]);
                        }
                    }
                    if (!survivor.AncestryLotIds.Contains(other.LotId))
                        survivor.AncestryLotIds.Add(other.LotId);
                    state.Lots.Remove(other);
                    mergedAway++;
                }
            }
            return mergedAway;
        }

        /// <summary>
        /// Consumes units of an item, oldest lots first. All-or-nothing: when
        /// available units are insufficient, nothing is consumed and a
        /// diagnostic is returned (callers route to hardship/shortage).
        /// </summary>
        public string Consume(
            string itemId,
            int quantityUnits,
            int dayIndex,
            string purpose,
            string consumerLabel,
            List<HouseholdConsumptionRecord> outRecords)
        {
            if (quantityUnits <= 0)
            {
                return Reject($"Rejected consumption of '{itemId}': quantity must be positive.");
            }

            if (string.IsNullOrWhiteSpace(purpose))
            {
                return Reject($"Rejected consumption of '{itemId}': purpose is required.");
            }

            int available = GetAvailableUnits(itemId);
            if (available < quantityUnits)
            {
                return Reject(
                    $"Insufficient {itemId}: {available} available, {quantityUnits} requested — nothing consumed.");
            }

            int remaining = quantityUnits;
            for (int i = 0; i < state.Lots.Count && remaining > 0; i++)
            {
                HouseholdItemLot lot = state.Lots[i];
                if (lot == null || !string.Equals(lot.ItemId, itemId, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                int take = Math.Min(remaining, lot.AvailableUnits);
                if (take <= 0)
                {
                    continue;
                }

                lot.QuantityUnits -= take;
                remaining -= take;
                outRecords?.Add(new HouseholdConsumptionRecord
                {
                    DayIndex = Mathf.Max(0, dayIndex),
                    HouseholdId = state.HouseholdId,
                    ItemId = lot.ItemId,
                    LotId = lot.LotId,
                    QuantityUnits = take,
                    Purpose = purpose,
                    ConsumerLabel = consumerLabel ?? string.Empty,
                });
            }

            RemoveEmptyLots();
            return null;
        }

        /// <summary>
        /// Reserves units for a sale or shipment commitment. Held units leave
        /// the consumable pool until released or fulfilled.
        /// </summary>
        public string HoldForCommitment(string itemId, int quantityUnits, string commitmentLabel)
        {
            if (quantityUnits <= 0)
            {
                return Reject($"Rejected hold of '{itemId}': quantity must be positive.");
            }

            if (string.IsNullOrWhiteSpace(commitmentLabel))
            {
                return Reject($"Rejected hold of '{itemId}': commitment label is required.");
            }

            int available = GetAvailableUnits(itemId);
            if (available < quantityUnits)
            {
                return Reject($"Cannot hold {quantityUnits} of {itemId}: only {available} available.");
            }

            int remaining = quantityUnits;
            for (int i = 0; i < state.Lots.Count && remaining > 0; i++)
            {
                HouseholdItemLot lot = state.Lots[i];
                if (lot == null || !string.Equals(lot.ItemId, itemId, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                int hold = Math.Min(remaining, lot.AvailableUnits);
                lot.HeldUnits += hold;
                remaining -= hold;
            }

            return null;
        }

        /// <summary>Releases a previous commitment hold back to the consumable pool.</summary>
        public void ReleaseHold(string itemId, int quantityUnits)
        {
            int remaining = Mathf.Max(0, quantityUnits);
            for (int i = 0; i < state.Lots.Count && remaining > 0; i++)
            {
                HouseholdItemLot lot = state.Lots[i];
                if (lot == null || !string.Equals(lot.ItemId, itemId, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                int release = Math.Min(remaining, Mathf.Max(0, lot.HeldUnits));
                lot.HeldUnits -= release;
                remaining -= release;
            }
        }

        /// <summary>
        /// Fulfills a commitment: held units leave the household permanently
        /// (shipped/sold). The caller moves the goods and the money through
        /// their own authorities; the lot simply no longer exists here.
        /// </summary>
        public string FulfillCommitment(string itemId, int quantityUnits, string commitmentLabel)
        {
            if (quantityUnits <= 0)
            {
                return Reject($"Rejected commitment fulfillment of '{itemId}': quantity must be positive.");
            }

            int held = 0;
            for (int i = 0; i < state.Lots.Count; i++)
            {
                HouseholdItemLot lot = state.Lots[i];
                if (lot != null && string.Equals(lot.ItemId, itemId, StringComparison.OrdinalIgnoreCase))
                {
                    held += Mathf.Max(0, lot.HeldUnits);
                }
            }

            if (held < quantityUnits)
            {
                return Reject($"Cannot fulfill {quantityUnits} of {itemId}: only {held} held for commitment.");
            }

            int remaining = quantityUnits;
            for (int i = 0; i < state.Lots.Count && remaining > 0; i++)
            {
                HouseholdItemLot lot = state.Lots[i];
                if (lot == null || !string.Equals(lot.ItemId, itemId, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                int take = Math.Min(remaining, Mathf.Max(0, lot.HeldUnits));
                lot.HeldUnits -= take;
                lot.QuantityUnits -= take;
                remaining -= take;
            }

            RemoveEmptyLots();
            return null;
        }

        public int GetAvailableUnits(string itemId)
        {
            int total = 0;
            for (int i = 0; i < state.Lots.Count; i++)
            {
                HouseholdItemLot lot = state.Lots[i];
                if (lot != null && string.Equals(lot.ItemId, itemId, StringComparison.OrdinalIgnoreCase))
                {
                    total += lot.AvailableUnits;
                }
            }

            return total;
        }

        public int GetTotalUnits(string itemId)
        {
            int total = 0;
            for (int i = 0; i < state.Lots.Count; i++)
            {
                HouseholdItemLot lot = state.Lots[i];
                if (lot != null && string.Equals(lot.ItemId, itemId, StringComparison.OrdinalIgnoreCase))
                {
                    total += Mathf.Max(0, lot.QuantityUnits);
                }
            }

            return total;
        }

        /// <summary>
        /// DERIVED report only: estimated days of supply at a daily use rate.
        /// Never a source of truth, never persisted as a balance.
        /// </summary>
        public float EstimateDaysOfSupply(string itemId, int dailyUseUnits)
        {
            if (dailyUseUnits <= 0)
            {
                return float.PositiveInfinity;
            }

            return GetAvailableUnits(itemId) / (float)dailyUseUnits;
        }

        private void RemoveEmptyLots()
        {
            for (int i = state.Lots.Count - 1; i >= 0; i--)
            {
                HouseholdItemLot lot = state.Lots[i];
                if (lot == null || lot.QuantityUnits <= 0)
                {
                    state.Lots.RemoveAt(i);
                }
            }
        }

        private string Reject(string diagnostic)
        {
            diagnostics.Add(diagnostic);
            return diagnostic;
        }
    }

    /// <summary>Phase B: registry of per-household inventories with save export/import.</summary>
    public sealed class HouseholdInventoryRegistry
    {
        private readonly Dictionary<int, HouseholdInventory> inventories = new Dictionary<int, HouseholdInventory>();
        private readonly List<string> diagnostics = new List<string>();

        public IReadOnlyList<string> Diagnostics => diagnostics;

        public HouseholdInventory GetOrCreate(int householdId)
        {
            if (!inventories.TryGetValue(householdId, out HouseholdInventory inventory))
            {
                inventory = new HouseholdInventory(householdId);
                inventories[householdId] = inventory;
            }

            return inventory;
        }

        public HouseholdInventory Get(int householdId)
        {
            return inventories.TryGetValue(householdId, out HouseholdInventory inventory) ? inventory : null;
        }

        public void ExportState(List<HouseholdInventoryState> outStates)
        {
            if (outStates == null)
            {
                return;
            }

            foreach (KeyValuePair<int, HouseholdInventory> entry in inventories)
            {
                outStates.Add(entry.Value.State);
            }
        }

        public void ImportState(IEnumerable<HouseholdInventoryState> inStates)
        {
            if (inStates == null)
            {
                return;
            }

            foreach (HouseholdInventoryState state in inStates)
            {
                if (state == null || state.HouseholdId < 0)
                {
                    continue;
                }

                if (inventories.ContainsKey(state.HouseholdId))
                {
                    diagnostics.Add($"ImportState: duplicate inventory for H{state.HouseholdId} skipped.");
                    continue;
                }

                state.Lots ??= new List<HouseholdItemLot>();
                inventories[state.HouseholdId] = new HouseholdInventory(state);
            }
        }
    }
}
