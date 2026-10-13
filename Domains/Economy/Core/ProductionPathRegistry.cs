using System;
using System.Collections.Generic;

namespace LandLedgers.Economy
{
    /// <summary>
    /// Pass V P1-4: the production path a business is routed through. APPEND-ONLY.
    ///
    /// Every business travels exactly ONE production path:
    ///   LegacyArchetype - the SharedBusinessRuntimeManager archetype runtimes
    ///     (and the GeneralStoreRuntimeManager for the general store). Production
    ///     runs through the archetype weekly operations; inventory truth is the
    ///     BusinessRuntimeState category stock; payroll runs through the monolith
    ///     weekly settlement (or the store's own payroll for GeneralStore).
    ///   GenericPath - the Pass IV generic production runtime
    ///     (GenericProductionRuntimeRegistry policies/processes). Inventory truth
    ///     is the GenericConfiguration FIFO positions with reservations; payroll
    ///     runs through the shared WageSettlementService.
    ///
    /// Before consolidation both paths could touch the same business (the
    /// generic retail sale authority consumed whichever stock had units; the
    /// daily generic-production drive advanced processes on legacy businesses).
    /// The registry is the single routing decision every seam consults.
    /// </summary>
    public enum ProductionPath
    {
        Unspecified = 0,
        LegacyArchetype = 1,
        GenericPath = 2,
    }

    /// <summary>
    /// Pass V P1-4: maps every business to its ONE production path and heals
    /// the duplicate inventory truth (BusinessRuntimeState category stock vs
    /// GenericConfiguration positions) by conserved consolidation: units move
    /// 1:1 from the non-authoritative source into the authoritative one with
    /// provenance, never created or destroyed.
    ///
    /// The type map (durable truth):
    ///   BusinessType.Generic      -> GenericPath. Classless businesses formed
    ///     through the generic BIZ-1 flow (player templates, NPC formation)
    ///     produce via generic policies and hold FIFO positions.
    ///   BusinessType.GeneralStore -> LegacyArchetype. The GeneralStoreRuntimeManager
    ///     owns production/stocking/payroll; its generic retail LINES are a
    ///     read-projection of the category stock (BindExistingMerchantStockToGenericRetail),
    ///     not a second stock truth.
    ///   Every other BusinessType  -> LegacyArchetype. The monolith's archetype
    ///     runtimes own production; WeeklySettlementBusinessTypes owns payroll.
    ///
    /// Per-instance overrides are session-scoped (tests, staged migration) and
    /// deliberately NOT persisted: the type map is the durable routing truth.
    /// </summary>
    public static class ProductionPathRegistry
    {
        private static readonly Dictionary<string, ProductionPath> instanceOverrides =
            new Dictionary<string, ProductionPath>(StringComparer.OrdinalIgnoreCase);

        /// <summary>Routes one business to its single production path.</summary>
        public static ProductionPath GetPath(BusinessInstanceState business)
        {
            if (business == null) return ProductionPath.Unspecified;
            if (!string.IsNullOrWhiteSpace(business.InstanceId)
                && instanceOverrides.TryGetValue(business.InstanceId, out ProductionPath overridePath)
                && overridePath != ProductionPath.Unspecified)
            {
                return overridePath;
            }

            return GetPathForType(business.BusinessType);
        }

        /// <summary>The durable type-level map. Append-only BusinessType values
        /// default to LegacyArchetype: a new specialized type must opt IN to the
        /// generic path explicitly (via SetInstancePath or an entry here).</summary>
        public static ProductionPath GetPathForType(BusinessType type)
        {
            return type == BusinessType.Generic ? ProductionPath.GenericPath : ProductionPath.LegacyArchetype;
        }

        /// <summary>Session-scoped per-instance override (tests, staged migration).</summary>
        public static void SetInstancePath(string instanceId, ProductionPath path)
        {
            if (string.IsNullOrWhiteSpace(instanceId)) return;
            if (path == ProductionPath.Unspecified) instanceOverrides.Remove(instanceId);
            else instanceOverrides[instanceId] = path;
        }

        public static void ClearInstancePath(string instanceId)
        {
            if (!string.IsNullOrWhiteSpace(instanceId)) instanceOverrides.Remove(instanceId);
        }

        /// <summary>Test isolation: drops all session overrides.</summary>
        public static void ClearAllInstanceOverrides()
        {
            instanceOverrides.Clear();
        }

        /// <summary>
        /// Pass V P1-4: retires the duplicate inventory truth for one business.
        /// Units on the NON-authoritative source move 1:1 into the authoritative
        /// source with consolidation provenance; the other source is zeroed.
        /// Conservation: total units before == total units after, per product.
        /// Returns the number of units moved (0 when already consolidated).
        /// </summary>
        public static int ConsolidateInventory(BusinessInstanceState business, int dayIndex, List<string> diagnostics)
        {
            if (business == null || business.RuntimeState == null) return 0;
            ProductionPath path = GetPath(business);
            if (path == ProductionPath.Unspecified) return 0;

            int moved = 0;
            if (path == ProductionPath.GenericPath)
            {
                moved += MoveCategoryStockIntoPositions(business, dayIndex, diagnostics);
            }
            else
            {
                moved += MovePositionsIntoCategoryStock(business, dayIndex, diagnostics);
            }

            return moved;
        }

        private static int MoveCategoryStockIntoPositions(BusinessInstanceState business, int dayIndex, List<string> diagnostics)
        {
            int moved = 0;
            IReadOnlyList<CategoryStockState> stocks = business.RuntimeState.CategoryStock;
            if (stocks == null) return 0;
            foreach (CategoryStockState stock in stocks)
            {
                if (stock == null || string.IsNullOrWhiteSpace(stock.CategoryId)) continue;
                int units = stock.CurrentStockUnits;
                if (units <= 0) continue;
                var definition = new GenericProductDefinition(stock.CategoryId, stock.CategoryId, ProductQuantityUnit.Each);
                if (!business.GenericConfiguration.TryAddInventory(
                    stock.CategoryId, units, 0, definition,
                    $"p1-4-consolidation:category-stock day {dayIndex}", dayIndex))
                {
                    diagnostics?.Add($"ProductionPathRegistry: could not consolidate {units}u '{stock.CategoryId}' " +
                        $"into generic positions for {business.InstanceId} — category stock left untouched.");
                    continue;
                }

                stock.SetCurrentStockUnits(0);
                moved += units;
                diagnostics?.Add($"ProductionPathRegistry: consolidated {units}u '{stock.CategoryId}' from category stock " +
                    $"into generic positions for {business.InstanceId} (generic path).");
            }

            return moved;
        }

        private static int MovePositionsIntoCategoryStock(BusinessInstanceState business, int dayIndex, List<string> diagnostics)
        {
            int moved = 0;
            IReadOnlyList<GenericInventoryPosition> positions = business.GenericConfiguration.Inventory;
            if (positions == null) return 0;
            foreach (GenericInventoryPosition position in new List<GenericInventoryPosition>(positions))
            {
                if (position == null || string.IsNullOrWhiteSpace(position.ProductId)) continue;
                int units = (int)Math.Floor(position.Quantity);
                if (units <= 0) continue;
                // Legacy category stock is unit counts without lot provenance by
                // design; the consolidation provenance is recorded in diagnostics
                // (units are conserved 1:1).
                int accepted = business.RuntimeState.AddCategoryStockUnits(position.ProductId, units);
                if (accepted <= 0)
                {
                    diagnostics?.Add($"ProductionPathRegistry: no category stock state for '{position.ProductId}' on " +
                        $"{business.InstanceId} — {units}u of generic positions left untouched (legacy path).");
                    continue;
                }

                float consumed;
                business.GenericConfiguration.TryConsumeInventory(position.ProductId, accepted, out consumed);
                moved += accepted;
                diagnostics?.Add($"ProductionPathRegistry: consolidated {accepted}u '{position.ProductId}' from generic positions " +
                    $"into category stock for {business.InstanceId} (legacy path).");
            }

            return moved;
        }
    }
}
