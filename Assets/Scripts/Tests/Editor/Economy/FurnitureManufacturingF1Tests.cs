// Copyright (c) Land & Ledgers contributors. Licensed under the MIT License.
// Agent F, slice F1 part 1 (expanded campaign 2026-11-16): carpentry
// manufacturing — the `carpentry` skill, the carpenter workstation, and the
// 8 furniture recipes (bed, table, chair batch, cabinets, shelf, crate,
// bedroll) driven through GenericProductionRuntime with a test business
// fixture only.
//
// The producing business is NOT decided here (O-F3, open): no test assumes
// a carpenter business exists, and no test wires a business to run the
// recipes in production. The fixture is a Generic business the tests build
// by hand, exactly as Pass12 does for the bakery. The runtime's registry
// path (GenericProductionRuntimeRegistry) is deliberately NOT driven to
// completion here: it creates output as an inventory position, while a
// furniture output must be an EquipmentAsset with provenance, created by
// the caller's output callback. The carpenter runtime binding (O-F3
// wiring) supplies that callback in production; these tests supply it in
// the fixture and prove the callback is invoked exactly once.
//
// What is proven here (plan section 4.4):
// - Every recipe consumes exactly its inputs once (no input, no output,
//   loud refusal; a short later input rolls earlier consumption back) and
//   creates exactly its outputs as EquipmentAssets with provenance.
// - The carpenter workstation gates the work through EquipmentTaskGate
//   (no workbench/kit/skill, no production — never a silent pass), and
//   the recipe feasibility check names the missing requirement.
// - Production takes real simulated time: person phases occupy the
//   carpenter through TaskAuthority; passive cure phases advance time
//   without creating a task for the person.
// - Manufacture never installs: the created asset is owned by the
//   business and located at the business, with no building association.
//   Installation is B0's separate step.

using System;
using System.Collections.Generic;
using LandLedgers.Economy;
using LandLedgers.Economy.Blacksmith;
using LandLedgers.Economy.Equipment;
using LandLedgers.Economy.Equipment.Workstations;
using LandLedgers.Economy.GenericBusiness;
using LandLedgers.Primitives;
using LandLedgers.Skills;
using LandLedgers.Tasks;
using LandLedgers.Time;
using NUnit.Framework;

namespace LandLedgers.Editor.Economy
{
    [TestFixture]
    public sealed class FurnitureManufacturingF1Tests
    {
        private const string BusinessInstanceId = "f1-test-carpenter-shop";
        private const int DayIndex = 1;

        /// <summary>
        /// The test shop: a Generic business fixture (Pass12 pattern) with
        /// the furniture methods + policies installed, the carpenter
        /// equipment and workspace registered, and a plain-dictionary
        /// inventory standing in for the business's real stock. Lot ids are
        /// tracked per product so the output asset can carry real provenance.
        /// </summary>
        private sealed class Shop
        {
            public readonly BusinessInstanceState Business;
            public readonly TaskAuthority Tasks = new TaskAuthority();
            public readonly WorkTimeBudgetStore Budgets = new WorkTimeBudgetStore();
            public readonly EntityId BusinessId = EntityId.For(EntityKind.Business, 910);
            public readonly EntityId Carpenter = EntityId.For(EntityKind.Person, 910);
            public readonly Dictionary<string, float> Stock =
                new Dictionary<string, float>(StringComparer.OrdinalIgnoreCase);
            public readonly Dictionary<string, List<string>> LotIds =
                new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
            public readonly HashSet<string> Equipment =
                new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            public readonly HashSet<string> Workspaces =
                new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            public readonly List<EquipmentAsset> CreatedAssets = new List<EquipmentAsset>();
            public int OutputCallbackInvocations;

            public Shop()
            {
                BusinessProfileDefinition profile =
                    BusinessProfileDefinition.CreateFallback(BusinessType.Generic, "F1 Test Carpenter Shop");
                Business = BusinessInstanceState.Create(
                    BusinessInstanceId, profile, -1, BusinessOwnerIdentity.Player());
                GenericBusinessVerticalService.ConfigureCarpentry(Business);
                Business.RegisterGenericEquipmentAsset("workbench");
                Business.RegisterGenericEquipmentAsset("carpenter-hand-tool-kit");
                Business.RegisterGenericWorkspace("carpenter-workshop");
                Equipment.Add("workbench");
                Equipment.Add("carpenter-hand-tool-kit");
                Workspaces.Add("carpenter-workshop");
            }

            public void StockUp(string productId, float quantity, string lotId)
            {
                float onHand = 0f;
                Stock.TryGetValue(productId, out onHand);
                Stock[productId] = onHand + quantity;
                if (!LotIds.TryGetValue(productId, out List<string> lots))
                {
                    lots = new List<string>();
                    LotIds[productId] = lots;
                }
                for (int i = 0; i < (int)quantity; i++) lots.Add(lotId + "-" + (lots.Count + 1));
            }

            public float OnHand(string productId)
            {
                float onHand = 0f;
                Stock.TryGetValue(productId, out onHand);
                return onHand;
            }

            public GenericProductionRuntime NewRuntime() =>
                new GenericProductionRuntime(Tasks, Budgets, BusinessId, DayIndex);

            public bool TryBegin(
                GenericProductionRuntime runtime, string methodId,
                out ProductionProcessState process, out string reason)
            {
                ProductionMethodDefinition method = null;
                foreach (ProductionMethodDefinition candidate in GenericVerticalCatalog.Furniture())
                {
                    if (string.Equals(candidate.MethodId, methodId, StringComparison.OrdinalIgnoreCase))
                    {
                        method = candidate;
                        break;
                    }
                }
                Assert.IsNotNull(method, $"Furniture catalog has no method '{methodId}'.");
                var policy = new GenericProductionPolicy
                {
                    MethodId = method.MethodId,
                    Mode = GenericProductionPolicyMode.MakeToOrder,
                    BatchSize = 1f,
                    Enabled = true,
                };
                var plan = new GenericProductionPlan { Policy = policy, Method = method, Quantity = 1f };
                return runtime.TryBegin(plan, Carpenter,
                    id => Equipment.Contains(id),
                    id => Workspaces.Contains(id),
                    id => OnHand(id) > 0f,
                    _ => true,
                    Consume,
                    Rollback,
                    out process, out reason);
            }

            private bool Consume(ProductionInputRequirement requirement)
            {
                if (requirement == null || OnHand(requirement.ProductId) + 0.001f < requirement.Quantity)
                    return false;
                Stock[requirement.ProductId] = OnHand(requirement.ProductId) - requirement.Quantity;
                return true;
            }

            private void Rollback(ProductionInputRequirement requirement)
            {
                if (requirement == null) return;
                Stock[requirement.ProductId] = OnHand(requirement.ProductId) + requirement.Quantity;
            }

            /// <summary>
            /// The output callback the carpenter runtime binding will supply
            /// in production: one EquipmentAsset per output unit, with
            /// provenance (maker, material lots, day), plus the recipe's
            /// byproducts as real inventory. Returns false if asked to
            /// create output twice — the runtime must never do that.
            /// </summary>
            public bool CreateFurnitureOutput(ProductionMethodDefinition method)
            {
                OutputCallbackInvocations++;
                if (OutputCallbackInvocations > 1) return false;
                int units = Math.Max(1, (int)method.OutputQuantity);
                for (int i = 0; i < units; i++)
                {
                    var lots = new List<string>();
                    foreach (ProductionInputRequirement input in method.InputRequirements)
                    {
                        if (LotIds.TryGetValue(input.ProductId, out List<string> ids))
                            lots.AddRange(ids);
                    }
                    CreatedAssets.Add(new EquipmentAsset
                    {
                        AssetId = method.OutputProductId + "-f1-" + (CreatedAssets.Count + 1),
                        Kind = method.OutputProductId,
                        Condition01 = 1f,
                        OwnerKind = "business",
                        OwnerId = BusinessInstanceId,
                        LocationId = BusinessInstanceId,
                        MadeByBusinessId = BusinessInstanceId,
                        MaterialLotIds = lots,
                        MadeDayIndex = DayIndex,
                    });
                }
                foreach (ProductionInputRequirement byproduct in method.ByproductOutputs)
                {
                    float onHand = 0f;
                    Stock.TryGetValue(byproduct.ProductId, out onHand);
                    Stock[byproduct.ProductId] = onHand + byproduct.Quantity;
                }
                return true;
            }
        }

        private static float PersonMinutes(ProductionRecipeDetail recipe)
        {
            float total = 0f;
            foreach (ProductionRecipePhase phase in recipe.Phases)
            {
                if (phase != null && phase.RequiresPerson) total += phase.DurationGameSeconds / 60f;
            }
            return total;
        }

        private static float InputQty(ProductionRecipeDetail recipe, string productId)
        {
            foreach (ProductionRecipeInput input in recipe.Inputs)
            {
                if (input != null && string.Equals(input.ProductId, productId, StringComparison.OrdinalIgnoreCase))
                    return input.Quantity;
            }
            return 0f;
        }

        // ---------- recipe data ----------

        [Test]
        public void Recipes_AllEight_ValidateCleanAndHaveUniqueMethodIds()
        {
            IReadOnlyList<ProductionRecipeDetail> recipes = FurnitureRecipePresets.All();
            Assert.AreEqual(8, recipes.Count, "First wave is carpentry only: bed, table, chair batch, 2 cabinets, shelf, crate, bedroll.");
            var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (ProductionRecipeDetail recipe in recipes)
            {
                Assert.AreEqual(0, recipe.Validate().Count,
                    $"Recipe '{recipe.MethodId}' must validate clean: {string.Join("; ", recipe.Validate())}.");
                Assert.IsTrue(ids.Add(recipe.MethodId), $"Duplicate method id '{recipe.MethodId}'.");
                Assert.AreEqual("carpentry", recipe.PerformanceSkillId);
                Assert.AreEqual(SkillIds.Carpentry, recipe.PerformanceSkillId);
                Assert.Contains("workbench", recipe.RequiredEquipmentIds);
                Assert.Contains("carpenter-hand-tool-kit", recipe.RequiredEquipmentIds);
                Assert.Contains("carpenter-workshop", recipe.RequiredWorkspaceIds);
                // Whole commercial units only: no fractional lot is ever consumed.
                foreach (ProductionRecipeInput input in recipe.Inputs)
                {
                    Assert.AreEqual(input.Quantity, (float)Math.Round(input.Quantity), 0.001f,
                        $"Recipe '{recipe.MethodId}' input '{input.ProductId}' must be a whole number of units, not {input.Quantity}.");
                    Assert.GreaterOrEqual(input.Quantity, 1f);
                }
            }
        }

        [Test]
        public void Recipes_MatchPlanTable_InputsLabourAndByproducts()
        {
            ProductionRecipeDetail bed = FurnitureRecipePresets.Bed();
            Assert.AreEqual(2f, InputQty(bed, "lumber"));
            Assert.AreEqual(1f, InputQty(bed, "nails_hardware_lot"));
            Assert.AreEqual(4f, InputQty(bed, "basic_cloth"));
            Assert.AreEqual(1f, InputQty(bed, "hay"), "One whole hay unit stuffs one mattress (plan 4.2 pin, resolved).");
            Assert.AreEqual(0f, InputQty(bed, "bedding"), "Bedding is NOT an input — no legitimate supply exists.");
            Assert.AreEqual(300f, PersonMinutes(bed));
            Assert.AreEqual(0, bed.Byproducts.Count);

            ProductionRecipeDetail table = FurnitureRecipePresets.Table();
            Assert.AreEqual(2f, InputQty(table, "lumber"));
            Assert.AreEqual(1f, InputQty(table, "nails_hardware_lot"));
            Assert.AreEqual(210f, PersonMinutes(table));
            Assert.AreEqual(0, table.Byproducts.Count);

            ProductionRecipeDetail chairs = FurnitureRecipePresets.ChairBatch();
            Assert.AreEqual("furniture.chair-batch", chairs.MethodId);
            Assert.AreEqual(4f, InputQty(chairs, "lumber"));
            Assert.AreEqual(1f, InputQty(chairs, "nails_hardware_lot"), "One nails lot per 4-chair batch (plan 4.2 pin, resolved).");
            Assert.AreEqual(4f, chairs.TotalOutputQuantity(), "One run makes 4 chairs.");
            Assert.AreEqual(480f, PersonMinutes(chairs));
            Assert.AreEqual(1, chairs.Byproducts.Count);
            Assert.AreEqual("fuel-wood", chairs.Byproducts[0].ProductId);
            Assert.AreEqual(4f, chairs.Byproducts[0].Quantity);

            ProductionRecipeDetail small = FurnitureRecipePresets.CabinetSmall();
            Assert.AreEqual(3f, InputQty(small, "lumber"));
            Assert.AreEqual(300f, PersonMinutes(small));

            ProductionRecipeDetail large = FurnitureRecipePresets.CabinetLarge();
            Assert.AreEqual(4f, InputQty(large, "lumber"));
            Assert.AreEqual(420f, PersonMinutes(large));

            ProductionRecipeDetail shelf = FurnitureRecipePresets.Shelf();
            Assert.AreEqual(1f, InputQty(shelf, "lumber"));
            Assert.AreEqual(0f, InputQty(shelf, "nails_hardware_lot"), "Shelf is pegged/bracketed — no fasteners.");
            Assert.AreEqual(60f, PersonMinutes(shelf));
            Assert.AreEqual(1f, shelf.Byproducts[0].Quantity);

            ProductionRecipeDetail crate = FurnitureRecipePresets.Crate();
            Assert.AreEqual(1f, InputQty(crate, "lumber"));
            Assert.AreEqual(45f, PersonMinutes(crate));
            Assert.AreEqual(1f, crate.Byproducts[0].Quantity);

            ProductionRecipeDetail bedroll = FurnitureRecipePresets.Bedroll();
            Assert.AreEqual(0f, InputQty(bedroll, "lumber"), "Bedroll needs no lumber — the cheap sleeping product.");
            Assert.AreEqual(2f, InputQty(bedroll, "basic_cloth"));
            Assert.AreEqual(1f, InputQty(bedroll, "hay"));
            Assert.AreEqual(60f, PersonMinutes(bedroll));

            // Flannel may stand in for ticking cloth; nothing else substitutes.
            Assert.AreEqual(1, bed.Substitutions.Count);
            Assert.AreEqual("basic_cloth", bed.Substitutions[0].OriginalProductId);
            Assert.Contains("flannel", bed.Substitutions[0].AlternativeProductIds);
            Assert.AreEqual(4f, bed.Substitutions[0].AlternativeQuantity);
            Assert.AreEqual(0, table.Substitutions.Count, "No substitution is recorded where the caller cannot use one.");
        }

        [Test]
        public void Recipes_FinishedWoodCuresPassively_UnfinishedWoodDoesNot()
        {
            foreach (ProductionRecipeDetail recipe in new[]
            {
                FurnitureRecipePresets.Bed(), FurnitureRecipePresets.Table(),
                FurnitureRecipePresets.ChairBatch(), FurnitureRecipePresets.CabinetSmall(),
                FurnitureRecipePresets.CabinetLarge(),
            })
            {
                ProductionRecipePhase cure = null;
                foreach (ProductionRecipePhase phase in recipe.Phases)
                {
                    if (phase != null && !phase.RequiresPerson) cure = phase;
                }
                Assert.IsNotNull(cure, $"Recipe '{recipe.MethodId}' must have a passive cure phase.");
                Assert.AreEqual("cure", cure.PhaseId);
                Assert.AreEqual(120f * 60f, cure.DurationGameSeconds);
            }
            foreach (ProductionRecipeDetail recipe in new[]
            {
                FurnitureRecipePresets.Shelf(), FurnitureRecipePresets.Crate(),
                FurnitureRecipePresets.Bedroll(),
            })
            {
                foreach (ProductionRecipePhase phase in recipe.Phases)
                    Assert.IsTrue(phase.RequiresPerson,
                        $"Recipe '{recipe.MethodId}' has no finish phase and no passive phase.");
            }
        }

        [Test]
        public void ToMethodDefinition_DerivesRuntimeMethodFromRecipe()
        {
            ProductionMethodDefinition bed = FurnitureRecipePresets.Bed().ToMethodDefinition();
            Assert.AreEqual("furniture.bed", bed.MethodId);
            Assert.AreEqual("furniture.bed", bed.OutputProductId);
            Assert.AreEqual(1f, bed.OutputQuantity);
            Assert.AreEqual(4, bed.InputRequirements.Count);
            Assert.AreEqual("carpentry", bed.PerformanceSkillId);
            Assert.Contains("workbench", bed.RequiredEquipmentIds);
            Assert.Contains("carpenter-hand-tool-kit", bed.RequiredEquipmentIds);
            Assert.Contains("carpenter-workshop", bed.RequiredWorkspaceIds);
            Assert.AreEqual(4, bed.Phases.Count);
            Assert.AreEqual(60, bed.Phases[0].ElapsedMinutes);
            Assert.AreEqual(180, bed.Phases[1].ElapsedMinutes);
            Assert.AreEqual(60, bed.Phases[2].ElapsedMinutes);
            Assert.AreEqual(120, bed.Phases[3].ElapsedMinutes, "Cure phase: seconds convert to whole minutes exactly.");
            Assert.IsTrue(bed.Phases[0].RequiresPerson);
            Assert.IsFalse(bed.Phases[3].RequiresPerson);
            Assert.Contains("workbench", bed.Phases[0].ReservedEquipmentIds);
            Assert.Contains("carpenter-workshop", bed.Phases[0].ReservedWorkspaceIds);
            Assert.AreEqual(0, bed.Phases[3].ReservedEquipmentIds.Count, "Cure holds the shop, not the tools.");
            Assert.Contains("carpenter-workshop", bed.Phases[3].ReservedWorkspaceIds);
            Assert.AreEqual(1, bed.InputSubstitutions.Count);
            Assert.AreEqual("flannel", bed.InputSubstitutions[0].AlternativeProductIds[0]);

            ProductionMethodDefinition chairs = FurnitureRecipePresets.ChairBatch().ToMethodDefinition();
            Assert.AreEqual("furniture.chair", chairs.OutputProductId);
            Assert.AreEqual(4f, chairs.OutputQuantity, "One run makes 4 chairs.");
            Assert.AreEqual(1, chairs.ByproductOutputs.Count);
            Assert.AreEqual("fuel-wood", chairs.ByproductOutputs[0].ProductId);
            Assert.AreEqual(4f, chairs.ByproductOutputs[0].Quantity);
        }

        // ---------- skill and workstation ----------

        [Test]
        public void Skill_CarpentryId_IsCanonicalAndUsedByRecipesAndWorkstation()
        {
            Assert.AreEqual("carpentry", SkillIds.Carpentry);
            Assert.AreEqual(SkillIds.Carpentry, FurnitureRecipePresets.CarpentrySkillId);
            Assert.AreEqual(SkillIds.Carpentry, FurnitureRecipePresets.Bed().PerformanceSkillId);
            WorkstationDefinition def = WorkstationCatalog.CarpenterWorkstation;
            Assert.AreEqual(1, def.SupportRequirements.Count);
            Assert.AreEqual("operator-skill", def.SupportRequirements[0].Kind);
            Assert.AreEqual(SkillIds.Carpentry, def.SupportRequirements[0].Detail);
        }

        [Test]
        public void Workstation_CarpenterWorkshop_DefinedInCatalog()
        {
            WorkstationDefinition def = WorkstationCatalog.CarpenterWorkstation;
            Assert.AreEqual("carpenter-workshop", def.WorkstationId);
            Assert.AreEqual("Carpenter Workshop", def.DisplayName);
            Assert.AreEqual("carpenter-workshop", def.RequiredSpaceKind);
            Assert.AreEqual(2, def.Components.Count);
            Assert.AreEqual("workbench", def.Components[0].EquipmentKind);
            Assert.AreEqual(1, def.Components[0].Count);
            Assert.AreEqual("carpenter-hand-tool-kit", def.Components[1].EquipmentKind);
            Assert.AreEqual(1, def.Components[1].Count);
            Assert.Contains("make-furniture", def.CapabilitiesGranted);
            Assert.IsFalse(string.IsNullOrWhiteSpace(def.SourceNote));
            bool inAll = false;
            foreach (WorkstationDefinition candidate in WorkstationCatalog.All)
            {
                if (string.Equals(candidate.WorkstationId, "carpenter-workshop", StringComparison.Ordinal))
                {
                    inAll = true;
                    break;
                }
            }
            Assert.IsTrue(inAll,
                "CarpenterWorkshop must be in WorkstationCatalog.All (the catalog builds a fresh instance per access, so compare by id, not by reference).");
            Assert.AreEqual(19, WorkstationCatalog.All.Count, "17 catalogued stations + the carpenter workshop + the household stove.");
        }

        private static WorkstationComponentView? FindIn(
            Dictionary<string, EquipmentAsset> byId, string assetId)
        {
            if (byId.TryGetValue(assetId, out EquipmentAsset asset) && asset != null)
            {
                return new WorkstationComponentView
                {
                    AssetId = asset.AssetId, Kind = asset.Kind,
                    Condition01 = asset.Condition01, IsUsable = asset.IsUsable,
                };
            }
            return null;
        }

        private static EquipmentAsset Asset(string id, string kind, float condition = 0.9f) =>
            new EquipmentAsset { AssetId = id, Kind = kind, Condition01 = condition };

        [Test]
        public void Workstation_EvaluateReady_RefusesMissingKitAndWreckedBench()
        {
            WorkstationDefinition def = WorkstationCatalog.CarpenterWorkstation;
            var parts = new Dictionary<string, EquipmentAsset>
            {
                ["bench-1"] = Asset("bench-1", "workbench"),
            };
            var station = new WorkstationInstance
            {
                InstanceId = "carpenter-1", WorkstationId = "carpenter-workshop",
                BusinessInstanceId = BusinessInstanceId, SpaceId = "carpenter-workshop",
            };
            station.InstallComponent("bench-1");
            string reason = station.EvaluateReady(def, id => FindIn(parts, id), new List<string>());
            Assert.IsNotNull(reason, "A bench without the hand-tool kit is not a carpenter workshop.");
            StringAssert.Contains("carpenter-hand-tool-kit", reason);

            parts["kit-1"] = Asset("kit-1", "carpenter-hand-tool-kit");
            station.InstallComponent("kit-1");
            Assert.IsNull(station.EvaluateReady(def, id => FindIn(parts, id), new List<string>()),
                "Bench + kit + space is a ready carpenter workshop.");

            parts["bench-1"].ApplyWear(0.99f);
            reason = station.EvaluateReady(def, id => FindIn(parts, id), new List<string>());
            Assert.IsNotNull(reason, "A wrecked workbench blocks the station (Tech X §3.9).");
            StringAssert.Contains("workbench", reason);

            var noSpace = new WorkstationInstance
            {
                InstanceId = "carpenter-2", WorkstationId = "carpenter-workshop",
                BusinessInstanceId = BusinessInstanceId, SpaceId = string.Empty,
            };
            noSpace.InstallComponent("bench-1");
            noSpace.InstallComponent("kit-1");
            Assert.IsNotNull(noSpace.EvaluateReady(def, id => FindIn(parts, id), new List<string>()),
                "A space alone never grants the workstation — and no space grants nothing.");
        }

        [Test]
        public void Gate_WorkstationCode_RefusesWithoutStationKitOrSkill()
        {
            var parts = new Dictionary<string, EquipmentAsset>
            {
                ["bench-1"] = Asset("bench-1", "workbench"),
                ["kit-1"] = Asset("kit-1", "carpenter-hand-tool-kit"),
            };
            var assets = new List<EquipmentAsset> { parts["bench-1"], parts["kit-1"] };
            var stations = new BusinessWorkstations(BusinessInstanceId);
            var gate = new EquipmentTaskGate
            {
                AssetsSupplier = () => assets,
                WorkstationsFor = (kind, id) => stations,
                FindWorkstationDefinition = id =>
                    string.Equals(id, "carpenter-workshop", StringComparison.Ordinal)
                        ? WorkstationCatalog.CarpenterWorkstation
                        : null,
                FindComponent = id => FindIn(parts, id),
                SupportSupplier = () => new SupportContext
                {
                    AvailableOperatorSkills = new List<string> { "carpentry" },
                },
            };
            var definition = new TaskDefinition("f1-carpentry-phase", "Carpentry phase", 60);
            definition.EquipmentClasses.Add("workstation:carpenter-workshop");

            string refusal = gate.CheckExecution("f1-carpentry-phase", definition,
                "business", BusinessInstanceId, DayIndex, new List<string>());
            Assert.IsNotNull(refusal, "No station established — the gate must refuse.");
            StringAssert.Contains("not established", refusal);

            var station = stations.GetOrCreate("carpenter-workshop");
            station.SpaceId = "carpenter-workshop";
            station.InstallComponent("bench-1");
            refusal = gate.CheckExecution("f1-carpentry-phase", definition,
                "business", BusinessInstanceId, DayIndex, new List<string>());
            Assert.IsNotNull(refusal, "Station without the kit must refuse.");
            StringAssert.Contains("carpenter-hand-tool-kit", refusal);

            station.InstallComponent("kit-1");
            gate.SupportSupplier = () => new SupportContext
            {
                AvailableOperatorSkills = new List<string>(),
            };
            refusal = gate.CheckExecution("f1-carpentry-phase", definition,
                "business", BusinessInstanceId, DayIndex, new List<string>());
            Assert.IsNotNull(refusal, "A shop with no carpenter must refuse.");
            StringAssert.Contains("carpentry", refusal);

            gate.SupportSupplier = () => new SupportContext
            {
                AvailableOperatorSkills = new List<string> { "carpentry" },
            };
            Assert.IsNull(gate.CheckExecution("f1-carpentry-phase", definition,
                "business", BusinessInstanceId, DayIndex, new List<string>()),
                "Established station + usable components + a carpenter may proceed.");
        }

        // ---------- recipe feasibility ----------

        private static CapabilityFeasibilityInput BedFeasibilityInput()
        {
            return new CapabilityFeasibilityInput
            {
                HasPremisesRight = true,
                HasRequiredAuthority = true,
                OwnedEquipmentIds = new List<string> { "workbench", "carpenter-hand-tool-kit" },
                AvailableWorkspaceIds = new List<string> { "carpenter-workshop" },
                OnHandQuantities = new Dictionary<string, float>(StringComparer.OrdinalIgnoreCase)
                {
                    ["lumber"] = 2f,
                    ["nails_hardware_lot"] = 1f,
                    ["basic_cloth"] = 4f,
                    ["hay"] = 1f,
                },
                QualifiedPersonSkillIds = new List<string> { "carpentry" },
            };
        }

        [Test]
        public void Feasibility_BedRun_NamesEachMissingRequirement()
        {
            Assert.IsTrue(GenericBusinessCapabilitySet.CheckProductionFeasibility(
                FurnitureRecipePresets.Bed(), BedFeasibilityInput(), out List<string> reasons),
                "A fully equipped, stocked shop with a carpenter can run the bed recipe.");
            Assert.AreEqual(0, reasons.Count);

            var noKit = BedFeasibilityInput();
            noKit.OwnedEquipmentIds.Remove("carpenter-hand-tool-kit");
            Assert.IsFalse(GenericBusinessCapabilitySet.CheckProductionFeasibility(
                FurnitureRecipePresets.Bed(), noKit, out reasons));
            Assert.AreEqual(1, reasons.Count);
            StringAssert.Contains("carpenter-hand-tool-kit", reasons[0]);

            var noSkill = BedFeasibilityInput();
            noSkill.QualifiedPersonSkillIds.Clear();
            Assert.IsFalse(GenericBusinessCapabilitySet.CheckProductionFeasibility(
                FurnitureRecipePresets.Bed(), noSkill, out reasons));
            StringAssert.Contains("carpentry", string.Join(" ", reasons));

            var shortLumber = BedFeasibilityInput();
            shortLumber.OnHandQuantities["lumber"] = 1f;
            Assert.IsFalse(GenericBusinessCapabilitySet.CheckProductionFeasibility(
                FurnitureRecipePresets.Bed(), shortLumber, out reasons));
            StringAssert.Contains("lumber", string.Join(" ", reasons));

            var noPremises = BedFeasibilityInput();
            noPremises.HasPremisesRight = false;
            Assert.IsFalse(GenericBusinessCapabilitySet.CheckProductionFeasibility(
                FurnitureRecipePresets.Bed(), noPremises, out reasons));
            StringAssert.Contains("premises", string.Join(" ", reasons).ToLowerInvariant());
        }

        // ---------- runtime lifecycle ----------

        [Test]
        public void Runtime_Bed_ConsumesOnceAndCreatesOneAssetWithProvenance()
        {
            var shop = new Shop();
            shop.StockUp("lumber", 2f, "lot-lumber");
            shop.StockUp("nails_hardware_lot", 1f, "lot-nails");
            shop.StockUp("basic_cloth", 4f, "lot-cloth");
            shop.StockUp("hay", 1f, "lot-hay");
            GenericProductionRuntime runtime = shop.NewRuntime();
            ProductionMethodDefinition method = FurnitureRecipePresets.Bed().ToMethodDefinition();

            Assert.IsTrue(shop.TryBegin(runtime, "furniture.bed", out ProductionProcessState process, out string reason), reason);
            Assert.IsNotNull(process);
            Assert.AreEqual("furniture.bed", process.MethodId);
            Assert.AreEqual(0f, shop.OnHand("lumber"), "Inputs are consumed exactly once at start.");
            Assert.AreEqual(0f, shop.OnHand("nails_hardware_lot"));
            Assert.AreEqual(0f, shop.OnHand("basic_cloth"));
            Assert.AreEqual(0f, shop.OnHand("hay"));
            Assert.AreEqual(0, shop.CreatedAssets.Count, "No output before the work is done.");
            Assert.AreEqual(0, process.CurrentPhaseIndex);
            Assert.AreEqual(60, process.RemainingPhaseMinutes);

            Assert.IsTrue(runtime.Advance(process, shop.Carpenter, 60,
                () => shop.CreateFurnitureOutput(method), out reason), reason);
            Assert.AreEqual(1, process.CurrentPhaseIndex, "Cut done; join-assemble begins.");
            Assert.AreEqual(180, process.RemainingPhaseMinutes);
            Assert.IsTrue(process.PersonRequiredNow(method));
            Assert.AreEqual(0, shop.CreatedAssets.Count);

            Assert.IsTrue(runtime.Advance(process, shop.Carpenter, 180,
                () => shop.CreateFurnitureOutput(method), out reason), reason);
            Assert.AreEqual(2, process.CurrentPhaseIndex, "Join-assemble done; finish begins.");
            Assert.AreEqual(60, process.RemainingPhaseMinutes);

            Assert.IsTrue(runtime.Advance(process, shop.Carpenter, 60,
                () => shop.CreateFurnitureOutput(method), out reason), reason);
            Assert.AreEqual(3, process.CurrentPhaseIndex, "Finish done; cure begins.");
            Assert.AreEqual(120, process.RemainingPhaseMinutes);
            Assert.IsFalse(process.PersonRequiredNow(method),
                "Cure is passive — the carpenter is not held by it.");
            Assert.IsNull(shop.Tasks.GetDefinition(
                $"generic-production:{process.ProcessId}:phase:3:definition"),
                "No task is created for the passive cure phase — the person is free.");
            Assert.AreEqual(0, shop.CreatedAssets.Count, "Still no output before the cure finishes.");

            Assert.IsTrue(runtime.Advance(process, shop.Carpenter, 120,
                () => shop.CreateFurnitureOutput(method), out reason), reason);
            Assert.IsTrue(process.Completed);
            Assert.IsTrue(process.OutputCreated);
            Assert.AreEqual(1, shop.OutputCallbackInvocations, "Output is created exactly once.");
            Assert.AreEqual(1, shop.CreatedAssets.Count);

            EquipmentAsset bed = shop.CreatedAssets[0];
            Assert.AreEqual("furniture.bed", bed.Kind);
            Assert.AreEqual("business", bed.OwnerKind);
            Assert.AreEqual(BusinessInstanceId, bed.OwnerId);
            Assert.AreEqual(BusinessInstanceId, bed.LocationId,
                "Manufacture is not furnishing: the bed sits with the business, in no building.");
            Assert.AreEqual(BusinessInstanceId, bed.MadeByBusinessId);
            Assert.AreEqual(DayIndex, bed.MadeDayIndex);
            Assert.AreEqual(8, bed.MaterialLotIds.Count,
                "Provenance carries every consumed lot: 2 lumber + 1 nails + 4 cloth + 1 hay.");
            Assert.IsTrue(bed.IsUsable);

            Assert.IsFalse(runtime.Advance(process, shop.Carpenter, 60,
                () => shop.CreateFurnitureOutput(method), out _),
                "A completed process advances no further.");
            Assert.AreEqual(1, shop.OutputCallbackInvocations, "No second bed is ever created.");
            Assert.AreEqual(1, shop.CreatedAssets.Count);
        }

        [Test]
        public void Runtime_MissingLumber_RefusesAndConsumesNothing()
        {
            var shop = new Shop();
            shop.StockUp("nails_hardware_lot", 1f, "lot-nails");
            shop.StockUp("basic_cloth", 4f, "lot-cloth");
            shop.StockUp("hay", 1f, "lot-hay");
            GenericProductionRuntime runtime = shop.NewRuntime();

            Assert.IsFalse(shop.TryBegin(runtime, "furniture.bed", out ProductionProcessState process, out string reason));
            Assert.IsNull(process);
            StringAssert.Contains("lumber", reason);
            Assert.AreEqual(1f, shop.OnHand("nails_hardware_lot"), "A refused start consumes nothing.");
            Assert.AreEqual(4f, shop.OnHand("basic_cloth"));
            Assert.AreEqual(1f, shop.OnHand("hay"));
            Assert.AreEqual(0, shop.CreatedAssets.Count);
        }

        [Test]
        public void Runtime_ShortLaterInput_RollsBackEarlierConsumption()
        {
            var shop = new Shop();
            shop.StockUp("lumber", 2f, "lot-lumber");
            shop.StockUp("nails_hardware_lot", 1f, "lot-nails");
            shop.StockUp("basic_cloth", 4f, "lot-cloth");
            // No hay: the last input fails after the first three are consumed.
            GenericProductionRuntime runtime = shop.NewRuntime();

            Assert.IsFalse(shop.TryBegin(runtime, "furniture.bed", out ProductionProcessState process, out string reason));
            Assert.IsNull(process);
            Assert.AreEqual(2f, shop.OnHand("lumber"), "Rollback restores the lumber.");
            Assert.AreEqual(1f, shop.OnHand("nails_hardware_lot"), "Rollback restores the nails.");
            Assert.AreEqual(4f, shop.OnHand("basic_cloth"), "Rollback restores the cloth.");
            Assert.AreEqual(0, shop.CreatedAssets.Count);
        }

        [Test]
        public void Runtime_MissingWorkbench_RefusesAtTheRuntimeGate()
        {
            var shop = new Shop();
            shop.Equipment.Remove("workbench");
            shop.StockUp("lumber", 2f, "lot-lumber");
            shop.StockUp("nails_hardware_lot", 1f, "lot-nails");
            shop.StockUp("basic_cloth", 4f, "lot-cloth");
            shop.StockUp("hay", 1f, "lot-hay");
            GenericProductionRuntime runtime = shop.NewRuntime();

            Assert.IsFalse(shop.TryBegin(runtime, "furniture.bed", out ProductionProcessState process, out string reason));
            Assert.IsNull(process);
            StringAssert.Contains("workbench", reason);
            Assert.AreEqual(2f, shop.OnHand("lumber"), "A refused start consumes nothing.");
        }

        [Test]
        public void Runtime_ChairBatch_CreatesFourChairsAndFourOffcuts()
        {
            var shop = new Shop();
            shop.StockUp("lumber", 4f, "lot-lumber");
            shop.StockUp("nails_hardware_lot", 1f, "lot-nails");
            GenericProductionRuntime runtime = shop.NewRuntime();
            ProductionMethodDefinition method = FurnitureRecipePresets.ChairBatch().ToMethodDefinition();

            Assert.IsTrue(shop.TryBegin(runtime, "furniture.chair-batch", out ProductionProcessState process, out string reason), reason);
            Assert.AreEqual(0f, shop.OnHand("lumber"));
            Assert.AreEqual(0f, shop.OnHand("nails_hardware_lot"));

            Assert.IsTrue(runtime.Advance(process, shop.Carpenter, 120,
                () => shop.CreateFurnitureOutput(method), out reason), reason);
            Assert.IsTrue(runtime.Advance(process, shop.Carpenter, 240,
                () => shop.CreateFurnitureOutput(method), out reason), reason);
            Assert.IsTrue(runtime.Advance(process, shop.Carpenter, 120,
                () => shop.CreateFurnitureOutput(method), out reason), reason);
            Assert.IsTrue(runtime.Advance(process, shop.Carpenter, 120,
                () => shop.CreateFurnitureOutput(method), out reason), reason);
            Assert.IsTrue(process.Completed);
            Assert.AreEqual(1, shop.OutputCallbackInvocations);
            Assert.AreEqual(4, shop.CreatedAssets.Count, "One run makes exactly 4 chairs.");
            foreach (EquipmentAsset chair in shop.CreatedAssets)
            {
                Assert.AreEqual("furniture.chair", chair.Kind);
                Assert.AreEqual(BusinessInstanceId, chair.OwnerId);
                Assert.AreEqual(BusinessInstanceId, chair.LocationId);
            }
            Assert.AreEqual(4f, shop.OnHand("fuel-wood"), "Each chair yields one offcut fuel-wood.");
        }

        [Test]
        public void Runtime_Bedroll_NeedsNoLumber()
        {
            var shop = new Shop();
            shop.StockUp("basic_cloth", 2f, "lot-cloth");
            shop.StockUp("hay", 1f, "lot-hay");
            GenericProductionRuntime runtime = shop.NewRuntime();
            ProductionMethodDefinition method = FurnitureRecipePresets.Bedroll().ToMethodDefinition();

            Assert.IsTrue(shop.TryBegin(runtime, "furniture.bedroll", out ProductionProcessState process, out string reason), reason);
            Assert.IsTrue(runtime.Advance(process, shop.Carpenter, 60,
                () => shop.CreateFurnitureOutput(method), out reason), reason);
            Assert.IsTrue(process.Completed);
            Assert.AreEqual(1, shop.CreatedAssets.Count);
            Assert.AreEqual("furniture.bedroll", shop.CreatedAssets[0].Kind);
        }

        // ---------- catalog, service and planner ----------

        [Test]
        public void VerticalCatalog_Furniture_ReturnsEightDerivedMethods()
        {
            IReadOnlyList<ProductionMethodDefinition> methods = GenericVerticalCatalog.Furniture();
            Assert.AreEqual(8, methods.Count);
            var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (ProductionMethodDefinition method in methods)
            {
                Assert.IsTrue(ids.Add(method.MethodId));
                Assert.AreEqual("carpentry", method.PerformanceSkillId);
            }
            ProductionMethodDefinition chairs = null;
            foreach (ProductionMethodDefinition method in methods)
            {
                if (string.Equals(method.MethodId, "furniture.chair-batch", StringComparison.OrdinalIgnoreCase))
                    chairs = method;
            }
            Assert.IsNotNull(chairs);
            Assert.AreEqual("furniture.chair", chairs.OutputProductId);
            Assert.AreEqual(4f, chairs.OutputQuantity);
        }

        [Test]
        public void ConfigureCarpentry_RegistersMethodsAndPoliciesAndCreatesNothingElse()
        {
            BusinessProfileDefinition profile =
                BusinessProfileDefinition.CreateFallback(BusinessType.Generic, "F1 Configure Proof");
            BusinessInstanceState business = BusinessInstanceState.Create(
                "f1-configure-proof", profile, -1, BusinessOwnerIdentity.Player());
            GenericBusinessVerticalService.ConfigureCarpentry(business);

            Assert.AreEqual(8, business.GenericConfiguration.ProductionMethods.Count);
            Assert.AreEqual(8, business.GenericConfiguration.ProductionPolicies.Count);
            foreach (ProductionMethodDefinition method in business.GenericConfiguration.ProductionMethods)
            {
                Assert.IsTrue(business.GenericConfiguration.TryGetProductionPolicy(method.MethodId, out _),
                    $"Method '{method.MethodId}' must have a policy.");
            }
            Assert.AreEqual(0, business.GenericConfiguration.Inventory.Count,
                "Configuring methods creates no stock.");
            Assert.AreEqual(0, business.GenericConfiguration.EquipmentAssetIds.Count,
                "Configuring methods creates no equipment.");
            Assert.AreEqual(0, business.GenericConfiguration.WorkspaceIds.Count,
                "Configuring methods creates no workspace.");
        }

        [Test]
        public void Planner_BedPlan_EligibleWithStockAndBlockedWithoutLumber()
        {
            var shop = new Shop();
            shop.StockUp("lumber", 2f, "lot-lumber");
            shop.StockUp("nails_hardware_lot", 1f, "lot-nails");
            shop.StockUp("basic_cloth", 4f, "lot-cloth");
            shop.StockUp("hay", 1f, "lot-hay");
            ProductionMethodDefinition method = FurnitureRecipePresets.Bed().ToMethodDefinition();
            var policy = new GenericProductionPolicy
            {
                MethodId = method.MethodId,
                Mode = GenericProductionPolicyMode.MakeToOrder,
                BatchSize = 1f,
                Enabled = true,
            };

            GenericProductionPlan plan = GenericProductionPlanner.Plan(policy, method, 0f, 1f,
                input => shop.OnHand(input.ProductId),
                input => 0f);
            Assert.IsTrue(plan.IsEligible, plan.BlockedReason);
            Assert.AreEqual(1f, plan.Quantity);

            shop.Stock["lumber"] = 1f;
            plan = GenericProductionPlanner.Plan(policy, method, 0f, 1f,
                input => shop.OnHand(input.ProductId),
                input => 0f);
            Assert.IsFalse(plan.IsEligible);
            StringAssert.Contains("lumber", plan.BlockedReason);
        }
    }
}
