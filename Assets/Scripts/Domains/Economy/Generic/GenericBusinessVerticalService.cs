using System.Collections.Generic;

namespace LandLedgers.Economy
{
    /// <summary>
    /// Small setup service for authored verticals. It installs policies and
    /// offers only; it never creates equipment, premises, stock, workers, or
    /// operating state. Those remain ordinary acquisition and runtime paths.
    /// </summary>
    public static class GenericBusinessVerticalService
    {
        public static void ConfigureBakery(BusinessInstanceState business)
        {
            if (business == null) return;
            ProductionMethodDefinition bread = GenericVerticalCatalog.Bread();
            business.RegisterGenericProductionMethod(bread);
            business.RegisterGenericProductionPolicy(new GenericProductionPolicy
            {
                MethodId = bread.MethodId,
                Mode = GenericProductionPolicyMode.MakeToStock,
                TargetStock = 12f,
                MaximumStock = 24f,
                BatchSize = 1f,
                Priority = 20,
            });
            business.TryConfigureGenericRetailLine(new GenericRetailProductLine("bread", "display", 12f, 24f, 6f, 15), 12f);
        }

        public static void ConfigurePrairieForkSmithy(BusinessInstanceState business)
        {
            if (business == null) return;
            foreach (ProductionMethodDefinition method in GenericVerticalCatalog.PrairieForkSmithing())
            {
                business.RegisterGenericProductionMethod(method);
                if (method.OutputProductId == "")
                {
                    business.RegisterGenericProductionPolicy(GenericVerticalCatalog.RepairPolicy());
                    continue;
                }
                (float target, float maximum) = GetPrairieForkPolicy(method.OutputProductId);
                business.RegisterGenericProductionPolicy(GenericVerticalCatalog.StockPolicy(method.MethodId, target, maximum));
                business.TryConfigureGenericRetailLine(
                    new GenericRetailProductLine(method.OutputProductId, "storage", target, maximum, target / 2f, 100),
                    maximum);
            }
        }

        public static bool AddMerchantProductLine(BusinessInstanceState business, string productId,
            float target, float maximum, float reorderPoint, int priceCents, string poolId = "storage")
        {
            return business != null && business.TryConfigureGenericRetailLine(
                new GenericRetailProductLine(productId, poolId, target, maximum, reorderPoint, priceCents),
                maximum);
        }

        private static (float target, float maximum) GetPrairieForkPolicy(string productId)
        {
            switch (productId)
            {
                case "8d-cut-nails": return (75f, 100f);
                case "hinges": return (12f, 20f);
                case "straps": return (10f, 20f);
                case "brackets": return (12f, 20f);
                case "hooks": return (8f, 16f);
                case "simple-latches": return (6f, 12f);
                case "bolts": return (20f, 40f);
                case "pins": return (20f, 40f);
                default: return (0f, 0f);
            }
        }
    }
}
