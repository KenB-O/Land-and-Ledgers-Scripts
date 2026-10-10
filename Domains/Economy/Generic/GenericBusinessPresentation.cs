using System.Text;
using System.Linq;

namespace LandLedgers.Economy
{
    /// <summary>Compact management readout for a classless Business.</summary>
    public static class GenericBusinessPresentation
    {
        public static string BuildReadout(BusinessInstanceState business)
        {
            if (business == null) return "<b>Generic Business</b>\nBusiness unavailable.";
            GenericBusinessConfiguration config = business.GenericConfiguration;
            var builder = new StringBuilder();
            builder.AppendLine("<b>Generic Business Operations</b>");
            builder.AppendLine($"Methods {config.ProductionMethods.Count} | Policies {config.ProductionPolicies.Count} | Processes {config.ActiveProcesses.Count}");
            builder.AppendLine($"Equipment {config.EquipmentAssetIds.Count} | Workspaces {config.WorkspaceIds.Count}");
            builder.AppendLine("Inventory");
            foreach (GenericInventoryPosition position in config.Inventory)
            {
                if (position == null) continue;
                builder.AppendLine($"  {position.ProductId}: {position.Quantity:0.##} ({position.AvailableQuantity:0.##} available)");
            }
            builder.AppendLine("Retail lines");
            foreach (GenericRetailProductLine line in config.Retail.ProductLines)
            {
                if (line == null) continue;
                RetailCapacityPool pool = config.Retail.CapacityPools.FirstOrDefault(candidate => candidate != null
                    && string.Equals(candidate.PoolId, line.CapacityPoolId, System.StringComparison.OrdinalIgnoreCase));
                builder.AppendLine($"  {line.ProductId}: {(line.EnabledForSale ? "enabled" : "disabled")} | {line.CapacityPoolId} {(pool != null ? pool.Occupied : 0f):0.##}/{(pool != null ? pool.Allocated : 0f):0.##}");
            }
            return builder.ToString().TrimEnd();
        }
    }
}
