using System;
using System.Collections.Generic;
using LandLedgers.Economy;
using LandLedgers.Population;

namespace LandLedgers.Orchestration.Systems
{
    /// <summary>
    /// Production construction-material adapter for a commissioning household.
    /// It deliberately exposes only goods that are actually held in the
    /// authoritative HouseholdInventory; it never manufactures a yard lot or
    /// a construction allowance. Yard/business sources can be supplied by the
    /// corresponding business runtime when a project is commissioned there.
    /// </summary>
    public sealed class HouseholdConstructionMaterialSource : IConstructionMaterialSource
    {
        private readonly HouseholdInventory inventory;

        public HouseholdConstructionMaterialSource(HouseholdInventory inventory)
        {
            this.inventory = inventory;
        }

        public int AvailableUnits(ConstructionMaterialRequirement requirement)
        {
            string itemId = ResolveItemId(requirement);
            return inventory == null || itemId == null ? 0 : inventory.GetAvailableUnits(itemId);
        }

        public string TryConsume(
            ConstructionMaterialRequirement requirement,
            int units,
            string projectLabel,
            int dayIndex,
            List<string> diagnostics,
            out string provenanceLabel)
        {
            provenanceLabel = string.Empty;
            string itemId = ResolveItemId(requirement);
            if (inventory == null)
            {
                return "household construction material source has no authoritative inventory.";
            }

            if (itemId == null)
            {
                return $"household inventory has no catalog item for '{requirement?.MaterialDisplayName}'.";
            }

            if (units <= 0)
            {
                return "construction material quantity must be positive.";
            }

            if (inventory.GetAvailableUnits(itemId) < units)
            {
                return $"household holds only {inventory.GetAvailableUnits(itemId)} available '{itemId}' units.";
            }

            var sourceLabels = new List<string>();
            int remainingForProvenance = units;
            foreach (HouseholdItemLot lot in inventory.Lots)
            {
                if (lot == null || !string.Equals(lot.ItemId, itemId, StringComparison.OrdinalIgnoreCase)) continue;
                int take = Math.Min(remainingForProvenance, lot.AvailableUnits);
                if (take <= 0) continue;
                if (!string.IsNullOrWhiteSpace(lot.SourceLabel) && !sourceLabels.Contains(lot.SourceLabel))
                    sourceLabels.Add(lot.SourceLabel);
                remainingForProvenance -= take;
                if (remainingForProvenance == 0) break;
            }

            var records = new List<HouseholdConsumptionRecord>();
            string refusal = inventory.Consume(
                itemId,
                units,
                dayIndex,
                string.IsNullOrWhiteSpace(projectLabel) ? "construction materials" : projectLabel,
                "construction",
                records);
            if (refusal != null)
            {
                return refusal;
            }

            provenanceLabel = sourceLabels.Count == 0
                ? $"household lot(s): {itemId} x{units}"
                : string.Join("; ", sourceLabels);
            diagnostics?.Add($"HouseholdConstructionMaterialSource: H{inventory.HouseholdId} consumed {units} {itemId} for {projectLabel ?? "construction"}.");
            return null;
        }

        private static string ResolveItemId(ConstructionMaterialRequirement requirement)
        {
            if (requirement == null) return null;
            string raw = requirement.MaterialDisplayName;
            if (string.IsNullOrWhiteSpace(raw)) return null;

            foreach (HouseholdItemDefinition definition in HouseholdItemCatalog.All)
            {
                if (string.Equals(raw, definition.ItemId, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(raw, definition.DisplayName, StringComparison.OrdinalIgnoreCase))
                {
                    return definition.ItemId;
                }
            }

            return null;
        }
    }

    /// <summary>
    /// Production tool-custody adapter over the same household inventory used
    /// by consumption and shopping. Tools remain ordinary held goods here;
    /// significant durable equipment can be supplied by an Asset-backed
    /// custody adapter without changing this contract.
    /// </summary>
    public sealed class HouseholdInventoryToolCustody : IHouseholdToolCustody
    {
        private readonly HouseholdInventoryRegistry inventories;

        public HouseholdInventoryToolCustody(HouseholdInventoryRegistry inventories)
        {
            this.inventories = inventories;
        }

        public bool HouseholdHoldsTool(int householdId, string toolItemId)
        {
            if (inventories == null || string.IsNullOrWhiteSpace(toolItemId)) return false;
            HouseholdInventory inventory = inventories.Get(householdId);
            return inventory != null && inventory.GetAvailableUnits(toolItemId) > 0;
        }
    }
}
