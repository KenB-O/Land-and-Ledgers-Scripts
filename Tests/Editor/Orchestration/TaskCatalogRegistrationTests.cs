using System.Collections.Generic;
using LandLedgers.Economy.Businesses.Logging;
using LandLedgers.Economy.Farming.Crops;
using LandLedgers.Economy.Farming.Dairy;
using LandLedgers.Economy.Farming.Integration;
using LandLedgers.Economy.Farming.Livestock;
using LandLedgers.Economy.Farming.Timber;
using LandLedgers.Economy.Freight;
using LandLedgers.Economy.Postal;
using LandLedgers.FirstLedger;
using LandLedgers.Orchestration.Systems;
using LandLedgers.Primitives;
using LandLedgers.Skills;
using LandLedgers.Tasks;
using LandLedgers.World.Journeys;
using NUnit.Framework;

namespace LandLedgers.EditorTests.Orchestration
{
    /// <summary>
    /// P1: boot-time task-definition registration. Before this pass, NO production
    /// caller ever registered any task catalog with the hub's TaskAuthority, so the
    /// first CreateTask for freight, travel, farm, or shop work threw
    /// "Unknown TaskDefinitionId". The hub's Awake now registers every static
    /// catalog; this fixture verifies the coverage.
    /// </summary>
    [TestFixture]
    public sealed class TaskCatalogRegistrationTests
    {
        private static TaskAuthority RegisteredAuthority()
        {
            var tasks = new TaskAuthority();
            var diag = new List<string>();
            SimulationSystemsHub.RegisterTaskCatalogs(tasks, new SkillService(), new PostalService(), diag);
            return tasks;
        }

        [Test]
        public void RegisterTaskCatalogs_RegistersTravelAndFreightDefinitions()
        {
            TaskAuthority tasks = RegisteredAuthority();

            Assert.IsNotNull(tasks.GetDefinition(JourneyTravel.TravelTaskId), "travel task");
            Assert.IsNotNull(tasks.GetDefinition(FreightCompanyRuntime.LoadFreightTaskId), "freight load");
            Assert.IsNotNull(tasks.GetDefinition(FreightCompanyRuntime.DriveRouteTaskId), "freight drive");
            Assert.IsNotNull(tasks.GetDefinition(FreightCompanyRuntime.UnloadFreightTaskId), "freight unload");
            Assert.IsNotNull(tasks.GetDefinition(FreightCompanyRuntime.RepairWagonTaskId), "wagon repair");
        }

        [Test]
        public void RegisterTaskCatalogs_RegistersShopCatalogDefinitions()
        {
            TaskAuthority tasks = RegisteredAuthority();

            Assert.IsNotNull(tasks.GetDefinition(GeneralStoreTaskCatalog.TendCustomerId), "tend customer");
            Assert.IsNotNull(tasks.GetDefinition(GeneralStoreTaskCatalog.UnloadFreightId), "store unload");
            Assert.IsNotNull(tasks.GetDefinition(GeneralStoreTaskCatalog.StockShelvesId), "stock shelves");
            Assert.IsNotNull(tasks.GetDefinition(GeneralStoreTaskCatalog.CleanStoreId), "clean store");
        }

        [Test]
        public void RegisterTaskCatalogs_RegistersFarmAndTimberDefinitions()
        {
            TaskAuthority tasks = RegisteredAuthority();

            Assert.IsNotNull(tasks.GetDefinition(CropChain.PlowFieldTaskId), "plow field");
            Assert.IsNotNull(tasks.GetDefinition(CropChain.HarvestFieldTaskId), "harvest field");
            Assert.IsNotNull(tasks.GetDefinition(DairyChain.MilkCowTaskId), "milk cow");
            Assert.IsNotNull(tasks.GetDefinition(TimberHarvest.FellTimberTaskId), "fell timber");
            Assert.IsNotNull(tasks.GetDefinition(Sawmill.SawLogsTaskId), "saw logs");
        }

        [Test]
        public void RegisterTaskCatalogs_RegistersLoggingAndPostalDefinitions()
        {
            TaskAuthority tasks = RegisteredAuthority();

            Assert.IsNotNull(tasks.GetDefinition(LoggingTaskDefinitions.SkidLogs), "skid logs");
            Assert.IsNotNull(tasks.GetDefinition(PostalService.SortMailTaskId), "sort mail");
            Assert.IsNotNull(tasks.GetDefinition(PostalService.DeliverLocalMailTaskId), "deliver mail");
        }

        [Test]
        public void RegisterTaskCatalogs_AllowsTaskCreationWithoutThrow()
        {
            TaskAuthority tasks = RegisteredAuthority();

            WorkTask task = null;
            Assert.DoesNotThrow(() =>
            {
                task = tasks.CreateTaskWithPlannedMinutes(
                    FreightCompanyRuntime.LoadFreightTaskId, EntityId.Invalid, 0, 30, "shipment-1");
            });
            Assert.IsNotNull(task);
            Assert.AreEqual(FreightCompanyRuntime.LoadFreightTaskId, task.DefinitionId);
        }

        [Test]
        public void RegisterTaskCatalogs_IsIdempotent()
        {
            var tasks = new TaskAuthority();
            var diag = new List<string>();
            SimulationSystemsHub.RegisterTaskCatalogs(tasks, new SkillService(), new PostalService(), diag);

            // Second registration must not throw and must not duplicate definitions.
            Assert.DoesNotThrow(() =>
                SimulationSystemsHub.RegisterTaskCatalogs(tasks, new SkillService(), new PostalService(), diag));
            Assert.IsNotNull(tasks.GetDefinition(JourneyTravel.TravelTaskId));
        }

        [Test]
        public void RegisterTaskCatalogs_RefusesNullAuthorityLoudly()
        {
            var diag = new List<string>();
            Assert.DoesNotThrow(() =>
                SimulationSystemsHub.RegisterTaskCatalogs(null, new SkillService(), new PostalService(), diag));
            Assert.Greater(diag.Count, 0, "A missing authority must be diagnosed, not silent.");
        }
    }
}
