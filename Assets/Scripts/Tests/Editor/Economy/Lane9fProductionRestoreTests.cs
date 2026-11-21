// Copyright (c) Land & Ledgers contributors. Licensed under the MIT License.
// Lane 9f (F R-2, expanded campaign 2026-11-20): in-progress furniture
// production must survive Save/Continue — restored or resumed, never
// silently lost. The process state itself already round-trips through
// the BusinessInstanceState DTO (proven by Pass 12's Unity test for a
// mid-bake passive phase); what was never proven is the ACTIVE phase:
// the phase WorkTask was created before the save, the bridge's session-only
// phase-task map is gone after Continue, and the rebind must find the
// persisted task instead of minting a duplicate — or stranding the process
// on a task that can never work again. These tests drive the real bed
// recipe (300 person-minutes + 120 passive cure) through the real runtime
// and the real TaskAuthority DTO, with a fresh runtime standing in for the
// post-Continue session.

using System.Collections.Generic;
using NUnit.Framework;
using LandLedgers.Economy;
using LandLedgers.Economy.GenericBusiness;
using LandLedgers.Primitives;
using LandLedgers.Persistence;
using LandLedgers.Tasks;
using LandLedgers.Time;
using UnityEngine;

namespace LandLedgers.Editor.Economy
{
    [TestFixture]
    public class Lane9fProductionRestoreTests
    {
        private TaskAuthority tasks;
        private WorkTimeBudgetStore budgets;
        private EntityId businessId;
        private EntityId carpenter;
        private ProductionMethodDefinition method;
        private Dictionary<string, float> inventory;
        private Dictionary<string, int> consumeCounts;
        private int outputsCreated;

        [SetUp]
        public void SetUp()
        {
            tasks = new TaskAuthority();
            budgets = new WorkTimeBudgetStore();
            budgets.EnsureDay(1);
            businessId = EntityId.For(EntityKind.Business, 900);
            carpenter = EntityId.For(EntityKind.Person, 900);
            method = FurnitureRecipePresets.Bed().ToMethodDefinition();
            inventory = new Dictionary<string, float>
            {
                { "lumber", 10f },
                { "nails_hardware_lot", 5f },
                { "basic_cloth", 10f },
                { "hay", 5f },
            };
            consumeCounts = new Dictionary<string, int>();
            outputsCreated = 0;
        }

        private static string PhaseDefinitionId(ProductionProcessState process, int phaseIndex)
        {
            return $"generic-production:{process.ProcessId}:phase:{phaseIndex}:definition";
        }

        private static int CountTasksWithDefinition(TaskAuthority authority, string definitionId)
        {
            int count = 0;
            foreach (WorkTask task in authority.CaptureSaveDto().tasks)
            {
                if (task != null && string.Equals(task.DefinitionId, definitionId, System.StringComparison.Ordinal)) count++;
            }
            return count;
        }

        private ProductionProcessState StartBed(GenericProductionRuntime runtime)
        {
            var policy = new GenericProductionPolicy
            {
                MethodId = method.MethodId,
                Enabled = true,
                Mode = GenericProductionPolicyMode.MakeToStock,
                TargetStock = 1f,
                MaximumStock = 1f,
                BatchSize = 1f,
            };
            var plan = new GenericProductionPlan { Policy = policy, Method = method, Quantity = 1f };
            bool started = runtime.TryBegin(plan, carpenter,
                _ => true, _ => true, _ => true, _ => true,
                input =>
                {
                    if (input == null || !inventory.TryGetValue(input.ProductId, out float onHand) || onHand < input.Quantity) return false;
                    inventory[input.ProductId] = onHand - input.Quantity;
                    consumeCounts.TryGetValue(input.ProductId, out int count);
                    consumeCounts[input.ProductId] = count + 1;
                    return true;
                },
                out ProductionProcessState process, out string reason);
            Assert.IsTrue(started, reason);
            Assert.IsNotNull(process);
            return process;
        }

        private GenericProductionRuntime NewRuntime(TaskAuthority authority, WorkTimeBudgetStore store, int dayIndex)
        {
            var runtime = new GenericProductionRuntime(authority, store, businessId, dayIndex);
            runtime.RegisterMethod(method);
            return runtime;
        }

        private void AdvanceToCompletion(GenericProductionRuntime runtime, ProductionProcessState process)
        {
            for (int i = 0; i < 8 && !process.Completed; i++)
            {
                runtime.Advance(process, carpenter, 24 * 60,
                    () => { outputsCreated++; return true; }, out _);
            }
        }

        // --- The implicit rebind (no explicit restore call) ---

        [Test]
        public void ActivePhase_ImplicitRebind_ResumesWithoutDuplicateTask()
        {
            GenericProductionRuntime runtime = NewRuntime(tasks, budgets, 1);
            ProductionProcessState process = StartBed(runtime);
            Assert.AreEqual(8f, inventory["lumber"], "lumber is consumed once at start");
            Assert.AreEqual(4f, inventory["nails_hardware_lot"]);
            Assert.AreEqual(6f, inventory["basic_cloth"]);
            Assert.AreEqual(4f, inventory["hay"]);

            // Work 10 of the 60-minute cut phase, then save mid-phase.
            // Partial minutes live on the task, not the process: the process
            // advances a whole phase only when its task completes.
            Assert.IsTrue(runtime.Advance(process, carpenter, 10,
                () => { outputsCreated++; return true; }, out string advanceReason), advanceReason);
            Assert.AreEqual(0, process.CurrentPhaseIndex);
            Assert.AreEqual(60, process.RemainingPhaseMinutes, "the process still owes the whole cut phase until its task completes");
            WorkTask phaseTask = tasks.FindOpenTask(PhaseDefinitionId(process, 0), businessId, carpenter);
            Assert.IsNotNull(phaseTask, "the cut phase has a live task before the save");
            Assert.AreEqual(TaskStatus.InProgress, phaseTask.Status);
            Assert.AreEqual(50, phaseTask.MinutesRemaining, "the 10 worked minutes live on the task");
            Assert.AreEqual(1, CountTasksWithDefinition(tasks, PhaseDefinitionId(process, 0)));

            // Continue: fresh authority from the save DTO, fresh runtime —
            // the bridge's session-only phase-task map is gone.
            var savedTasks = tasks.CaptureSaveDto();
            var restoredTasks = new TaskAuthority();
            restoredTasks.LoadFromSaveDto(savedTasks);
            var restoredBudgets = new WorkTimeBudgetStore();
            restoredBudgets.EnsureDay(2);
            GenericProductionRuntime restoredRuntime = NewRuntime(restoredTasks, restoredBudgets, 2);

            AdvanceToCompletion(restoredRuntime, process);

            Assert.IsTrue(process.Completed, "the restored process finishes");
            Assert.AreEqual(1, outputsCreated, "exactly one bed is created");
            Assert.AreEqual(1, consumeCounts["lumber"], "lumber is not re-consumed after Continue");
            Assert.AreEqual(1, consumeCounts["nails_hardware_lot"]);
            Assert.AreEqual(1, consumeCounts["basic_cloth"]);
            Assert.AreEqual(1, consumeCounts["hay"]);
            Assert.AreEqual(1, CountTasksWithDefinition(restoredTasks, PhaseDefinitionId(process, 0)),
                "the rebind adopts the persisted cut task — no duplicate task is minted for the same phase");
        }

        [Test]
        public void FailedPhaseTask_DoesNotStrandProcessAfterContinue()
        {
            GenericProductionRuntime runtime = NewRuntime(tasks, budgets, 1);
            ProductionProcessState process = StartBed(runtime);
            Assert.IsTrue(runtime.Advance(process, carpenter, 10,
                () => { outputsCreated++; return true; }, out _));
            WorkTask phaseTask = tasks.FindOpenTask(PhaseDefinitionId(process, 0), businessId, carpenter);
            Assert.IsNotNull(phaseTask);
            Assert.IsTrue(tasks.CompleteTask(phaseTask.TaskId, TaskOutcome.RecoverableFailure, 1, budgets),
                "the cut phase task fails before the save");

            // Continue: fresh authority from the save DTO, fresh runtime.
            var savedTasks = tasks.CaptureSaveDto();
            var restoredTasks = new TaskAuthority();
            restoredTasks.LoadFromSaveDto(savedTasks);
            var restoredBudgets = new WorkTimeBudgetStore();
            restoredBudgets.EnsureDay(2);
            GenericProductionRuntime restoredRuntime = NewRuntime(restoredTasks, restoredBudgets, 2);

            AdvanceToCompletion(restoredRuntime, process);

            Assert.IsTrue(process.Completed,
                "a failed phase task must not strand the process — the rebind skips a terminal task and a fresh task works the phase");
            Assert.AreEqual(1, outputsCreated, "exactly one bed is created after the retry");
            Assert.AreEqual(1, consumeCounts["lumber"], "the retry does not re-consume inputs");
        }

        // --- The explicit restore step ---

        [Test]
        public void RestoreProcess_BindsPersistedTaskWithoutWork()
        {
            GenericProductionRuntime runtime = NewRuntime(tasks, budgets, 1);
            ProductionProcessState process = StartBed(runtime);
            Assert.IsTrue(runtime.Advance(process, carpenter, 10,
                () => { outputsCreated++; return true; }, out _));
            WorkTask before = tasks.FindOpenTask(PhaseDefinitionId(process, 0), businessId, carpenter);
            Assert.IsNotNull(before);
            Assert.AreEqual(50, before.MinutesRemaining);

            var savedTasks = tasks.CaptureSaveDto();
            var restoredTasks = new TaskAuthority();
            restoredTasks.LoadFromSaveDto(savedTasks);
            var restoredBudgets = new WorkTimeBudgetStore();
            restoredBudgets.EnsureDay(2);
            GenericProductionRuntime restoredRuntime = NewRuntime(restoredTasks, restoredBudgets, 2);

            Assert.IsTrue(restoredRuntime.RestoreProcess(process, carpenter, out string reason), reason);
            WorkTask rebound = restoredTasks.FindOpenTask(PhaseDefinitionId(process, 0), businessId, carpenter);
            Assert.IsNotNull(rebound, "the restore binds the persisted task");
            Assert.AreEqual(before.TaskId, rebound.TaskId, "the SAME task is rebound — no duplicate is minted");
            Assert.AreEqual(50, rebound.MinutesRemaining, "restore accrues no work");
            Assert.AreEqual(0, process.CurrentPhaseIndex, "restore advances nothing");
            Assert.AreEqual(60, process.RemainingPhaseMinutes);
            Assert.AreEqual(1, CountTasksWithDefinition(restoredTasks, PhaseDefinitionId(process, 0)));

            AdvanceToCompletion(restoredRuntime, process);
            Assert.IsTrue(process.Completed);
            Assert.AreEqual(1, outputsCreated);
        }

        [Test]
        public void RestoreProcess_MissingMethod_RefusesLoudly_AndKeepsProcess()
        {
            GenericProductionRuntime runtime = NewRuntime(tasks, budgets, 1);
            ProductionProcessState process = StartBed(runtime);
            Assert.IsTrue(runtime.Advance(process, carpenter, 10,
                () => { outputsCreated++; return true; }, out _));

            var savedTasks = tasks.CaptureSaveDto();
            var restoredTasks = new TaskAuthority();
            restoredTasks.LoadFromSaveDto(savedTasks);
            var restoredBudgets = new WorkTimeBudgetStore();
            restoredBudgets.EnsureDay(2);
            // Deliberately no RegisterMethod: the method is gone after Continue.
            var restoredRuntime = new GenericProductionRuntime(restoredTasks, restoredBudgets, businessId, 2);

            Assert.IsFalse(restoredRuntime.RestoreProcess(process, carpenter, out string reason));
            Assert.IsFalse(string.IsNullOrWhiteSpace(reason), "the refusal names the problem");
            Assert.IsTrue(reason.Contains(method.MethodId), "the refusal names the missing method");
            Assert.IsFalse(process.Completed, "the process is kept, not dropped");

            // Once the method is registered again, the same process rebinds.
            restoredRuntime.RegisterMethod(method);
            Assert.IsTrue(restoredRuntime.RestoreProcess(process, carpenter, out _));
            AdvanceToCompletion(restoredRuntime, process);
            Assert.IsTrue(process.Completed);
            Assert.AreEqual(1, outputsCreated);
        }

        [Test]
        public void RestoreProcess_PassivePhase_NeedsNoTask()
        {
            GenericProductionRuntime runtime = NewRuntime(tasks, budgets, 1);
            ProductionProcessState process = StartBed(runtime);
            // Jump the process to its passive cure phase (index 3): the
            // person phases are done, the piece cures in the shop.
            process.CurrentPhaseIndex = 3;
            process.RemainingPhaseMinutes = 120;
            Assert.IsFalse(process.PersonRequiredNow(method));

            var savedTasks = tasks.CaptureSaveDto();
            var restoredTasks = new TaskAuthority();
            restoredTasks.LoadFromSaveDto(savedTasks);
            var restoredBudgets = new WorkTimeBudgetStore();
            restoredBudgets.EnsureDay(2);
            GenericProductionRuntime restoredRuntime = NewRuntime(restoredTasks, restoredBudgets, 2);

            Assert.IsTrue(restoredRuntime.RestoreProcess(process, carpenter, out string reason), reason);
            Assert.AreEqual(0, CountTasksWithDefinition(restoredTasks, PhaseDefinitionId(process, 3)),
                "a passive phase needs no task — restore mints none");

            AdvanceToCompletion(restoredRuntime, process);
            Assert.IsTrue(process.Completed);
            Assert.AreEqual(1, outputsCreated);
        }

        // --- The registry step (stub business + real configuration) ---

        [Test]
        public void RestoreProcessesForBusiness_RestoresAndAdvancesToCompletion()
        {
            var config = new GenericBusinessConfiguration();
            config.AddProductionMethod(method);
            config.AddOrReplaceProductionPolicy(new GenericProductionPolicy
            {
                MethodId = method.MethodId,
                Enabled = true,
                Mode = GenericProductionPolicyMode.MakeToStock,
                TargetStock = 1f,
                MaximumStock = 1f,
                BatchSize = 1f,
            });
            config.AddInventory("lumber", 10f);
            config.AddInventory("nails_hardware_lot", 5f);
            config.AddInventory("basic_cloth", 10f);
            config.AddInventory("hay", 5f);
            var business = LandLedgers.Economy.BusinessInstanceState.FromSaveDto(new BusinessInstanceSaveDto
            {
                instanceId = "carpentry-workshop-restore-test",
                businessType = LandLedgers.Economy.BusinessType.Generic,
                generic = config.CaptureSaveDto(),
            });
            var registry = new GenericProductionRuntimeRegistry(tasks, budgets);
            GenericProductionRuntime runtime = registry.ForBusiness(business, businessId, 1);
            var outputDefinition = new GenericProductDefinition("furniture.bed", "Bed", ProductQuantityUnit.Each);
            Assert.IsTrue(GenericProductionPolicyExecutor.TryStart(business, runtime, method.MethodId,
                carpenter, outputDefinition, 1, out ProductionProcessState process, out string startReason,
                _ => true, _ => true), startReason);
            Assert.AreEqual(8f, business.GenericConfiguration.GetInventoryQuantity("lumber"), "lumber is consumed once at start");

            Assert.IsTrue(runtime.Advance(process, carpenter, 10,
                () => { outputsCreated++; return true; }, out _), "10 minutes into the cut phase");

            // Continue through the same business DTO and JSON serialization
            // boundary used by game saves. The previous fixture manually
            // copied selected process fields, which could pass while the
            // production save DTO silently omitted one of them.
            string businessJson = JsonUtility.ToJson(business.CaptureSaveDto());
            BusinessInstanceSaveDto restoredBusinessDto = JsonUtility.FromJson<BusinessInstanceSaveDto>(businessJson);
            LandLedgers.Economy.BusinessInstanceState restoredBusiness =
                LandLedgers.Economy.BusinessInstanceState.FromSaveDto(restoredBusinessDto);
            Assert.NotNull(restoredBusiness);
            ProductionProcessState rebuiltProcess = null;
            foreach (ProductionProcessState candidate in restoredBusiness.GenericConfiguration.ActiveProcesses)
                if (candidate != null && candidate.ProcessId == process.ProcessId) { rebuiltProcess = candidate; break; }
            Assert.NotNull(rebuiltProcess, "the actual business save DTO retains the in-progress furniture batch");
            Assert.AreEqual(process.ProcessId, rebuiltProcess.ProcessId);
            Assert.AreEqual(process.CurrentPhaseIndex, rebuiltProcess.CurrentPhaseIndex);
            Assert.AreEqual(process.RemainingPhaseMinutes, rebuiltProcess.RemainingPhaseMinutes);
            CollectionAssert.AreEqual(process.ConsumedInputIds, rebuiltProcess.ConsumedInputIds,
                "consumed-input identity is durable, preventing a second material debit");
            CollectionAssert.AreEqual(process.ConsumedInputLotIds, rebuiltProcess.ConsumedInputLotIds,
                "material provenance remains attached to the batch");
            Assert.AreEqual(8f, restoredBusiness.GenericConfiguration.GetInventoryQuantity("lumber"));
            Assert.AreEqual(4f, restoredBusiness.GenericConfiguration.GetInventoryQuantity("nails_hardware_lot"));
            Assert.AreEqual(6f, restoredBusiness.GenericConfiguration.GetInventoryQuantity("basic_cloth"));

            // Continue also restores TaskAuthority independently. The new
            // runtime must rebind the persisted cut task instead of creating
            // a duplicate or consuming the saved inputs again.
            var savedTasks = tasks.CaptureSaveDto();
            var restoredTasks = new TaskAuthority();
            restoredTasks.LoadFromSaveDto(savedTasks);
            var restoredBudgets = new WorkTimeBudgetStore();
            restoredBudgets.EnsureDay(2);
            var restoredRegistry = new GenericProductionRuntimeRegistry(restoredTasks, restoredBudgets);

            int restoredCount = restoredRegistry.RestoreProcessesForBusiness(
                restoredBusiness, businessId, 2, out List<string> diagnostics);
            Assert.AreEqual(1, restoredCount, "the one incomplete process is rebound");
            Assert.AreEqual(0, diagnostics.Count, "no refusal — a healthy restore is silent: " + string.Join("; ", diagnostics));

            for (int i = 0; i < 8 && !rebuiltProcess.Completed; i++)
                restoredRegistry.AdvanceBusinessProcesses(restoredBusiness, businessId, 2, 24 * 60);
            Assert.IsTrue(rebuiltProcess.Completed, "the restored process finishes through the registry");
            Assert.AreEqual(1f, restoredBusiness.GenericConfiguration.GetInventoryQuantity("furniture.bed"), "exactly one bed is created");
            Assert.AreEqual(8f, restoredBusiness.GenericConfiguration.GetInventoryQuantity("lumber"), "lumber is not re-consumed after Continue");
            Assert.IsTrue(rebuiltProcess.OutputCreated, "the output guard itself survives in the real save DTO");
            Assert.AreEqual(1, CountTasksWithDefinition(restoredTasks, PhaseDefinitionId(rebuiltProcess, 0)),
                "no duplicate cut task is minted for the restored process");
        }
    }
}
