using System.Collections.Generic;
using LandLedgers.Population;
using LandLedgers.Primitives;
using LandLedgers.Tasks;
using NUnit.Framework;

namespace LandLedgers.Editor.Population
{
    /// <summary>
    /// Pass IV (Worker 4, Phase O): autonomous work generation from legitimate
    /// needs. Proves:
    /// - Received goods → handling task; shelf below target + real stock →
    ///   restock task; empty shelf with NO stock → refusal (procurement signal,
    ///   never a phantom task).
    /// - Repair requests → work orders at the requested priority; failed
    ///   equipment → a real maintenance decision (repair/replace/defer) + work
    ///   order; destroyed equipment → replace decision, no work order.
    /// - Farm window (right days + good weather) → field task; bad weather →
    ///   none.
    /// - Unmet production policy + feasible → work order; infeasible → loud
    ///   refusal.
    /// - No quotas: with no triggers, nothing is generated. Dedupe: an already
    ///   open task for the same trigger is not duplicated.
    /// - Household low days-of-supply → one real purchasing need per item.
    /// </summary>
    [TestFixture]
    public sealed class AutonomousWorkGeneratorTests
    {
        private sealed class Fixture
        {
            public readonly TaskAuthority Tasks = new TaskAuthority();
            public readonly AutonomousWorkGenerator Generator = new AutonomousWorkGenerator();
            public readonly HouseholdNeedRegistry Needs = new HouseholdNeedRegistry();
            public readonly EntityId BusinessOwner = EntityId.For(EntityKind.Business, 9001);

            private readonly HashSet<string> openKeys = new HashSet<string>();

            public Fixture()
            {
                var diag = new List<string>();
                AutonomousWorkGenerator.GeneratedWorkCatalog.RegisterAll(Tasks, diag);
            }

            public AutonomousWorkGenerator.BusinessGenerationInputs BusinessInputs(int day = 100)
            {
                return new AutonomousWorkGenerator.BusinessGenerationInputs
                {
                    BusinessInstanceId = "bakery-1",
                    BusinessOwner = BusinessOwner,
                    DayIndex = day,
                    StockUnits = _ => 0,
                    HasOpenTask = (def, cref) => openKeys.Contains(def + "|" + cref),
                };
            }

            public List<WorkTask> OpenTasks()
            {
                return Tasks.GetOpenTasks(BusinessOwner);
            }
        }

        [Test]
        public void NoTriggers_NothingGenerated_NoQuotas()
        {
            var f = new Fixture();
            var diag = new List<string>();

            AutonomousWorkGenerator.GenerationReport report =
                f.Generator.GenerateForBusiness(f.BusinessInputs(), f.Tasks, diag);

            Assert.AreEqual(0, report.CreatedTaskIds.Count);
            Assert.AreEqual(0, report.Refusals.Count);
            Assert.AreEqual(0, f.OpenTasks().Count);
        }

        [Test]
        public void ReceivedGoods_CreatesHandlingTask_Once()
        {
            var f = new Fixture();
            var inputs = f.BusinessInputs();
            inputs.ReceivedGoods.Add(new AutonomousWorkGenerator.ReceivedGoods
            {
                ItemId = "flour",
                Units = 50,
                LocationId = "bakery",
                Provenance = "mill delivery",
            });

            AutonomousWorkGenerator.GenerationReport first =
                f.Generator.GenerateForBusiness(inputs, f.Tasks, null);
            Assert.AreEqual(1, first.CreatedTaskIds.Count);

            // Second run with the trigger still present: the open task dedupes.
            inputs.HasOpenTask = (def, cref) => true;
            AutonomousWorkGenerator.GenerationReport second =
                f.Generator.GenerateForBusiness(inputs, f.Tasks, null);
            Assert.AreEqual(0, second.CreatedTaskIds.Count, "must not duplicate an open handling task");
        }

        [Test]
        public void ShelfBelowTarget_WithStock_CreatesRestock_WithoutStock_Refuses()
        {
            var f = new Fixture();
            var inputs = f.BusinessInputs();
            inputs.Shelves.Add(new AutonomousWorkGenerator.ShelfLine
            {
                ItemId = "bread",
                ShelfUnits = 2,
                TargetUnits = 20,
            });
            inputs.StockUnits = item => item == "bread" ? 30 : 0;

            AutonomousWorkGenerator.GenerationReport report =
                f.Generator.GenerateForBusiness(inputs, f.Tasks, null);

            Assert.AreEqual(1, report.CreatedTaskIds.Count);
            WorkTask task = f.Tasks.FindTask(f.OpenTasks()[0].TaskId);
            Assert.AreEqual(AutonomousWorkGenerator.GeneratedWorkCatalog.RestockShelvesId, task.DefinitionId);

            // Same shelf, no stock anywhere: refusal, never a phantom task.
            var f2 = new Fixture();
            var inputs2 = f2.BusinessInputs();
            inputs2.Shelves.Add(new AutonomousWorkGenerator.ShelfLine { ItemId = "bread", ShelfUnits = 2, TargetUnits = 20 });
            inputs2.StockUnits = _ => 0;
            AutonomousWorkGenerator.GenerationReport report2 =
                f2.Generator.GenerateForBusiness(inputs2, f2.Tasks, null);

            Assert.AreEqual(0, report2.CreatedTaskIds.Count);
            Assert.AreEqual(1, report2.Refusals.Count);
            Assert.IsTrue(report2.Refusals[0].Contains("procurement signal"));
        }

        [Test]
        public void RepairRequest_CreatesWorkOrder_AtRequestedPriority()
        {
            var f = new Fixture();
            var inputs = f.BusinessInputs();
            inputs.RepairRequests.Add(new AutonomousWorkGenerator.RepairRequest
            {
                RequestId = "req-9",
                EquipmentId = "oven-1",
                Description = "door hinge broken",
                Priority = TaskPriority.Urgent,
                DayIndex = 100,
            });

            AutonomousWorkGenerator.GenerationReport report =
                f.Generator.GenerateForBusiness(inputs, f.Tasks, null);

            Assert.AreEqual(1, report.CreatedTaskIds.Count);
            Assert.AreEqual(TaskPriority.Urgent, f.OpenTasks()[0].Priority);
            Assert.IsTrue(inputs.RepairRequests[0].Fulfilled);
        }

        [Test]
        public void FailedEquipment_MaintenanceDecision_AndDestroyedMeansReplace()
        {
            var f = new Fixture();
            var inputs = f.BusinessInputs();
            inputs.Equipment.Add(new AutonomousWorkGenerator.EquipmentCondition
            {
                EquipmentId = "plow-1",
                OwnerBusinessId = "bakery-1",
                Condition01 = 0.1f,
                Failed = true,
                LocationId = "barn",
            });
            inputs.Equipment.Add(new AutonomousWorkGenerator.EquipmentCondition
            {
                EquipmentId = "anvil-9",
                OwnerBusinessId = "bakery-1",
                Condition01 = 0.0f,
                Failed = true,
            });

            AutonomousWorkGenerator.GenerationReport report =
                f.Generator.GenerateForBusiness(inputs, f.Tasks, null);

            Assert.AreEqual(2, report.MaintenanceDecisions.Count);
            AutonomousWorkGenerator.MaintenanceDecision plow =
                report.MaintenanceDecisions.Find(d => d.EquipmentId == "plow-1");
            AutonomousWorkGenerator.MaintenanceDecision anvil =
                report.MaintenanceDecisions.Find(d => d.EquipmentId == "anvil-9");
            Assert.AreEqual("Repair", plow.Decision);
            Assert.IsFalse(string.IsNullOrEmpty(plow.WorkOrderTaskId), "repair needs a work order");
            Assert.AreEqual("Replace", anvil.Decision);
            Assert.IsTrue(string.IsNullOrEmpty(anvil.WorkOrderTaskId), "destroyed equipment gets no work order");
            Assert.AreEqual(1, report.CreatedTaskIds.Count, "only the plow repair is a task");
        }

        [Test]
        public void FarmWindow_GoodWeather_CreatesFieldTask_BadWeather_None()
        {
            var f = new Fixture();
            var inputs = f.BusinessInputs(day: 105);
            inputs.FarmWindow = new AutonomousWorkGenerator.FarmWorkWindow
            {
                StartDayIndex = 100,
                EndDayIndex = 110,
                WorkKind = "spring plowing",
                FieldLocationId = "field-north",
                WeatherOk = true,
            };

            AutonomousWorkGenerator.GenerationReport report =
                f.Generator.GenerateForBusiness(inputs, f.Tasks, null);
            Assert.AreEqual(1, report.CreatedTaskIds.Count);

            var f2 = new Fixture();
            var inputs2 = f2.BusinessInputs(day: 105);
            inputs2.FarmWindow = new AutonomousWorkGenerator.FarmWorkWindow
            {
                StartDayIndex = 100,
                EndDayIndex = 110,
                WorkKind = "spring plowing",
                FieldLocationId = "field-north",
                WeatherOk = false,
            };
            AutonomousWorkGenerator.GenerationReport report2 =
                f2.Generator.GenerateForBusiness(inputs2, f2.Tasks, null);
            Assert.AreEqual(0, report2.CreatedTaskIds.Count, "bad weather: no field task");
        }

        [Test]
        public void ProductionPolicyUnmet_Feasible_CreatesOrder_Infeasible_Refuses()
        {
            var f = new Fixture();
            var inputs = f.BusinessInputs();
            inputs.ProductionPolicy = new AutonomousWorkGenerator.ManagerProductionPolicy
            {
                ItemId = "bread",
                TargetUnitsPerDay = 100,
                ActualUnitsPerDay = 40,
            };
            inputs.CheckFeasibility = _ => new AutonomousWorkGenerator.WorkFeasibility
            {
                Feasible = true,
                Reason = "2 bakers free, flour on hand",
                AvailableWorkerMinutes = 480,
                MaterialsAvailable = true,
            };

            AutonomousWorkGenerator.GenerationReport report =
                f.Generator.GenerateForBusiness(inputs, f.Tasks, null);
            Assert.AreEqual(1, report.CreatedTaskIds.Count);

            var f2 = new Fixture();
            var inputs2 = f2.BusinessInputs();
            inputs2.ProductionPolicy = new AutonomousWorkGenerator.ManagerProductionPolicy
            {
                ItemId = "bread",
                TargetUnitsPerDay = 100,
                ActualUnitsPerDay = 40,
            };
            inputs2.CheckFeasibility = _ => new AutonomousWorkGenerator.WorkFeasibility
            {
                Feasible = false,
                Reason = "no flour on hand",
                AvailableWorkerMinutes = 480,
                MaterialsAvailable = false,
            };
            AutonomousWorkGenerator.GenerationReport report2 =
                f2.Generator.GenerateForBusiness(inputs2, f2.Tasks, null);
            Assert.AreEqual(0, report2.CreatedTaskIds.Count);
            Assert.AreEqual(1, report2.Refusals.Count);
            Assert.IsTrue(report2.Refusals[0].Contains("no phantom task"));
        }

        [Test]
        public void HouseholdLowSupply_OpensOnePurchasingNeedPerItem()
        {
            var f = new Fixture();
            var diag = new List<string>();
            var inputs = new AutonomousWorkGenerator.HouseholdGenerationInputs
            {
                HouseholdId = 7,
                HouseholdOwner = EntityId.For(EntityKind.Household, 7),
                Needs = f.Needs,
                DayIndex = 100,
                LowDaysThreshold = 7f,
                DaysOfSupplyByItem = new Dictionary<string, float>
                {
                    { "flour", 3f },
                    { "salt", 30f },
                },
            };

            AutonomousWorkGenerator.GenerationReport report = f.Generator.GenerateForHousehold(inputs, diag);

            Assert.AreEqual(1, report.CreatedNeedKeys.Count);
            Assert.AreEqual("7:flour", report.CreatedNeedKeys[0]);
            HouseholdPurchasingNeed need = f.Needs.GetOpenNeed(7, "flour");
            Assert.IsNotNull(need);
            Assert.IsTrue(need.IsNecessity);

            // Second run: the open need dedupes.
            AutonomousWorkGenerator.GenerationReport second = f.Generator.GenerateForHousehold(inputs, diag);
            Assert.AreEqual(0, second.CreatedNeedKeys.Count);
        }
    }
}
