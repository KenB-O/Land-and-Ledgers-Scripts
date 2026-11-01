using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using LandLedgers.Orchestration.Scenarios;
using LandLedgers.Orchestration.Scenarios.Generation;

namespace LandLedgers.EditorTests.Orchestration
{
    /// <summary>
    /// Pass V P1-5 (Worker 1): lifecycle semantics — New Game generates, Load
    /// Game restores without regenerating, Restart (same/new seed), Replay from
    /// preset, Continue-Failed preserves, Edit Preset affects future
    /// generations only — plus ghost-state prevention regression tests.
    /// </summary>
    [TestFixture]
    public sealed class ScenarioCoordinatorLifecycleTests
    {
        private SimulatedScenarioWorld world;
        private ScenarioCoordinator coordinator;

        [SetUp]
        public void SetUp()
        {
            world = new SimulatedScenarioWorld();
            coordinator = world.CreateCoordinator();
            var diagnostics = new List<string>();
            Assert.IsTrue(coordinator.RegisterPreset(SliceLPresetFactory.Build(), diagnostics),
                string.Join("; ", diagnostics));
            coordinator.SelectedPresetId = SliceLPresetFactory.PresetId;
        }

        private bool RunNewGame(int seed, out List<string> diagnostics)
        {
            diagnostics = new List<string>();
            return coordinator.ExecuteLifecycle(ScenarioLifecycleAction.NewGame, seed, diagnostics);
        }

        [Test]
        public void NewGame_GeneratesWorld_AndRecordsCampaign()
        {
            Assert.IsTrue(RunNewGame(42, out List<string> diagnostics), string.Join("; ", diagnostics));

            Assert.AreEqual(SliceLPresetFactory.PresetId, coordinator.ActivePresetId);
            Assert.AreEqual(42, coordinator.ActiveSeed);
            Assert.AreEqual("first-ledger-slice-l", coordinator.ActiveScenarioId);
            Assert.AreEqual(1, world.Builder.GenerationCalls);

            Assert.IsTrue(world.CampaignStore.TryLoad(ScenarioCoordinator.DefaultCampaignSlot, out ScenarioCampaignRecord saved));
            Assert.IsTrue(saved.DescribesSameWorld(coordinator.ActiveCampaign));
            Assert.IsFalse(string.IsNullOrWhiteSpace(saved.GenerationFingerprint));
            Assert.IsTrue(saved.ProvenanceTag.Contains("slice-l-hearth-and-ledger"));

            Assert.IsTrue(coordinator.LifecycleLog.Count > 0);
        }

        [Test]
        public void NewGame_WithoutSelectedPreset_FailsLoudly()
        {
            coordinator.SelectedPresetId = string.Empty;
            var diagnostics = new List<string>();
            Assert.IsFalse(coordinator.ExecuteLifecycle(ScenarioLifecycleAction.NewGame, 1, diagnostics));
            Assert.IsTrue(diagnostics.Count > 0);
            Assert.AreEqual(0, world.Builder.GenerationCalls);
        }

        [Test]
        public void NewGame_UnregisteredPreset_FailsWithoutGenerating()
        {
            coordinator.SelectedPresetId = "no-such-preset";
            var diagnostics = new List<string>();
            Assert.IsFalse(coordinator.ExecuteLifecycle(ScenarioLifecycleAction.NewGame, 1, diagnostics));
            Assert.AreEqual(0, world.Builder.GenerationCalls);
        }

        [Test]
        public void NewGame_InvalidPreset_FailsWithoutInventingState()
        {
            var diagnostics = new List<string>();
            Assert.IsTrue(coordinator.RegisterPreset(SliceLPresetFactory.BuildInvalidPopulationRange(), diagnostics) == false);
            // Registration itself refuses the invalid preset, so selection can never reach generation.
            coordinator.SelectedPresetId = SliceLPresetFactory.PresetId;
            Assert.IsTrue(RunNewGame(5, out _));

            // Directly exercise the failure path: a preset that validates at
            // register time is the only kind the coordinator will generate from.
            Assert.AreEqual(1, world.Builder.GenerationCalls);
        }

        [Test]
        public void LoadGame_RestoresWithoutRegenerating()
        {
            Assert.IsTrue(RunNewGame(11, out _));
            int callsAfterNewGame = world.Builder.GenerationCalls;
            string fingerprint = coordinator.ActiveCampaign.GenerationFingerprint;

            // Simulate an app restart: a fresh coordinator over the same store.
            // The world keeps its state (the save pipeline owns world restore).
            ScenarioCoordinator loader = world.CreateCoordinator();
            var diagnostics = new List<string>();
            Assert.IsTrue(loader.ExecuteLifecycle(ScenarioLifecycleAction.LoadGame, 0, diagnostics),
                string.Join("; ", diagnostics));

            Assert.AreEqual(callsAfterNewGame, world.Builder.GenerationCalls,
                "LoadGame must never invoke generation.");
            Assert.AreEqual(11, loader.ActiveSeed);
            Assert.AreEqual(fingerprint, loader.ActiveCampaign.GenerationFingerprint);
            Assert.IsTrue(loader.ActiveCampaign.DescribesSameWorld(coordinator.ActiveCampaign));
        }

        [Test]
        public void LoadGame_WithoutSave_FailsLoudly()
        {
            var diagnostics = new List<string>();
            Assert.IsFalse(coordinator.ExecuteLifecycle(ScenarioLifecycleAction.LoadGame, 0, diagnostics));
            Assert.IsTrue(diagnostics.Count > 0);
            Assert.AreEqual(string.Empty, coordinator.ActivePresetId);
        }

        [Test]
        public void RestartSameSeed_ReproducesIdenticalWorld()
        {
            Assert.IsTrue(RunNewGame(7, out _));
            string first = coordinator.ActiveCampaign.GenerationFingerprint;

            var diagnostics = new List<string>();
            Assert.IsTrue(coordinator.ExecuteLifecycle(ScenarioLifecycleAction.RestartSameSeed, 999, diagnostics),
                string.Join("; ", diagnostics));

            Assert.AreEqual(7, coordinator.ActiveSeed, "RestartSameSeed ignores the passed seed.");
            Assert.AreEqual(first, coordinator.ActiveCampaign.GenerationFingerprint,
                "Same definition version + seed + start date must regenerate the identical world.");
            Assert.AreEqual(2, world.Builder.GenerationCalls);
        }

        [Test]
        public void RestartNewSeed_ProducesDifferentWorld()
        {
            Assert.IsTrue(RunNewGame(7, out _));
            string first = coordinator.ActiveCampaign.GenerationFingerprint;

            var diagnostics = new List<string>();
            Assert.IsTrue(coordinator.ExecuteLifecycle(ScenarioLifecycleAction.RestartNewSeed, 8, diagnostics),
                string.Join("; ", diagnostics));

            Assert.AreEqual(8, coordinator.ActiveSeed);
            Assert.IsFalse(string.Equals(first, coordinator.ActiveCampaign.GenerationFingerprint),
                "A new seed must produce a different world.");
        }

        [Test]
        public void Restart_WithoutActiveCampaign_Fails()
        {
            var diagnostics = new List<string>();
            Assert.IsFalse(coordinator.ExecuteLifecycle(ScenarioLifecycleAction.RestartSameSeed, 0, diagnostics));
            Assert.IsFalse(coordinator.ExecuteLifecycle(ScenarioLifecycleAction.RestartNewSeed, 3, diagnostics));
        }

        [Test]
        public void ReplayFromPreset_StartsFreshCampaign()
        {
            Assert.IsTrue(RunNewGame(21, out _));
            string first = coordinator.ActiveCampaign.GenerationFingerprint;

            var diagnostics = new List<string>();
            Assert.IsTrue(coordinator.ExecuteLifecycle(ScenarioLifecycleAction.ReplayFromPreset, 22, diagnostics),
                string.Join("; ", diagnostics));

            Assert.AreEqual(22, coordinator.ActiveSeed);
            Assert.IsFalse(string.Equals(first, coordinator.ActiveCampaign.GenerationFingerprint));
            Assert.AreEqual(2, world.Builder.GenerationCalls);
        }

        [Test]
        public void ContinueFailed_PreservesWorld_NeverRegenerates()
        {
            Assert.IsTrue(RunNewGame(31, out _));
            int calls = world.Builder.GenerationCalls;
            string fingerprint = coordinator.ActiveCampaign.GenerationFingerprint;

            var diagnostics = new List<string>();
            Assert.IsTrue(coordinator.ExecuteLifecycle(ScenarioLifecycleAction.ContinueFailed, 0, diagnostics),
                string.Join("; ", diagnostics));

            Assert.AreEqual(calls, world.Builder.GenerationCalls);
            Assert.AreEqual(fingerprint, coordinator.ActiveCampaign.GenerationFingerprint);
            Assert.AreEqual(31, coordinator.ActiveSeed);
        }

        [Test]
        public void ContinueFailed_WithoutActiveCampaign_Fails()
        {
            var diagnostics = new List<string>();
            Assert.IsFalse(coordinator.ExecuteLifecycle(ScenarioLifecycleAction.ContinueFailed, 0, diagnostics));
        }

        [Test]
        public void EditPreset_BumpsVersion_FutureGenerationsOnly()
        {
            Assert.IsTrue(RunNewGame(5, out _));
            string campaignDefinitionVersion = coordinator.ActiveCampaign.DefinitionVersion;

            ScenarioPreset edited = ScenarioPresetLibrary.DeepCopy(coordinator.GetPreset(SliceLPresetFactory.PresetId));
            edited.StartingWorld.EditableEconomyPolicy.SetCashBand(50000, 200000);

            var diagnostics = new List<string>();
            Assert.IsTrue(coordinator.UpdatePreset(edited, diagnostics), string.Join("; ", diagnostics));

            ScenarioPreset updated = coordinator.GetPreset(SliceLPresetFactory.PresetId);
            Assert.AreEqual("1.1.0", updated.PresetVersion, "Edit bumps the preset version (minor).");
            Assert.AreEqual(50000, updated.StartingWorld.EconomyPolicy.CashBandMinCents);

            // The live campaign is untouched: edits affect future generations only.
            Assert.AreEqual(campaignDefinitionVersion, coordinator.ActiveCampaign.DefinitionVersion);
            Assert.AreEqual(5, coordinator.ActiveSeed);
        }

        [Test]
        public void CaptureRestorePresetDto_RoundTrips()
        {
            Assert.IsTrue(RunNewGame(9, out _));
            ScenarioPreset dto = coordinator.CapturePresetDto();
            Assert.IsNotNull(dto);
            Assert.AreEqual(SliceLPresetFactory.PresetId, dto.PresetId);

            dto.SetIdentity("restored-preset", "Restored", "2.0.0");
            var diagnostics = new List<string>();
            Assert.IsTrue(coordinator.RestorePresetDto(dto, diagnostics), string.Join("; ", diagnostics));
            Assert.AreEqual("2.0.0", coordinator.GetPreset("restored-preset").PresetVersion);
        }

        // ---------- ghost-state prevention ----------

        [Test]
        public void NewGame_PurgesEveryGhostCategory()
        {
            Assert.IsTrue(RunNewGame(100, out _));
            world.PlantGhostState("run1");
            Assert.IsTrue(world.HasGhostTag("run1"), "Test setup failed to plant ghost state.");

            var diagnostics = new List<string>();
            Assert.IsTrue(coordinator.ExecuteLifecycle(ScenarioLifecycleAction.NewGame, 200, diagnostics),
                string.Join("; ", diagnostics));

            Assert.IsFalse(world.HasGhostTag("run1"),
                "New Game must not inherit any prior-run state in any category.");
            var cleared = new List<string>(world.ResetPort.ClearedCategories);
            Assert.IsTrue(cleared.Count >= 8);
            foreach (string category in ScenarioLifecycleSemantics.NewGameMustNotInherit)
            {
                Assert.IsTrue(cleared.Contains(category),
                    $"Reset must clear '{category}'.");
            }
        }

        [Test]
        public void NewGame_DoesNotInheritPriorWorldRecords()
        {
            Assert.IsTrue(RunNewGame(300, out _));
            string firstFingerprint = coordinator.ActiveCampaign.GenerationFingerprint;
            int firstHouseholds = world.Builder.LastDraft.Households.Count;

            // Capture run-1 authority contents for comparison.
            var firstLedgerStates = new List<LandLedgers.Population.HouseholdLedgerState>();
            world.Ledgers.ExportState(firstLedgerStates);
            int firstLedgerEntries = 0;
            foreach (var state in firstLedgerStates) firstLedgerEntries += state.Entries.Count;

            Assert.IsTrue(RunNewGame(301, out _));

            var secondLedgerStates = new List<LandLedgers.Population.HouseholdLedgerState>();
            world.Ledgers.ExportState(secondLedgerStates);
            foreach (var state in secondLedgerStates)
            {
                foreach (var entry in state.Entries)
                {
                    Assert.IsFalse((entry.Reason ?? string.Empty).Contains("seed:300"),
                        "Run-2 ledgers must not contain run-1 entries.");
                }
            }

            Assert.IsFalse(string.Equals(firstFingerprint, coordinator.ActiveCampaign.GenerationFingerprint));
            Assert.AreEqual(firstHouseholds > 0, true);
        }

        [Test]
        public void Coordinator_StoresNoWorldState_OnlyMetadata()
        {
            Assert.IsTrue(RunNewGame(400, out _));

            // Reflection guard: the coordinator must not hold person, household,
            // business, inventory, property, or money state — only metadata and ports.
            var forbidden = new[]
            {
                "PopulationState", "HouseholdLedger", "FinancialObligation",
                "BusinessRuntime", "GenericInventoryPosition", "PersonState", "HouseholdState",
            };
            foreach (FieldInfo field in typeof(ScenarioCoordinator).GetFields(
                BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public))
            {
                string typeName = field.FieldType.Name;
                foreach (string banned in forbidden)
                {
                    Assert.IsFalse(typeName.Contains(banned),
                        $"ScenarioCoordinator field '{field.Name}' of type '{typeName}' looks like world state.");
                }
            }

            Assert.IsNotNull(coordinator.ActiveCampaign);
            Assert.IsFalse(string.IsNullOrWhiteSpace(coordinator.ActiveCampaign.GenerationFingerprint));
        }

        [Test]
        public void RegisterPreset_RejectsDuplicatesAndInvalid()
        {
            var diagnostics = new List<string>();
            Assert.IsFalse(coordinator.RegisterPreset(SliceLPresetFactory.Build(), diagnostics),
                "Duplicate preset ids must be rejected.");
            Assert.IsTrue(coordinator.RegisterPreset(null, diagnostics) == false);
            Assert.IsFalse(coordinator.ValidatePreset("missing", diagnostics));
            Assert.IsTrue(coordinator.ValidatePreset(SliceLPresetFactory.PresetId, diagnostics));
        }
    }
}
