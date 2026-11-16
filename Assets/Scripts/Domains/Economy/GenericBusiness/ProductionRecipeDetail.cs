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

        /// <summary>
        /// Derives the runtime method from this recipe. The recipe stays the
        /// one authority: the method is a projection of it, never a second
        /// source of recipe data. Phase durations are game seconds here and
        /// whole minutes on the method (legacy ProductionPhaseDefinition);
        /// minutes = ceil(seconds / 60), so a recipe authored in whole
        /// minutes converts exactly and a sub-minute phase rounds up to one
        /// minute, never down to zero. Person-required phases reserve the
        /// recipe's equipment and workspaces; passive phases reserve the
        /// workspaces only (the piece cures in the shop; the tools are free).
        /// Byproducts and fuel/energy inputs are carried as data — the
        /// runtime consumes exactly the listed inputs and creates output only
        /// through the caller's callback, which also creates the byproducts.
        /// </summary>
        public ProductionMethodDefinition ToMethodDefinition()
        {
            var method = new ProductionMethodDefinition
            {
                MethodId = MethodId,
                RequiredFloorAreaSqM = RequiredFloorAreaSqM,
                WasteFraction01 = WasteFraction01,
                PerformanceSkillId = PerformanceSkillId,
            };
            if (Outputs != null && Outputs.Count > 0 && Outputs[0] != null)
            {
                method.OutputProductId = Outputs[0].ProductId;
                method.OutputQuantity = Outputs[0].Quantity;
            }
            if (Inputs != null)
            {
                foreach (ProductionRecipeInput input in Inputs)
                {
                    if (input == null) continue;
                    method.InputRequirements.Add(new ProductionInputRequirement
                    {
                        ProductId = input.ProductId,
                        Quantity = input.Quantity,
                    });
                }
            }
            if (Byproducts != null)
            {
                foreach (ProductionRecipeOutput byproduct in Byproducts)
                {
                    if (byproduct == null) continue;
                    method.ByproductOutputs.Add(new ProductionInputRequirement
                    {
                        ProductId = byproduct.ProductId,
                        Quantity = byproduct.Quantity,
                    });
                }
            }
            if (FuelOrEnergyInputs != null)
            {
                foreach (ProductionRecipeInput fuel in FuelOrEnergyInputs)
                {
                    if (fuel == null) continue;
                    method.FuelOrEnergyInputs.Add(new ProductionInputRequirement
                    {
                        ProductId = fuel.ProductId,
                        Quantity = fuel.Quantity,
                    });
                }
            }
            if (Substitutions != null)
            {
                foreach (ProductionInputSubstitution substitution in Substitutions)
                {
                    if (substitution == null) continue;
                    method.InputSubstitutions.Add(new ProductionInputSubstitutionDefinition
                    {
                        OriginalProductId = substitution.OriginalProductId,
                        AlternativeProductIds = new List<string>(substitution.AlternativeProductIds ?? new List<string>()),
                        AlternativeQuantity = substitution.AlternativeQuantity,
                    });
                }
            }
            if (RequiredEquipmentIds != null) method.RequiredEquipmentIds.AddRange(RequiredEquipmentIds);
            if (RequiredWorkspaceIds != null) method.RequiredWorkspaceIds.AddRange(RequiredWorkspaceIds);
            if (FeasibilityConditions != null) method.FeasibilityConditions.AddRange(FeasibilityConditions);
            if (Phases != null)
            {
                foreach (ProductionRecipePhase phase in Phases)
                {
                    if (phase == null) continue;
                    var methodPhase = new ProductionPhaseDefinition
                    {
                        PhaseId = phase.PhaseId,
                        ElapsedMinutes = Math.Max(1, (int)Math.Ceiling(phase.DurationGameSeconds / 60f)),
                        RequiresPerson = phase.RequiresPerson,
                    };
                    if (phase.RequiresPerson)
                    {
                        if (RequiredEquipmentIds != null) methodPhase.ReservedEquipmentIds.AddRange(RequiredEquipmentIds);
                        if (RequiredWorkspaceIds != null) methodPhase.ReservedWorkspaceIds.AddRange(RequiredWorkspaceIds);
                    }
                    else
                    {
                        if (RequiredWorkspaceIds != null) methodPhase.ReservedWorkspaceIds.AddRange(RequiredWorkspaceIds);
                    }
                    method.Phases.Add(methodPhase);
                }
            }
            return method;
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

    /// <summary>
    /// F1 (expanded campaign 2026-11-16): furniture recipe presets, plan
    /// section 4.2. First wave is CARPENTRY ONLY (wood + fasteners +
    /// cloth). Stove (cast iron) and barrel (cooperage) are deliberately out.
    ///
    /// Product ids: furniture.* per FurnitureItemSpecs (Domains/Economy/
    /// Storage/FurnitureItemSpecs.cs, the F0 authored table); "lumber" is
    /// one milled board (ImportCatalog.LumberId; RepairQueueEconomy.
    /// LumberMaterialId); "nails_hardware_lot" and "basic_cloth" are
    /// household-catalog ids (cloth by the yard); "hay" is the CropChain
    /// hay lot (ProductKind "hay", integer units at 40 units/acre);
    /// "fuel-wood" is the sawmill slab/offcut fuel-wood product.
    ///
    /// Quantities are the plan's proposed numbers, adopted under the
    /// standing delegation doctrine (O-F1: proceed on the proposed numbers;
    /// Kennedy corrects what he disagrees with). Labour is person-minutes
    /// through TaskAuthority; passive phases (finish curing) advance time
    /// without holding the person, per the bakery proof/drying pattern.
    /// Whole boards only as inputs: a piece that needs less than a board
    /// consumes 1 board and yields offcut fuel-wood as a byproduct.
    ///
    /// Two open pins from plan section 4.2, resolved here:
    /// - Hay stuffing: hay lots are real integer-unit lots (CropChain.
    ///   StackHay, 40 units/acre). At a ~1 ton/acre prairie-hay yield one
    ///   unit is ~25 kg; a straw mattress takes ~15 kg, so ONE whole hay
    ///   unit stuffs one mattress (bed, bedroll). Whole units only — no
    ///   fractional lot is ever consumed, and no free input is substituted.
    /// - Nails per chair: the lot authority has no fractional-lot path, so
    ///   the chair recipe is a BATCH of 4 chairs: 4 lumber + 1
    ///   nails_hardware_lot -> 4 chairs + 4 fuel-wood offcuts, 480
    ///   person-minutes (4 x the plan's 120). The plan's cost build is
    ///   preserved exactly (nails share 3c per chair). One run = 4 chairs;
    ///   callers planning in output units must divide by 4.
    ///
    /// Cure (passive, 120 min): an authored finish-curing window for every
    /// recipe with a finish phase (bed, table, chairs, cabinets). Shelf,
    /// crate and bedroll have no finish phase and no cure phase. The cure
    /// duration is a calibration assumption for the owner to correct.
    ///
    /// Substitutions are advisory only: the runtime consumes exactly the
    /// listed inputs unless the caller implements substitution. Flannel may
    /// stand in for basic_cloth ticking (same yardage). There is no oak
    /// substitution to record: the sawmill hardwood profile yields the same
    /// "lumber" product id, so there is no distinct oak-lumber product.
    ///
    /// The producing business is NOT decided here (O-F3, open): these
    /// presets name the recipes, the skill and the workstation. Which
    /// business runs them, and where the carpenter works, is the owner's
    /// decision. Tests drive the recipes through GenericProductionRuntime
    /// with a test business fixture only.
    /// </summary>
    public static class FurnitureRecipePresets
    {
        public const string CarpentrySkillId = "carpentry";
        public const string CarpenterWorkstationId = "carpenter-workshop";

        private static readonly string[] CarpenterEquipment =
            { "workbench", "carpenter-hand-tool-kit" };
        private static readonly string[] CarpenterWorkspaces =
            { CarpenterWorkstationId };

        private static ProductionRecipeDetail Recipe(
            string methodId, string displayName,
            List<ProductionRecipeInput> inputs,
            string outputId, float outputQty,
            List<ProductionRecipeOutput> byproducts,
            List<ProductionRecipePhase> phases,
            List<string> feasibilityConditions)
        {
            var recipe = new ProductionRecipeDetail
            {
                MethodId = methodId,
                DisplayName = displayName,
                Inputs = inputs,
                Outputs = new List<ProductionRecipeOutput>
                {
                    new ProductionRecipeOutput { ProductId = outputId, Quantity = outputQty },
                },
                Byproducts = byproducts ?? new List<ProductionRecipeOutput>(),
                RequiredEquipmentIds = new List<string>(CarpenterEquipment),
                RequiredWorkspaceIds = new List<string>(CarpenterWorkspaces),
                PerformanceSkillId = CarpentrySkillId,
                FeasibilityConditions = feasibilityConditions,
                Phases = phases,
            };
            return recipe;
        }

        private static ProductionRecipePhase PersonPhase(string phaseId, float minutes) =>
            new ProductionRecipePhase
            {
                PhaseId = phaseId,
                DurationGameSeconds = minutes * 60f,
                RequiresPerson = true,
            };

        private static ProductionRecipePhase CurePhase(float minutes = 120f) =>
            new ProductionRecipePhase
            {
                PhaseId = "cure",
                DurationGameSeconds = minutes * 60f,
                RequiresPerson = false,
            };

        private static ProductionRecipeInput Input(string productId, float quantity) =>
            new ProductionRecipeInput { ProductId = productId, Quantity = quantity };

        private static ProductionRecipeOutput Byproduct(string productId, float quantity) =>
            new ProductionRecipeOutput { ProductId = productId, Quantity = quantity };

        private static ProductionInputSubstitution ClothSubstitution(float yards) =>
            new ProductionInputSubstitution
            {
                OriginalProductId = "basic_cloth",
                AlternativeProductIds = new List<string> { "flannel" },
                AlternativeQuantity = yards,
            };

        /// <summary>
        /// Bed (frame + straw mattress, assembled): 2 lumber, 1
        /// nails_hardware_lot, 4 yd basic_cloth (ticking), 1 hay (mattress
        /// stuffing). Labour cut 60, join/assemble 180, finish 60 = 300
        /// person-minutes + 120 passive cure. Bedding is NOT an input (no
        /// legitimate supply exists); it is a separate retail product.
        /// </summary>
        public static ProductionRecipeDetail Bed()
        {
            var recipe = Recipe(
                "furniture.bed", "Bed",
                new List<ProductionRecipeInput>
                {
                    Input("lumber", 2f),
                    Input("nails_hardware_lot", 1f),
                    Input("basic_cloth", 4f),
                    Input("hay", 1f),
                },
                "furniture.bed", 1f,
                null,
                new List<ProductionRecipePhase>
                {
                    PersonPhase("cut", 60f),
                    PersonPhase("join-assemble", 180f),
                    PersonPhase("finish", 60f),
                    CurePhase(),
                },
                new List<string> { "carpenter workstation present", "lumber, fasteners, ticking cloth and hay on hand" });
            recipe.Substitutions.Add(ClothSubstitution(4f));
            return recipe;
        }

        /// <summary>
        /// Table (dining/work): 2 lumber, 1 nails_hardware_lot. Labour cut
        /// 45, assemble 120, finish 45 = 210 person-minutes + 120 passive cure.
        /// </summary>
        public static ProductionRecipeDetail Table()
        {
            return Recipe(
                "furniture.table", "Table",
                new List<ProductionRecipeInput>
                {
                    Input("lumber", 2f),
                    Input("nails_hardware_lot", 1f),
                },
                "furniture.table", 1f,
                null,
                new List<ProductionRecipePhase>
                {
                    PersonPhase("cut", 45f),
                    PersonPhase("assemble", 120f),
                    PersonPhase("finish", 45f),
                    CurePhase(),
                },
                new List<string> { "carpenter workstation present", "lumber and fasteners on hand" });
        }

        /// <summary>
        /// Chair BATCH of 4: 4 lumber, 1 nails_hardware_lot -> 4 chairs + 4
        /// fuel-wood offcuts (each chair uses ~half a board; the rest is
        /// offcut). Labour 4 x (cut 30, assemble 60, finish 30) = 480
        /// person-minutes + 120 passive cure. One run = 4 chairs; see the
        /// class header for why the chair is batched.
        /// </summary>
        public static ProductionRecipeDetail ChairBatch()
        {
            return Recipe(
                "furniture.chair-batch", "Chair batch (4 chairs)",
                new List<ProductionRecipeInput>
                {
                    Input("lumber", 4f),
                    Input("nails_hardware_lot", 1f),
                },
                "furniture.chair", 4f,
                new List<ProductionRecipeOutput>
                {
                    Byproduct("fuel-wood", 4f),
                },
                new List<ProductionRecipePhase>
                {
                    PersonPhase("cut", 120f),
                    PersonPhase("assemble", 240f),
                    PersonPhase("finish", 120f),
                    CurePhase(),
                },
                new List<string> { "carpenter workstation present", "lumber and fasteners on hand" });
        }

        /// <summary>
        /// Small cabinet: 3 lumber, 1 nails_hardware_lot. Labour cut 60,
        /// assemble 180, finish 60 = 300 person-minutes + 120 passive cure.
        /// </summary>
        public static ProductionRecipeDetail CabinetSmall()
        {
            return Recipe(
                "furniture.cabinet-small", "Small Cabinet",
                new List<ProductionRecipeInput>
                {
                    Input("lumber", 3f),
                    Input("nails_hardware_lot", 1f),
                },
                "furniture.cabinet-small", 1f,
                null,
                new List<ProductionRecipePhase>
                {
                    PersonPhase("cut", 60f),
                    PersonPhase("assemble", 180f),
                    PersonPhase("finish", 60f),
                    CurePhase(),
                },
                new List<string> { "carpenter workstation present", "lumber and fasteners on hand" });
        }

        /// <summary>
        /// Large cabinet: 4 lumber, 1 nails_hardware_lot. Labour cut 90,
        /// assemble 240, finish 90 = 420 person-minutes + 120 passive cure.
        /// </summary>
        public static ProductionRecipeDetail CabinetLarge()
        {
            return Recipe(
                "furniture.cabinet-large", "Large Cabinet",
                new List<ProductionRecipeInput>
                {
                    Input("lumber", 4f),
                    Input("nails_hardware_lot", 1f),
                },
                "furniture.cabinet-large", 1f,
                null,
                new List<ProductionRecipePhase>
                {
                    PersonPhase("cut", 90f),
                    PersonPhase("assemble", 240f),
                    PersonPhase("finish", 90f),
                    CurePhase(),
                },
                new List<string> { "carpenter workstation present", "lumber and fasteners on hand" });
        }

        /// <summary>
        /// Shelf: 1 lumber, no fasteners (pegged/bracketed). Labour cut 30,
        /// assemble 30 = 60 person-minutes. Byproduct 1 fuel-wood offcut.
        /// No finish phase, no cure phase.
        /// </summary>
        public static ProductionRecipeDetail Shelf()
        {
            return Recipe(
                "furniture.shelf", "Shelf",
                new List<ProductionRecipeInput>
                {
                    Input("lumber", 1f),
                },
                "furniture.shelf", 1f,
                new List<ProductionRecipeOutput>
                {
                    Byproduct("fuel-wood", 1f),
                },
                new List<ProductionRecipePhase>
                {
                    PersonPhase("cut", 30f),
                    PersonPhase("assemble", 30f),
                },
                new List<string> { "carpenter workstation present", "lumber on hand" });
        }

        /// <summary>
        /// Crate: 1 lumber. Labour cut 15, assemble 30 = 45 person-minutes.
        /// Byproduct 1 fuel-wood offcut. No finish phase, no cure phase.
        /// </summary>
        public static ProductionRecipeDetail Crate()
        {
            return Recipe(
                "furniture.crate", "Crate",
                new List<ProductionRecipeInput>
                {
                    Input("lumber", 1f),
                },
                "furniture.crate", 1f,
                new List<ProductionRecipeOutput>
                {
                    Byproduct("fuel-wood", 1f),
                },
                new List<ProductionRecipePhase>
                {
                    PersonPhase("cut", 15f),
                    PersonPhase("assemble", 30f),
                },
                new List<string> { "carpenter workstation present", "lumber on hand" });
        }

        /// <summary>
        /// Bedroll: 2 yd basic_cloth, 1 hay (stuffing). Labour sew/fill 60
        /// person-minutes. No lumber — the cheap sleeping product for poor
        /// households. No finish phase, no cure phase.
        /// </summary>
        public static ProductionRecipeDetail Bedroll()
        {
            var recipe = Recipe(
                "furniture.bedroll", "Bedroll",
                new List<ProductionRecipeInput>
                {
                    Input("basic_cloth", 2f),
                    Input("hay", 1f),
                },
                "furniture.bedroll", 1f,
                null,
                new List<ProductionRecipePhase>
                {
                    PersonPhase("sew-fill", 60f),
                },
                new List<string> { "carpenter workstation present", "ticking cloth and hay on hand" });
            recipe.Substitutions.Add(ClothSubstitution(2f));
            return recipe;
        }

        public static IReadOnlyList<ProductionRecipeDetail> All()
        {
            return new[]
            {
                Bed(), Table(), ChairBatch(), CabinetSmall(), CabinetLarge(),
                Shelf(), Crate(), Bedroll(),
            };
        }
    }
}
