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

        /// <summary>
        /// F1 (expanded campaign 2026-11-16): the furniture methods, derived
        /// from FurnitureRecipePresets — the recipes stay the one authority,
        /// these methods are their runtime projection. A business must still
        /// own the referenced equipment/workspace and pass the physical
        /// evaluator at execution time. Which business runs these methods is
        /// NOT decided here (O-F3, open); this catalog entry is the seam the
        /// future carpenter binding (or a diversifying business) calls.
        /// One run of the chair method makes 4 chairs (batch of 4 per one
        /// nails_hardware_lot); every other method makes 1 unit per run.
        /// </summary>
        public static IReadOnlyList<ProductionMethodDefinition> Furniture()
        {
            var methods = new List<ProductionMethodDefinition>();
            foreach (LandLedgers.Economy.GenericBusiness.ProductionRecipeDetail recipe
                in LandLedgers.Economy.GenericBusiness.FurnitureRecipePresets.All())
            {
                methods.Add(recipe.ToMethodDefinition());
            }
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

        /// <summary>
        /// F1 part 2 (O-F3): the generic product definition for lumber as
        /// the carpentry workshop holds it. Lot identity is required so
        /// the lumber a piece of furniture is made from keeps its lot
        /// provenance into the finished EquipmentAsset (the production
        /// executor captures the consumed FIFO lot ids). One unit is one
        /// board as the furniture recipes count it.
        /// </summary>
        public static GenericProductDefinition LumberDefinition() =>
            new GenericProductDefinition("lumber", "Lumber", ProductQuantityUnit.BoardFoot,
                requiresLotIdentity: true);
    }
}
