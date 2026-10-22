using System.Collections.Generic;
using System.Reflection;
using LandLedgers.Economy;
using LandLedgers.Primitives;
using LandLedgers.Tasks;
using LandLedgers.Time;
using NUnit.Framework;

namespace LandLedgers.EditorTests.Economy
{
    /// <summary>Pass IX review R-11: the generic production registry's
    /// provenance seam must be assigned in production — the hub constructs
    /// the registry with its own persisted SharedCategoryStockLedger as
    /// ProvenanceLedger. These tests construct the registry exactly the
    /// production way (same initializer the hub property uses) and drive a
    /// real production output through the registry path.</summary>
    [TestFixture]
    public sealed class Pass9ReviewFixProvenanceLedgerTests
    {
        /// <summary>The production construction, mirrored from
        /// SimulationSystemsHub.GenericProduction (the hub itself is
        /// excluded from the Roslyn harness by sources.txt).</summary>
        private static GenericProductionRuntimeRegistry ProductionRegistry(SharedCategoryStockLedger hubLedger)
        {
            return new GenericProductionRuntimeRegistry(new TaskAuthority(), new WorkTimeBudgetStore())
            {
                ProvenanceLedger = hubLedger,
                ProvenanceIds = new EntityIdRegistry(),
            };
        }

        private static BusinessInstanceState CreateBusiness(string instanceId)
        {
            var business = new BusinessInstanceState();
            SetField(business, "instanceId", instanceId);
            SetField(business, "businessType", BusinessType.Generic);
            SetField(business, "runtimeState", new BusinessRuntimeState());
            return business;
        }

        private static void SetField(object target, string fieldName, object value)
        {
            FieldInfo field = target.GetType().GetField(fieldName,
                BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.IsNotNull(field, "field '" + fieldName + "' not found");
            field.SetValue(target, value);
        }

        private static void RegisterTrivialMethod(BusinessInstanceState business, string methodId)
        {
            business.RegisterGenericProductionMethod(new ProductionMethodDefinition
            {
                MethodId = methodId,
                OutputProductId = methodId + "-out",
                OutputQuantity = 1f,
                Phases = new List<ProductionPhaseDefinition>
                {
                    new ProductionPhaseDefinition { ElapsedMinutes = 30, RequiresPerson = true },
                },
            });
            business.RegisterGenericProductionPolicy(new GenericProductionPolicy
            {
                MethodId = methodId,
                TargetStock = 10f,
                MaximumStock = 20f,
                Priority = 1,
            });
        }

        [Test]
        public void ProductionConstruction_RegistryProvenanceLedgerIsTheHubLedger()
        {
            var hubLedger = new SharedCategoryStockLedger();
            GenericProductionRuntimeRegistry registry = ProductionRegistry(hubLedger);

            Assert.AreSame(hubLedger, registry.ProvenanceLedger,
                "the registry records provenance into the hub's ledger, not a private one");
        }

        [Test]
        public void ProductionOutput_ThroughRegistryPath_LandsInPersistedHubLedger()
        {
            var hubLedger = new SharedCategoryStockLedger();
            GenericProductionRuntimeRegistry registry = ProductionRegistry(hubLedger);
            BusinessInstanceState business = CreateBusiness("gen-prov");
            RegisterTrivialMethod(business, "make");
            EntityId businessId = EntityId.For(EntityKind.Business, 91);

            bool started = registry.TryStartEligiblePolicy(business, businessId, 1,
                new[] { EntityId.For(EntityKind.Person, 5) }, out ProductionProcessState process, out string reason);
            Assert.IsTrue(started, reason);
            Assert.IsNotNull(process);
            Assert.AreEqual(0, hubLedger.Provenance.Count, "nothing recorded before the process completes");

            registry.AdvanceBusinessProcesses(business, businessId, 1, 24 * 60);

            Assert.IsTrue(process.Completed, "the trivial 30-minute process completes within a day");
            List<StockProvenanceRecord> records = hubLedger.FindByCategory("make-out");
            Assert.AreEqual(1, records.Count,
                "the completed output is recorded in the hub ledger through the registry path");
            Assert.AreEqual("gen-prov", records[0].BusinessId);
            Assert.AreEqual(1f, records[0].Quantity);

            // The hub ledger is the persisted one: the record survives its DTO.
            SharedCategoryStockLedger.SharedCategoryStockLedgerSaveDto dto = hubLedger.CaptureSaveDto();
            Assert.AreEqual(1, dto.provenance.Count);
            var restored = new SharedCategoryStockLedger();
            restored.LoadFromSaveDto(dto);
            Assert.AreEqual(1, restored.FindByCategory("make-out").Count);
        }
    }
}
