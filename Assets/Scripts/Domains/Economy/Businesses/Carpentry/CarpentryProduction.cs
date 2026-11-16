using System;
using System.Collections.Generic;
using LandLedgers.Economy;
using LandLedgers.Economy.Blacksmith;
using LandLedgers.Economy.Equipment;
using LandLedgers.Primitives;

namespace LandLedgers.Economy.Businesses.Carpentry
{
    /// <summary>
    /// F1 part 2 (expanded campaign 2026-11-16, O-F3): the carpentry
    /// runtime binding — the production leg that turns the workshop's
    /// lumber into furniture through the F1 part 1 recipes. Starting
    /// goes through the shared GenericProductionPolicyExecutor (the
    /// business's registered methods and make-to-stock policies, the
    /// equipment/workspace/stock checks off the business's generic
    /// configuration, FIFO lot capture for provenance), so the
    /// carpenter uses exactly the same production authority as every
    /// other generic business. Advancing goes through
    /// BusinessInstanceState.AdvanceGenericProduction with the
    /// furniture output callback (the F1 fixture's pattern, in
    /// production): one EquipmentAsset per output unit, owned by the
    /// business, carrying the maker, the consumed material lot ids,
    /// and the making day — never an inventory position, because
    /// furniture is equipment, not a generic stock line. Byproduct
    /// fuel-wood offcuts land in the workshop's generic inventory.
    ///
    /// Two carpentry-specific guards sit on top of the shared
    /// executor. First, the make-to-stock cap: the shared planner
    /// measures stock through the generic inventory, which holds no
    /// furniture (outputs are EquipmentAssets), so it always sees
    /// zero and would always start. This binding counts the
    /// workshop's existing furniture assets of the output kind in its
    /// register and refuses to start past the policy's maximum stock
    /// — the configured targets (one on hand, at most two) stay
    /// meaningful. Second, the output callback refuses a second
    /// invocation for the same process, so a completed piece is never
    /// created twice.
    ///
    /// The bed and bedroll recipes are not runnable through this
    /// binding in production: they need hay, which has no priced
    /// supply (F7 finding F3), so the start leg refuses them for lack
    /// of inputs. Don's carpentry skill is not recorded at New Game
    /// in this slice — the skill is a duration/quality modifier, not
    /// an assignment gate (the blacksmith precedent reads the level
    /// and proceeds at base when unskilled), and no precedent exists
    /// for granting an authored skill at New Game. The owner should
    /// rule Don's carpentry level; the skill recording is a follow-up.
    /// </summary>
    public sealed class CarpentryProduction
    {
        private readonly BusinessInstanceState business;
        private readonly BusinessEquipmentRegister register;
        private readonly HashSet<string> completedProcesses = new HashSet<string>(StringComparer.Ordinal);

        public CarpentryProduction(BusinessInstanceState business, BusinessEquipmentRegister register)
        {
            this.business = business;
            this.register = register;
        }

        /// <summary>
        /// Starts one furniture run for the given carpenter. Refuses
        /// when the method is not a furniture method, when the
        /// business carries no policy for it (the carpentry template
        /// installs the policies), when the inputs are not on hand, or
        /// when the workshop already holds the policy maximum of that
        /// furniture kind.
        /// </summary>
        public bool TryBeginFurniture(
            GenericProductionRuntime runtime,
            string methodId,
            EntityId carpenter,
            int dayIndex,
            out ProductionProcessState process,
            out string reason)
        {
            process = null;
            reason = string.Empty;
            if (business == null || business.RuntimeState == null || runtime == null
                || string.IsNullOrWhiteSpace(methodId))
            {
                reason = "Carpentry production requires a workshop with runtime state, a production runtime, and a furniture method id.";
                return false;
            }
            ProductionMethodDefinition method = FindFurnitureMethod(methodId);
            if (method == null)
            {
                reason = $"Carpentry production refused: '{methodId}' is not a furniture method.";
                return false;
            }
            GenericProductionPolicy policy;
            if (!business.GenericConfiguration.TryGetProductionPolicy(method.MethodId, out policy) || policy == null)
            {
                reason = $"Carpentry production refused: the workshop carries no production policy for '{method.MethodId}' — the carpentry setup template installs the policies.";
                return false;
            }
            if (policy.Mode == GenericProductionPolicyMode.MakeToStock && register != null)
            {
                int onHand = CountFurnitureOnHand(method.OutputProductId);
                if (onHand >= policy.MaximumStock)
                {
                    reason = $"Carpentry production refused: the workshop already holds {onHand} {method.OutputProductId} and the policy maximum is {policy.MaximumStock}.";
                    return false;
                }
            }
            // The output definition is unused by the shared start leg
            // (it belongs to the inventory-output advance path, which
            // furniture does not use) — pass null.
            return GenericProductionPolicyExecutor.TryStart(
                business, runtime, method.MethodId, carpenter, null, dayIndex,
                out process, out reason);
        }

        /// <summary>
        /// Advances one furniture run by the given minutes of the
        /// carpenter's work. On completion the furniture output
        /// callback creates the piece (or chair batch) as business-owned
        /// equipment with provenance.
        /// </summary>
        public bool TryAdvanceFurniture(
            GenericProductionRuntime runtime,
            ProductionProcessState process,
            EntityId carpenter,
            int minutes,
            int dayIndex,
            out string reason)
        {
            reason = string.Empty;
            if (business == null || runtime == null || process == null)
            {
                reason = "Carpentry production requires a workshop, a production runtime, and a process to advance.";
                return false;
            }
            ProductionMethodDefinition method = FindFurnitureMethod(process.MethodId);
            if (method == null)
            {
                reason = $"Carpentry production refused: process '{process.ProcessId}' runs '{process.MethodId}', which is not a furniture method.";
                return false;
            }
            if (register == null)
            {
                reason = "Carpentry production refused: the workshop has no equipment register to receive the finished furniture.";
                return false;
            }
            return business.AdvanceGenericProduction(
                runtime, process.ProcessId, carpenter, minutes,
                () => CreateFurnitureOutput(method, process, dayIndex),
                out reason);
        }

        private static ProductionMethodDefinition FindFurnitureMethod(string methodId)
        {
            if (string.IsNullOrWhiteSpace(methodId)) return null;
            foreach (ProductionMethodDefinition method in GenericVerticalCatalog.Furniture())
            {
                if (method != null && string.Equals(method.MethodId, methodId, StringComparison.OrdinalIgnoreCase))
                    return method;
            }
            return null;
        }

        private int CountFurnitureOnHand(string outputProductId)
        {
            if (register?.Assets == null || string.IsNullOrWhiteSpace(outputProductId)) return 0;
            int count = 0;
            foreach (EquipmentAsset asset in register.Assets)
            {
                if (asset != null && string.Equals(asset.Kind, outputProductId, StringComparison.OrdinalIgnoreCase))
                    count++;
            }
            return count;
        }

        /// <summary>
        /// The output callback the production binding supplies: one
        /// EquipmentAsset per output unit, with provenance (maker,
        /// material lot ids, day), plus the recipe's byproducts as real
        /// inventory. Returns false if asked to create output twice for
        /// the same process — the runtime must never do that.
        /// </summary>
        private bool CreateFurnitureOutput(ProductionMethodDefinition method, ProductionProcessState process, int dayIndex)
        {
            if (!completedProcesses.Add(process.ProcessId)) return false;
            int units = Math.Max(1, (int)method.OutputQuantity);
            var lots = new List<string>();
            if (process.ConsumedInputLotIds != null) lots.AddRange(process.ConsumedInputLotIds);
            for (int i = 0; i < units; i++)
            {
                register.RegisterAsset(new EquipmentAsset
                {
                    AssetId = $"{method.OutputProductId}-carpentry-{business.InstanceId}-{process.ProcessId}-{i + 1}",
                    Kind = method.OutputProductId,
                    Condition01 = 1f,
                    OwnerKind = "business",
                    OwnerId = business.InstanceId,
                    LocationId = business.InstanceId,
                    MadeByBusinessId = business.InstanceId,
                    MaterialLotIds = new List<string>(lots),
                    MadeDayIndex = dayIndex,
                });
            }
            foreach (ProductionInputRequirement byproduct in method.ByproductOutputs ?? new List<ProductionInputRequirement>())
            {
                if (byproduct == null || byproduct.Quantity <= 0f) continue;
                business.GenericConfiguration.AddInventory(byproduct.ProductId, byproduct.Quantity, 0, null,
                    $"furniture production {method.MethodId} by {business.InstanceId} (offcuts)", dayIndex);
            }
            return true;
        }
    }
}
