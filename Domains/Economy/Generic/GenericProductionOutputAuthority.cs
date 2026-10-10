using System;

namespace LandLedgers.Economy
{
    /// <summary>
    /// Final production write for fungible output. It is deliberately small:
    /// process idempotence lives in GenericProductionAuthority, while inventory
    /// remains the Business configuration's single generic stock authority.
    /// </summary>
    public static class GenericProductionOutputAuthority
    {
        public static bool TryCreateInventoryOutputOnce(
            ProductionProcessState process,
            ProductionMethodDefinition method,
            GenericBusinessConfiguration configuration,
            GenericProductDefinition definition,
            int unitCostCents,
            string provenance,
            int dayIndex,
            out string reason)
        {
            reason = string.Empty;
            if (configuration == null || method == null || string.IsNullOrWhiteSpace(method.OutputProductId)
                || method.OutputQuantity <= 0f)
            {
                reason = "Production output requires a configured fungible Product.";
                return false;
            }
            return GenericProductionAuthority.TryCreateOutputOnce(process, () =>
            {
                return configuration.TryAddInventory(method.OutputProductId, method.OutputQuantity,
                    unitCostCents, definition, provenance, dayIndex);
            });
        }
    }
}
