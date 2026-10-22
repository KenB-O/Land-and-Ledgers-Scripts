using System;
using LandLedgers.Primitives;

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
            out string reason,
            SharedCategoryStockLedger provenanceLedger = null,
            EntityIdRegistry provenanceIds = null)
        {
            reason = string.Empty;
            if (configuration == null || method == null || string.IsNullOrWhiteSpace(method.OutputProductId)
                || method.OutputQuantity <= 0f)
            {
                reason = "Production output requires a configured fungible Product.";
                return false;
            }
            bool created = GenericProductionAuthority.TryCreateOutputOnce(process, () =>
            {
                return configuration.TryAddInventory(method.OutputProductId, method.OutputQuantity,
                    unitCostCents, definition, provenance, dayIndex);
            });
            if (created && provenanceLedger != null)
            {
                // Pass IX WS6 (IDENT-001): the output seam is a provenance
                // boundary — record the output lot with the real input lots
                // the process consumed at start.
                provenanceLedger.RecordProductionOutput(
                    method.OutputProductId, method.OutputQuantity,
                    process != null ? process.ProcessId : string.Empty,
                    string.Empty,
                    process != null ? process.ConsumedInputLotIds : null,
                    dayIndex, provenanceIds);
            }
            return created;
        }
    }
}
