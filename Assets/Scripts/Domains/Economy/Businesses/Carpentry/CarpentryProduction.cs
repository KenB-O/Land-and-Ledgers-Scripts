using System;
using System.Collections.Generic;
using System.Globalization;
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
        /// G-1 (expanded campaign 2026-11-16, W-G4 Workstream 1): whether
        /// this business is a carpentry workshop — a Generic business
        /// carrying at least one furniture production method. The driver
        /// routes such a business through the furniture path (this
        /// binding) instead of the generic inventory-output path, and
        /// skips it in the generic advance so a furniture process is
        /// never advanced twice. Structural, not identity-based: it does
        /// not depend on the owner's person id, so it survives the
        /// Millward renumbering (integration ordinal 7, person ids
        /// 14/15) and any later carpentry workshop.
        /// </summary>
        public static bool IsCarpentryWorkshop(BusinessInstanceState business)
        {
            if (business == null || business.BusinessType != BusinessType.Generic
                || business.GenericConfiguration == null) return false;
            foreach (ProductionMethodDefinition method in business.GenericConfiguration.ProductionMethods)
            {
                if (method != null && IsFurnitureMethod(method.MethodId)) return true;
            }
            return false;
        }

        /// <summary>
        /// G-1: whether the method id is a furniture method (one of the
        /// eight F1 part 1 recipes, as installed by the carpentry setup
        /// template).
        /// </summary>
        public static bool IsFurnitureMethod(string methodId)
        {
            return FindFurnitureMethod(methodId) != null;
        }

        /// <summary>
        /// G-1: whether the output kind is a furniture kind (the output
        /// product id of a furniture method, e.g. "furniture.bed").
        /// </summary>
        public static bool IsFurnitureOutputKind(string outputProductId)
        {
            if (string.IsNullOrWhiteSpace(outputProductId)) return false;
            foreach (ProductionMethodDefinition method in GenericVerticalCatalog.Furniture())
            {
                if (method != null && string.Equals(method.OutputProductId, outputProductId, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }

        /// <summary>
        /// G-1: the workshop's finished furniture assets in its register,
        /// oldest-made first (then asset id, deterministic). Workbench,
        /// tool kits and any non-furniture asset are excluded.
        /// </summary>
        public IReadOnlyList<EquipmentAsset> FinishedFurnitureAssets()
        {
            var finished = new List<EquipmentAsset>();
            if (register?.Assets == null) return finished;
            foreach (EquipmentAsset asset in register.Assets)
            {
                if (asset != null && IsFurnitureOutputKind(asset.Kind)) finished.Add(asset);
            }
            finished.Sort((a, b) =>
            {
                int byDay = a.MadeDayIndex.CompareTo(b.MadeDayIndex);
                return byDay != 0 ? byDay : string.CompareOrdinal(a.AssetId, b.AssetId);
            });
            return finished;
        }

        /// <summary>
        /// G-1: starts the highest-priority eligible furniture run for
        /// the workshop — the driver start leg. Mirrors
        /// GenericProductionRuntimeRegistry.TryStartEligiblePolicy, with
        /// one correction: the plans are built with the TRUE on-hand
        /// count (the workshop's furniture assets of the output kind in
        /// its register), not the generic inventory quantity (which
        /// holds no furniture and always reads zero). A method at its
        /// target stock is therefore ineligible in the plan itself, and
        /// the cap in TryBeginFurniture stands as the backstop. All
        /// eight policies carry priority 10, so ties resolve to the
        /// first eligible method in catalog order (bed, table, chair
        /// batch, cabinet-small, cabinet-large, shelf, crate, bedroll);
        /// bed and bedroll stay ineligible while hay has no priced
        /// supply. One start attempt per call, like the generic driver;
        /// workers already assigned to an active process are skipped.
        /// </summary>
        public bool TryBeginEligibleFurniture(
            GenericProductionRuntime runtime,
            IEnumerable<EntityId> workerIds,
            int dayIndex,
            out ProductionProcessState process,
            out string reason)
        {
            process = null;
            reason = string.Empty;
            if (business == null || business.GenericConfiguration == null || runtime == null || workerIds == null)
            {
                reason = "Carpentry production requires a workshop, a production runtime, and workers to start an eligible furniture run.";
                return false;
            }
            var plans = new List<GenericProductionPlan>();
            foreach (GenericProductionPolicy policy in business.GenericConfiguration.ProductionPolicies)
            {
                if (policy == null) continue;
                ProductionMethodDefinition method = FindFurnitureMethod(policy.MethodId);
                if (method == null) continue;
                plans.Add(GenericProductionPlanner.Plan(
                    policy,
                    method,
                    CountFurnitureOnHand(method.OutputProductId),
                    0f,
                    input => business.GenericConfiguration.GetAvailableInventoryQuantity(input.ProductId),
                    _ => 0f));
            }
            GenericProductionPlan selected = GenericProductionPlanner.ChooseHighestPriority(plans);
            if (selected == null)
            {
                reason = "Carpentry production refused: no furniture policy is eligible — every method is at target stock, disabled, or short of inputs.";
                return false;
            }
            // One start attempt per call, for the first available worker
            // (the generic driver's shape): a refusal names the real
            // gate (inputs, equipment, workspace) and is logged by the
            // driver, not retried against the next method.
            foreach (EntityId workerId in workerIds)
            {
                if (!workerId.IsValid || IsWorkerAssigned(workerId)) continue;
                return TryBeginFurniture(runtime, selected.Method.MethodId, workerId, dayIndex, out process, out reason);
            }
            reason = "Carpentry production refused: no available worker to start the eligible furniture run.";
            return false;
        }

        /// <summary>
        /// G-1: advances every active furniture process on the workshop
        /// by the given minutes — the driver advance leg. Furniture
        /// processes go through TryAdvanceFurniture (the equipment-output
        /// callback); a non-furniture active process is NEVER advanced
        /// here and is reported in the reason, because the driver skips
        /// the workshop in the generic advance — a process the furniture
        /// route cannot advance would otherwise stall silently. The
        /// workshop's template installs furniture methods only, so a
        /// non-furniture active process means something else started
        /// work on the workshop and must be investigated, not advanced.
        /// </summary>
        public bool TryAdvanceFurnitureProcesses(
            GenericProductionRuntime runtime,
            int minutes,
            int dayIndex,
            out string reason)
        {
            reason = string.Empty;
            if (business == null || business.GenericConfiguration == null || runtime == null)
            {
                reason = "Carpentry production requires a workshop and a production runtime to advance furniture processes.";
                return false;
            }
            string firstRefusal = null;
            foreach (ProductionProcessState process in new List<ProductionProcessState>(business.GenericConfiguration.ActiveProcesses))
            {
                if (process == null || process.Completed) continue;
                if (!IsFurnitureMethod(process.MethodId))
                {
                    firstRefusal = firstRefusal ?? $"Carpentry production refused: process '{process.ProcessId}' runs '{process.MethodId}', which is not a furniture method — the workshop carries a non-furniture process the furniture route cannot advance.";
                    continue;
                }
                if (!TryParsePerson(process.AssignedPersonId, out EntityId personId))
                {
                    firstRefusal = firstRefusal ?? $"Carpentry production refused: process '{process.ProcessId}' names no parseable assigned person ('{process.AssignedPersonId}') — it cannot be advanced.";
                    continue;
                }
                if (!TryAdvanceFurniture(runtime, process, personId, minutes, dayIndex, out string advanceReason))
                    firstRefusal = firstRefusal ?? advanceReason;
            }
            if (firstRefusal != null)
            {
                reason = firstRefusal;
                return false;
            }
            return true;
        }

        private bool IsWorkerAssigned(EntityId workerId)
        {
            foreach (ProductionProcessState active in business.GenericConfiguration.ActiveProcesses)
            {
                if (active != null && !active.Completed
                    && string.Equals(active.AssignedPersonId, workerId.ToString(), StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }

        /// <summary>
        /// Mirrors GenericProductionRuntimeRegistry.TryParsePerson: the
        /// assigned person id is the EntityId string form ("P{personId}").
        /// </summary>
        private static bool TryParsePerson(string value, out EntityId personId)
        {
            personId = EntityId.Invalid;
            if (string.IsNullOrWhiteSpace(value) || !value.StartsWith("P", StringComparison.OrdinalIgnoreCase)) return false;
            return int.TryParse(value.Substring(1), NumberStyles.Integer, CultureInfo.InvariantCulture, out int id)
                && id >= 0
                && (personId = EntityId.For(EntityKind.Person, id)).IsValid;
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
