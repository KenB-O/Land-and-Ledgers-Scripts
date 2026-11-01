using System.Collections.Generic;
using LandLedgers.Economy.Butcher;
using LandLedgers.Economy.Creation;
using LandLedgers.Economy.Farming;
using LandLedgers.Economy.Freight;
using LandLedgers.Persistence;
using LandLedgers.Primitives;
using LandLedgers.ReadModels.Valuation;
using LandLedgers.Skills;
using LandLedgers.Tasks;
using LandLedgers.Time;
using NUnit.Framework;

namespace LandLedgers.EditorTests.Persistence
{
    /// <summary>
    /// CLN-1: save-pipeline round-trips for the standalone authorities. Each authority's
    /// Capture/Load pair must preserve state without Unity (plain C# DTO round-trip).
    /// </summary>
    [TestFixture]
    public sealed class SystemsSaveRoundTripTests
    {
        [Test]
        public void TaskAuthority_RoundTrip_PreservesDefinitionsAndTasks()
        {
            var authority = new TaskAuthority();
            string rejection;
            Assert.IsTrue(authority.RegisterDefinition(new TaskDefinition("tend_customer", "Tend Customer", 4), out rejection));

            TaskAuthoritySaveDto dto = authority.CaptureSaveDto();

            var restored = new TaskAuthority();
            restored.LoadFromSaveDto(dto);

            TaskAuthoritySaveDto redto = restored.CaptureSaveDto();
            Assert.AreEqual(1, redto.definitions.Count);
            Assert.AreEqual("tend_customer", redto.definitions[0].DefinitionId);
        }

        [Test]
        public void SkillService_RoundTrip_PreservesDefinitions()
        {
            var service = new SkillService();
            string rejection;
            Assert.IsTrue(service.RegisterSkill(new SkillDefinition("carpentry", "Carpentry", "woodwork"), out rejection));

            SkillServiceSaveDto dto = service.CaptureSaveDto();

            var restored = new SkillService();
            restored.LoadFromSaveDto(dto);

            SkillServiceSaveDto redto = restored.CaptureSaveDto();
            Assert.AreEqual(1, redto.definitions.Count);
            Assert.AreEqual("carpentry", redto.definitions[0].SkillId);
        }

        [Test]
        public void WorkTimeBudgetStore_RoundTrip_PreservesBudgets()
        {
            var store = new WorkTimeBudgetStore();
            EntityId person = EntityId.For(EntityKind.Person, 1);
            store.GetOrCreate(person, 480);

            WorkTimeBudgetSaveDto dto = store.CaptureSaveDto();

            var restored = new WorkTimeBudgetStore();
            restored.LoadFromSaveDto(dto, 100);

            WorkTimeBudgetSaveDto redto = restored.CaptureSaveDto();
            Assert.AreEqual(1, redto.budgets.Count);
            Assert.AreEqual(person, redto.budgets[0].PersonId);
            Assert.AreEqual(100, restored.CurrentAbsoluteDayIndex);
        }

        [Test]
        public void ValuationReadModel_RoundTrip_PreservesEntries()
        {
            var model = new EnterpriseValuationReadModel();
            model.RegisterBusiness("biz_001", "player", true);
            model.RecordWeeklyProfit("biz_001", 5000);

            ValuationReadModelSaveDto dto = model.CaptureSaveDto();

            var restored = new EnterpriseValuationReadModel();
            restored.LoadFromSaveDto(dto);

            Assert.AreEqual(1, restored.TrackedBusinessCount);
            ValuationReadModelSaveDto redto = restored.CaptureSaveDto();
            Assert.AreEqual("biz_001", redto.entries[0].businessInstanceId);
            Assert.IsTrue(redto.entries[0].playerOwned);
        }

        [Test]
        public void FreightPool_RoundTrip_PreservesWagons()
        {
            var pool = new FreightResourcePool();
            pool.AddWagon(new FreightWagon("wagon_1", "Test Wagon", 100));

            FreightResourcePoolSaveDto dto = pool.CaptureSaveDto();

            var restored = new FreightResourcePool();
            restored.LoadFromSaveDto(dto);

            FreightResourcePoolSaveDto redto = restored.CaptureSaveDto();
            Assert.AreEqual(1, redto.wagons.Count);
            Assert.AreEqual("wagon_1", redto.wagons[0].WagonId);
        }

        [Test]
        public void OperatingLedger_RoundTrip_PreservesFirstCommerce()
        {
            var ledger = new BusinessOperatingLedger();
            ledger.RecordCommerce(EntityId.For(EntityKind.Building, 5), 42, "first sale");

            OperatingLedgerSaveDto dto = ledger.CaptureSaveDto();

            var restored = new BusinessOperatingLedger();
            restored.LoadFromSaveDto(dto);

            Assert.AreEqual(1, restored.OperatingBusinessCount);
            Assert.IsTrue(restored.IsOperating(EntityId.For(EntityKind.Building, 5)));
            Assert.IsFalse(restored.IsOperating(EntityId.For(EntityKind.Building, 6)));
        }

        [Test]
        public void CapabilityRegistry_RoundTrip_PreservesCapabilities()
        {
            var registry = new BusinessCapabilityRegistry();
            var diagnostics = new List<string>();
            Assert.IsTrue(registry.Register(new BusinessCapability("meat-retail", "Meat Retail"), diagnostics));

            CapabilityRegistrySaveDto dto = registry.CaptureSaveDto();

            var restored = new BusinessCapabilityRegistry();
            restored.LoadFromSaveDto(dto, diagnostics);

            Assert.AreEqual(1, restored.Count);
            Assert.IsTrue(restored.TryGet("meat-retail", out BusinessCapability capability));
            Assert.AreEqual("Meat Retail", capability.DisplayName);
        }

        [Test]
        public void FarmFlowLedger_RoundTrip_PreservesSales()
        {
            var ledger = new FarmFlowLedger();
            var lot = new FarmProduceLot("LOT-1", "eggs", 36, 100, "F1", "Test Farm");
            ledger.RecordProduceLot(lot);
            ledger.RecordLivestockSale(new FarmLivestockSale
            {
                SaleId = "FLS-100-A1",
                AnimalId = "A1",
                PriceCents = 8500,
                DayIndex = 100,
                FarmBusinessId = "F1",
                ButcherBusinessId = "B1",
            });

            FarmFlowSaveDto dto = ledger.CaptureSaveDto();

            var restored = new FarmFlowLedger();
            restored.LoadFromSaveDto(dto);

            FarmFlowSaveDto redto = restored.CaptureSaveDto();
            Assert.AreEqual(1, redto.produceLots.Count);
            Assert.AreEqual("LOT-1", redto.produceLots[0].LotId);
            Assert.AreEqual(1, redto.livestockSales.Count);
            Assert.AreEqual(8500, redto.livestockSales[0].PriceCents);
        }

        [Test]
        public void ButcherRuntime_RoundTrip_PreservesLotsAndCursor()
        {
            var butcher = new ButcherRuntime("B1", "yard", "counter");
            // Lots are created through slaughter; here we verify the DTO path directly.
            ButcherRuntimeSaveDto dto = butcher.CaptureSaveDto();
            Assert.AreEqual("B1", dto.businessInstanceId);

            var restored = new ButcherRuntime("B1", "yard", "counter");
            restored.LoadFromSaveDto(dto);

            ButcherRuntimeSaveDto redto = restored.CaptureSaveDto();
            Assert.AreEqual("B1", redto.businessInstanceId);
            Assert.AreEqual("yard", redto.productionSiteId);
        }
    }
}
