using System.Collections.Generic;

namespace LandLedgers.Economy
{
    /// <summary>
    /// Representative authored methods used by generic Businesses. These are
    /// data presets, not permissions: a Business must still own the referenced
    /// equipment/workspace and pass the physical evaluator at execution time.
    /// </summary>
    public static class GenericVerticalCatalog
    {
        public static ProductionMethodDefinition Bread()
        {
            return new ProductionMethodDefinition
            {
                MethodId = "generic-bread",
                OutputProductId = "bread",
                OutputQuantity = 1f,
                InputRequirements = new List<ProductionInputRequirement>
                {
                    new ProductionInputRequirement { ProductId = "flour", Quantity = 1f },
                    new ProductionInputRequirement { ProductId = "fuel", Quantity = 1f },
                },
                RequiredEquipmentIds = new List<string> { "oven" },
                RequiredWorkspaceIds = new List<string> { "preparation", "oven-space" },
                PerformanceSkillId = "baking",
                Phases = new List<ProductionPhaseDefinition>
                {
                    new ProductionPhaseDefinition { PhaseId = "load", ElapsedMinutes = 1, RequiresPerson = true, ReservedEquipmentIds = new List<string> { "oven" }, ReservedWorkspaceIds = new List<string> { "preparation", "oven-space" } },
                    new ProductionPhaseDefinition { PhaseId = "bake", ElapsedMinutes = 28, RequiresPerson = false, ReservedEquipmentIds = new List<string> { "oven" }, ReservedWorkspaceIds = new List<string> { "oven-space" } },
                    new ProductionPhaseDefinition { PhaseId = "remove-check", ElapsedMinutes = 1, RequiresPerson = true, ReservedEquipmentIds = new List<string> { "oven" }, ReservedWorkspaceIds = new List<string> { "oven-space" } },
                },
            };
        }

        public static IReadOnlyList<ProductionMethodDefinition> PrairieForkSmithing()
        {
            string[] products = { "8d-cut-nails", "hinges", "straps", "brackets", "hooks", "simple-latches", "bolts", "pins" };
            var methods = new List<ProductionMethodDefinition>();
            foreach (string product in products)
            {
                methods.Add(new ProductionMethodDefinition
                {
                    MethodId = "generic-smithing-" + product,
                    OutputProductId = product,
                    OutputQuantity = 1f,
                    InputRequirements = new List<ProductionInputRequirement>
                    {
                        new ProductionInputRequirement { ProductId = "iron", Quantity = 1f },
                        new ProductionInputRequirement { ProductId = "fuel", Quantity = 1f },
                    },
                    RequiredEquipmentIds = new List<string> { "forge", "anvil", "smith-tools" },
                    RequiredWorkspaceIds = new List<string> { "smithy" },
                    PerformanceSkillId = "smithing",
                    Phases = new List<ProductionPhaseDefinition>
                    {
                        new ProductionPhaseDefinition { PhaseId = "forge-and-form", ElapsedMinutes = 10, RequiresPerson = true, ReservedEquipmentIds = new List<string> { "forge", "anvil", "smith-tools" }, ReservedWorkspaceIds = new List<string> { "smithy" } },
                    },
                });
            }
            methods.Add(new ProductionMethodDefinition
            {
                MethodId = "generic-repair-ironwork",
                OutputProductId = string.Empty,
                OutputQuantity = 0f,
                InputRequirements = new List<ProductionInputRequirement> { new ProductionInputRequirement { ProductId = "iron", Quantity = 1f } },
                RequiredEquipmentIds = new List<string> { "forge", "anvil", "smith-tools" },
                RequiredWorkspaceIds = new List<string> { "smithy" },
                PerformanceSkillId = "smithing",
                Phases = new List<ProductionPhaseDefinition> { new ProductionPhaseDefinition { PhaseId = "repair", ElapsedMinutes = 20, RequiresPerson = true, ReservedEquipmentIds = new List<string> { "forge", "anvil", "smith-tools" }, ReservedWorkspaceIds = new List<string> { "smithy" } }, },
            });
            return methods;
        }

        public static GenericProductionPolicy StockPolicy(string methodId, float target, float maximum, int priority = 0)
        {
            return new GenericProductionPolicy
            {
                MethodId = methodId,
                Mode = GenericProductionPolicyMode.MakeToStock,
                TargetStock = target,
                MaximumStock = maximum,
                BatchSize = 1f,
                Priority = priority,
                Enabled = true,
            };
        }

        public static GenericProductionPolicy RepairPolicy()
        {
            return new GenericProductionPolicy
            {
                MethodId = "generic-repair-ironwork",
                Mode = GenericProductionPolicyMode.MakeToOrder,
                Priority = 100,
                CustomerOrdersOverrideStock = true,
                Enabled = true,
            };
        }
    }
}
