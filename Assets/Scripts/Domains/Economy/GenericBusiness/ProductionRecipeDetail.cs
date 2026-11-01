// Generic Business Reconstruction, section 7.
// Specific recipes / ProductionMethods, extending (not replacing) the
// established ProductionMethodDefinition system.
// This detail record is the plain-C# view of a method's recipe data.
// In Unity it is populated from the additive fields added to
// ProductionMethodDefinition in this pass (ByproductOutputs,
// FuelOrEnergyInputs, InputSubstitutions, RequiredFloorAreaSqM,
// WasteFraction01, FeasibilityConditions) - see
// GenericBusinessUnityAdapter for the field mapping. There is exactly
// one recipe authority: ProductionMethodDefinition. This record never
// becomes a second authority; it is how plain-C# code and tests read
// a method without pulling in UnityEngine.
// Durations here are game SECONDS (float), matching
// ProductionStageContracts (DurationGameSeconds). The legacy
// ProductionPhaseDefinition uses whole minutes; when Unity AI populates
// a legacy method from a recipe, minutes = ceil(seconds / 60), which
// is lossy for sub-minute phases and is flagged in the handoff - the
// seconds value in this record is the precise one.
using System;
using System.Collections.Generic;

namespace LandLedgers.Economy.GenericBusiness
{
    public sealed class ProductionRecipeInput
    {
        public string ProductId = string.Empty;
        public float Quantity;
    }

    public sealed class ProductionRecipeOutput
    {
        public string ProductId = string.Empty;
        public float Quantity;
    }

    /// <summary>
    /// One allowed substitution for one input: if the original product
    /// is unavailable, any one of the alternatives may be used, at the
    /// stated quantity, if the caller supports substitution for this
    /// method. Substitution is never assumed: a recipe with no entry
    /// for an input has no valid substitution for it.
    /// </summary>
    public sealed class ProductionInputSubstitution
    {
        public string OriginalProductId = string.Empty;
        public List<string> AlternativeProductIds = new List<string>();
        public float AlternativeQuantity;
    }

    public sealed class ProductionRecipeDetail
    {
        public string MethodId = string.Empty;
        public string DisplayName = string.Empty;
        public List<ProductionRecipeInput> Inputs = new List<ProductionRecipeInput>();
        public List<ProductionRecipeOutput> Outputs = new List<ProductionRecipeOutput>();
        public List<ProductionRecipeOutput> Byproducts = new List<ProductionRecipeOutput>();
        public List<ProductionRecipeInput> FuelOrEnergyInputs = new List<ProductionRecipeInput>();
        public List<ProductionInputSubstitution> Substitutions = new List<ProductionInputSubstitution>();
        public List<string> RequiredEquipmentIds = new List<string>();
        public List<string> RequiredWorkspaceIds = new List<string>();
        public float RequiredFloorAreaSqM;
        public float WasteFraction01;
        public string PerformanceSkillId = string.Empty;
        public List<string> FeasibilityConditions = new List<string>();
        public List<ProductionRecipePhase> Phases = new List<ProductionRecipePhase>();

        public float TotalOutputQuantity()
        {
            float total = 0f;
            foreach (ProductionRecipeOutput o in Outputs) total += o != null ? o.Quantity : 0f;
            return total;
        }

        /// <summary>
        /// Validate the recipe itself (not the business). A recipe with
        /// no output, a non-positive input, or a waste fraction outside
        /// 0..1 is malformed and must be refused at authoring time.
        /// </summary>
        public List<string> Validate()
        {
            var problems = new List<string>();
            if (string.IsNullOrWhiteSpace(MethodId)) problems.Add("Recipe needs a method id.");
            if (Outputs == null || Outputs.Count == 0) problems.Add("Recipe needs at least one output.");
            foreach (ProductionRecipeInput i in Inputs)
            {
                if (i == null || string.IsNullOrWhiteSpace(i.ProductId) || i.Quantity <= 0f)
                    problems.Add("Recipe has a malformed input requirement.");
            }
            foreach (ProductionRecipeOutput o in Outputs)
            {
                if (o == null || string.IsNullOrWhiteSpace(o.ProductId) || o.Quantity <= 0f)
                    problems.Add("Recipe has a malformed output.");
            }
            if (WasteFraction01 < 0f || WasteFraction01 > 1f) problems.Add("Waste fraction must be between 0 and 1.");
            if (RequiredFloorAreaSqM < 0f) problems.Add("Required floor area cannot be negative.");
            return problems;
        }
    }

    public sealed class ProductionRecipePhase
    {
        public string PhaseId = string.Empty;
        public float DurationGameSeconds;
        public bool RequiresPerson;
        public List<string> ReservedEquipmentIds = new List<string>();
        public List<string> ReservedWorkspaceIds = new List<string>();
    }

    /// <summary>
    /// Bakery recipe presets. Quantities are taken from the existing
    /// BakeryBreadCatalog product specs in the repo (bread: 12 lb flour
    /// per batch, 24 loaves; rolls: 6 lb, 48 rolls; pie: 4 lb, 12 pies;
    /// notions per batch for every product). Those specs are
    /// calibration (Canon Part XV in the catalog's own comments), NOT
    /// canon, and are repeated here only so the three products share
    /// one recipe system instead of a single hard-coded bread product.
    /// A bakery enables whichever of these methods its owner chooses;
    /// enabling a method does not create its inputs.
    /// </summary>
    public static class BakeryRecipePresets
    {
        private static ProductionRecipeDetail Batch(
            string methodId, string displayName, string outputId, float outputQty,
            float flourLb, float notions)
        {
            return new ProductionRecipeDetail
            {
                MethodId = methodId,
                DisplayName = displayName,
                Inputs = new List<ProductionRecipeInput>
                {
                    new ProductionRecipeInput { ProductId = "flour", Quantity = flourLb },
                    new ProductionRecipeInput { ProductId = "notions", Quantity = notions },
                },
                Outputs = new List<ProductionRecipeOutput>
                {
                    new ProductionRecipeOutput { ProductId = outputId, Quantity = outputQty },
                },
                FuelOrEnergyInputs = new List<ProductionRecipeInput>
                {
                    new ProductionRecipeInput { ProductId = "fuel", Quantity = 1f },
                },
                RequiredEquipmentIds = new List<string> { "oven", "baker-hand-kit" },
                RequiredWorkspaceIds = new List<string> { "preparation", "oven-space" },
                PerformanceSkillId = "baking",
                FeasibilityConditions = new List<string> { "oven workstation present", "flour and notions on hand" },
                Phases = new List<ProductionRecipePhase>
                {
                    new ProductionRecipePhase { PhaseId = "prepare", DurationGameSeconds = 3600f, RequiresPerson = true },
                    new ProductionRecipePhase { PhaseId = "proof", DurationGameSeconds = 7200f, RequiresPerson = false },
                    new ProductionRecipePhase { PhaseId = "bake", DurationGameSeconds = 3600f, RequiresPerson = false },
                    new ProductionRecipePhase { PhaseId = "unload-cool", DurationGameSeconds = 1800f, RequiresPerson = true },
                },
            };
        }

        public static ProductionRecipeDetail BreadBatch()
        {
            return Batch("bakery.bread-batch", "Bread batch", "bread", 24f, 12f, 1f);
        }

        public static ProductionRecipeDetail RollsBatch()
        {
            return Batch("bakery.rolls-batch", "Rolls batch", "rolls", 48f, 6f, 1f);
        }

        public static ProductionRecipeDetail PieBatch()
        {
            return Batch("bakery.pie-batch", "Pie batch", "pie", 12f, 4f, 1f);
        }

        public static IReadOnlyList<ProductionRecipeDetail> All()
        {
            return new[] { BreadBatch(), RollsBatch(), PieBatch() };
        }
    }
}
