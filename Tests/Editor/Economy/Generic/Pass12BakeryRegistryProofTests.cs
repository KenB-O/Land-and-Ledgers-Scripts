using System.Collections.Generic;
using System.Linq;
using LandLedgers.Economy;
using LandLedgers.Primitives;
using LandLedgers.Tasks;
using LandLedgers.Time;
using NUnit.Framework;

namespace LandLedgers.EditorTests.Economy.Generic
{
    /// <summary>
    /// Pass XII Worker B: bakery and smithy proof through the LIVE generic
    /// production path (GenericProductionRuntimeRegistry, the path
    /// SimulationDrivers.DriveGenericProduction actually calls), not through
    /// the executor alone and not through the unwired staged chains.
    ///
    /// The staged chains (BakeryStagedProductionChain, ForgeRepairChain) stay
    /// gated: wiring them alongside this path would double-produce, because
    /// the generic bread/smithing methods already cover the same outputs
    /// (bread, nails, ironwork repair) on the same businesses.
    ///
    /// Bread method phases (GenericVerticalCatalog.Bread): load 1 min
    /// (person), bake 28 min (passive, oven still reserved), remove-check
    /// 1 min (person). The registry advances one phase per
    /// AdvanceBusinessProcesses call, so a loaf takes three driver days.
    /// That is the real driver behaviour, asserted here rather than hidden.
    /// </summary>
    [TestFixture]
    public sealed class Pass12BakeryRegistryProofTests
    {
        private static BusinessInstanceState CreateGenericBusiness()
        {
            // NOTE: the Roslyn harness subset excludes BusinessCreation.cs,
            // so this fixture uses the direct state factory (the same
            // BusinessInstanceState the creation authority returns) with a
            // classless Generic fallback profile. No trade permission is
            // implied: BusinessType.Generic routes to the generic path only.
            BusinessProfileDefinition profile =
                BusinessProfileDefinition.CreateFallback(BusinessType.Generic, "Pass XII Proof Enterprise");
            BusinessInstanceState business = BusinessInstanceState.Create(
                "pass12-proof-enterprise", profile, -1, BusinessOwnerIdentity.Player());
            Assert.AreEqual(BusinessType.Generic, business.BusinessType);
            Assert.AreEqual("pass12-proof-enterprise", business.InstanceId);
            return business;
        }

        private static GenericProductionRuntimeRegistry NewRegistry(
            out TaskAuthority tasks, out WorkTimeBudgetStore budgets)
        {
            tasks = new TaskAuthority();
            budgets = new WorkTimeBudgetStore();
            return new GenericProductionRuntimeRegistry(tasks, budgets);
        }

        [Test]
        public void BakeryRegistry_ShipmentReceiptBakeAndRestore_ProducesExactlyOneLoaf()
        {
            BusinessInstanceState business = CreateGenericBusiness();
            GenericBusinessVerticalService.ConfigureBakery(business);
            business.RegisterGenericEquipmentAsset("oven");
            business.RegisterGenericWorkspace("preparation");
            business.RegisterGenericWorkspace("oven-space");

            // Flour and fuel arrive through the legitimate receiving
            // authority (idempotent per shipment id), not by direct seeding.
            var flourDef = new GenericProductDefinition("flour", "Flour", ProductQuantityUnit.Each);
            var fuelDef = new GenericProductDefinition("fuel", "Fuel", ProductQuantityUnit.Each);
            Assert.IsTrue(GenericShipmentReceivingAuthority.TryReceive(
                business, "ship-flour-1", "flour", 5f, 10, flourDef, 1, out string reason), reason);
            Assert.IsFalse(GenericShipmentReceivingAuthority.TryReceive(
                business, "ship-flour-1", "flour", 5f, 10, flourDef, 1, out _),
                "The same shipment must not be received twice.");
            Assert.AreEqual(5f, business.GenericConfiguration.GetInventoryQuantity("flour"));
            Assert.IsTrue(GenericShipmentReceivingAuthority.TryReceive(
                business, "ship-fuel-1", "fuel", 5f, 5, fuelDef, 1, out reason), reason);

            GenericProductionRuntimeRegistry registry = NewRegistry(out _, out _);
            EntityId baker = EntityId.For(EntityKind.Person, 900);
            EntityId businessId = EntityId.For(EntityKind.Business, 900);
            var workers = new List<EntityId> { baker };

            // Day 1 (registry start + advance): inputs consumed exactly once,
            // load phase completes, bake phase begins, baker is released.
            Assert.IsTrue(registry.TryStartEligiblePolicy(
                business, businessId, 1, workers, out ProductionProcessState process, out reason), reason);
            Assert.IsNotNull(process);
            Assert.AreEqual("generic-bread", process.MethodId);
            Assert.AreEqual(4f, business.GenericConfiguration.GetInventoryQuantity("flour"));
            Assert.AreEqual(4f, business.GenericConfiguration.GetInventoryQuantity("fuel"));
            Assert.AreEqual(0f, business.GenericConfiguration.GetInventoryQuantity("bread"));

            // A second loaf cannot start while the oven is reserved.
            Assert.IsFalse(registry.TryStartEligiblePolicy(
                business, businessId, 1, workers, out _, out _));

            registry.AdvanceBusinessProcesses(business, businessId, 1, 24 * 60);
            Assert.AreEqual(1, process.CurrentPhaseIndex);
            Assert.AreEqual(28, process.RemainingPhaseMinutes);
            Assert.IsFalse(process.PersonRequiredNow(GenericVerticalCatalog.Bread()));
            Assert.AreEqual(0f, business.GenericConfiguration.GetInventoryQuantity("bread"));

            // Save/restore mid-bake through the real business DTO: the
            // restored business keeps the phase, the reservation, the
            // consumed inputs and zero bread, and finishes to exactly one loaf.
            BusinessInstanceState restored = BusinessInstanceState.FromSaveDto(business.CaptureSaveDto());
            Assert.IsNotNull(restored);
            ProductionProcessState restoredProcess = restored.GenericConfiguration.ActiveProcesses[0];
            Assert.AreEqual(1, restoredProcess.CurrentPhaseIndex);
            Assert.AreEqual(28, restoredProcess.RemainingPhaseMinutes);
            Assert.Contains("oven", restoredProcess.ReservedEquipmentIds);
            Assert.AreEqual(4f, restored.GenericConfiguration.GetInventoryQuantity("flour"));
            Assert.AreEqual(4f, restored.GenericConfiguration.GetInventoryQuantity("fuel"));
            Assert.AreEqual(0f, restored.GenericConfiguration.GetInventoryQuantity("bread"));

            GenericProductionRuntimeRegistry restoredRegistry = NewRegistry(out _, out _);
            restoredRegistry.AdvanceBusinessProcesses(restored, businessId, 2, 24 * 60);
            Assert.AreEqual(2, restoredProcess.CurrentPhaseIndex);
            Assert.IsTrue(restoredProcess.PersonRequiredNow(GenericVerticalCatalog.Bread()));
            Assert.AreEqual(0f, restored.GenericConfiguration.GetInventoryQuantity("bread"));

            restoredRegistry.AdvanceBusinessProcesses(restored, businessId, 3, 24 * 60);
            Assert.IsTrue(restoredProcess.Completed);
            Assert.AreEqual(1f, restored.GenericConfiguration.GetInventoryQuantity("bread"));

            // Advancing a completed process creates no second loaf.
            restoredRegistry.AdvanceBusinessProcesses(restored, businessId, 4, 24 * 60);
            Assert.AreEqual(1f, restored.GenericConfiguration.GetInventoryQuantity("bread"));
            Assert.AreEqual(4f, restored.GenericConfiguration.GetInventoryQuantity("flour"));
            Assert.AreEqual(4f, restored.GenericConfiguration.GetInventoryQuantity("fuel"));
        }

        [Test]
        public void BakeryRegistry_InterruptedBake_ProducesNothingUntilFinished()
        {
            BusinessInstanceState business = CreateGenericBusiness();
            GenericBusinessVerticalService.ConfigureBakery(business);
            business.RegisterGenericEquipmentAsset("oven");
            business.RegisterGenericWorkspace("preparation");
            business.RegisterGenericWorkspace("oven-space");
            business.GenericConfiguration.AddInventory("flour", 2f);
            business.GenericConfiguration.AddInventory("fuel", 2f);

            GenericProductionRuntimeRegistry registry = NewRegistry(out TaskAuthority tasks, out WorkTimeBudgetStore budgets);
            EntityId baker = EntityId.For(EntityKind.Person, 901);
            EntityId businessId = EntityId.For(EntityKind.Business, 901);
            Assert.IsTrue(registry.TryStartEligiblePolicy(
                business, businessId, 1, new List<EntityId> { baker },
                out ProductionProcessState process, out string reason), reason);

            // Day 1 through the registry: load done, bake phase begins.
            registry.AdvanceBusinessProcesses(business, businessId, 1, 24 * 60);
            Assert.AreEqual(1, process.CurrentPhaseIndex);
            Assert.AreEqual(28, process.RemainingPhaseMinutes);

            // A partial (interrupted) bake advance: 10 of 28 minutes, no
            // output, inputs still consumed exactly once, oven still held.
            var runtime = new GenericProductionRuntime(tasks, budgets, businessId, 1);
            runtime.RegisterMethod(GenericVerticalCatalog.Bread());
            Assert.IsTrue(runtime.Advance(process, baker, 10, () => true, out _));
            Assert.AreEqual(1, process.CurrentPhaseIndex);
            Assert.AreEqual(18, process.RemainingPhaseMinutes);
            Assert.IsFalse(process.Completed);
            Assert.Contains("oven", process.ReservedEquipmentIds);
            Assert.AreEqual(0f, business.GenericConfiguration.GetInventoryQuantity("bread"));
            Assert.AreEqual(1f, business.GenericConfiguration.GetInventoryQuantity("flour"),
                "Inputs are consumed once at start, not again on resume.");

            // Resume through the registry: bake finishes, remove-check still
            // gates the loaf; only the final phase creates output.
            registry.AdvanceBusinessProcesses(business, businessId, 2, 24 * 60);
            Assert.AreEqual(2, process.CurrentPhaseIndex);
            Assert.AreEqual(0f, business.GenericConfiguration.GetInventoryQuantity("bread"));
            registry.AdvanceBusinessProcesses(business, businessId, 3, 24 * 60);
            Assert.IsTrue(process.Completed);
            Assert.AreEqual(1f, business.GenericConfiguration.GetInventoryQuantity("bread"));
        }

        [Test]
        public void SmithyRegistry_NailsThroughLivePath_RepairStaysOrderGated()
        {
            BusinessInstanceState business = CreateGenericBusiness();
            GenericBusinessVerticalService.ConfigurePrairieForkSmithy(business);
            business.RegisterGenericEquipmentAsset("forge");
            business.RegisterGenericEquipmentAsset("anvil");
            business.RegisterGenericEquipmentAsset("smith-tools");
            business.RegisterGenericWorkspace("smithy");
            business.GenericConfiguration.AddInventory("iron", 10f);
            business.GenericConfiguration.AddInventory("fuel", 10f);

            GenericProductionRuntimeRegistry registry = NewRegistry(out _, out _);
            EntityId smith = EntityId.For(EntityKind.Person, 902);
            EntityId businessId = EntityId.For(EntityKind.Business, 902);
            Assert.IsTrue(registry.TryStartEligiblePolicy(
                business, businessId, 1, new List<EntityId> { smith },
                out ProductionProcessState process, out string reason), reason);
            // The repair policy (priority 100) is MakeToOrder and has no
            // order waiting, so it must not be picked; a stock method is.
            Assert.AreNotEqual("generic-repair-ironwork", process.MethodId);
            Assert.IsTrue(process.MethodId.StartsWith("generic-smithing-"));
            Assert.AreEqual(9f, business.GenericConfiguration.GetInventoryQuantity("iron"));
            Assert.AreEqual(9f, business.GenericConfiguration.GetInventoryQuantity("fuel"));

            // Single 10-minute person phase: one driver-day advance finishes it.
            string outputProduct = null;
            foreach (ProductionMethodDefinition method in business.GenericConfiguration.ProductionMethods)
            {
                if (method != null && string.Equals(method.MethodId, process.MethodId, System.StringComparison.OrdinalIgnoreCase))
                    outputProduct = method.OutputProductId;
            }
            Assert.IsFalse(string.IsNullOrWhiteSpace(outputProduct));
            registry.AdvanceBusinessProcesses(business, businessId, 1, 24 * 60);
            Assert.IsTrue(process.Completed);
            Assert.AreEqual(1f, business.GenericConfiguration.GetInventoryQuantity(outputProduct));
            registry.AdvanceBusinessProcesses(business, businessId, 2, 24 * 60);
            Assert.AreEqual(1f, business.GenericConfiguration.GetInventoryQuantity(outputProduct),
                "A completed process must not produce a second unit.");
        }

        [Test]
        public void SmithyRegistry_WithoutIron_RefusesStart()
        {
            BusinessInstanceState business = CreateGenericBusiness();
            GenericBusinessVerticalService.ConfigurePrairieForkSmithy(business);
            business.RegisterGenericEquipmentAsset("forge");
            business.RegisterGenericEquipmentAsset("anvil");
            business.RegisterGenericEquipmentAsset("smith-tools");
            business.RegisterGenericWorkspace("smithy");
            business.GenericConfiguration.AddInventory("fuel", 10f);

            GenericProductionRuntimeRegistry registry = NewRegistry(out _, out _);
            Assert.IsFalse(registry.TryStartEligiblePolicy(
                business, EntityId.For(EntityKind.Business, 903), 1,
                new List<EntityId> { EntityId.For(EntityKind.Person, 903) },
                out _, out _));
            Assert.IsFalse(business.GenericConfiguration.ActiveProcesses.Any(p => p != null && !p.Completed));
        }
    }
}
