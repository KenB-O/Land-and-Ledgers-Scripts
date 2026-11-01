// Pass VIII (Worker 9): scenario/persistence identity — authored history is
// not a simulated transaction. New Game / Restart / save-load must never
// duplicate authored assets (starting cash, starting debt, employments,
// inventory lots, property grants) and must never silently cancel a valid
// obligation. Proves the ScenarioWorldBuilder/ScenarioEconomySeeder/
// ScenarioCoordinator reset+seed path at the production authorities.
using System.Collections.Generic;
using LandLedgers.Economy;
using LandLedgers.Orchestration.Scenarios;
using LandLedgers.Orchestration.Scenarios.Generation;
using LandLedgers.Population;
using NUnit.Framework;

namespace LandLedgers.EditorTests.Orchestration
{
    [TestFixture]
    public sealed class Pass8ScenarioIdentityTests
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

        private sealed class AuthoredCounts
        {
            public int LedgerEntryCount;
            public int ObligationCount;
            public int EmploymentCount;
            public int InventoryLotCount;
            public int PropertyGrantCount;
        }

        private AuthoredCounts CaptureAuthoredCounts()
        {
            var counts = new AuthoredCounts();
            var states = new List<HouseholdLedgerState>();
            world.Ledgers.ExportState(states);
            foreach (HouseholdLedgerState state in states)
            {
                counts.LedgerEntryCount += state?.Entries?.Count ?? 0;
            }

            counts.ObligationCount = world.Obligations.Obligations.Count;
            counts.EmploymentCount = world.Employments.Count;
            foreach (KeyValuePair<string, GenericInventoryPosition> kv in world.Inventory.Positions)
            {
                counts.InventoryLotCount += kv.Value?.Lots?.Count ?? 0;
            }

            counts.PropertyGrantCount = world.Property.RecordedGrants.Count;
            return counts;
        }

        private static void AssertCountsEqual(AuthoredCounts expected, AuthoredCounts actual, string context)
        {
            Assert.AreEqual(expected.LedgerEntryCount, actual.LedgerEntryCount,
                context + ": authored ledger entries must be identical, not doubled");
            Assert.AreEqual(expected.ObligationCount, actual.ObligationCount,
                context + ": authored obligations must be identical, not doubled");
            Assert.AreEqual(expected.EmploymentCount, actual.EmploymentCount,
                context + ": authored employments must be identical, not doubled");
            Assert.AreEqual(expected.InventoryLotCount, actual.InventoryLotCount,
                context + ": authored inventory lots must be identical, not doubled");
            Assert.AreEqual(expected.PropertyGrantCount, actual.PropertyGrantCount,
                context + ": authored property grants must be identical, not doubled");
        }

        [Test]
        public void NewGame_TwiceSameSeed_PostsAuthoredAssetsExactlyOnce()
        {
            var diagnostics = new List<string>();
            Assert.IsTrue(coordinator.ExecuteLifecycle(ScenarioLifecycleAction.NewGame, 42, diagnostics),
                string.Join("; ", diagnostics));
            AuthoredCounts first = CaptureAuthoredCounts();
            string firstFingerprint = coordinator.ActiveCampaign.GenerationFingerprint;
            Assert.Greater(first.LedgerEntryCount, 0, "slice L must author starting cash");
            Assert.Greater(first.InventoryLotCount, 0, "slice L must author opening stock lots");
            Assert.Greater(first.PropertyGrantCount, 0, "slice L must author property grants");

            // A second New Game with the same seed must regenerate the
            // identical world — the reset path purges prior-run state, so no
            // authored asset is posted twice.
            diagnostics = new List<string>();
            Assert.IsTrue(coordinator.ExecuteLifecycle(ScenarioLifecycleAction.NewGame, 42, diagnostics),
                string.Join("; ", diagnostics));
            AuthoredCounts second = CaptureAuthoredCounts();
            Assert.AreEqual(firstFingerprint, coordinator.ActiveCampaign.GenerationFingerprint);
            AssertCountsEqual(first, second, "NewGame x2 same seed");
        }

        [Test]
        public void RestartSameSeed_AfterSpending_RestoresAuthoredStateExactly()
        {
            var diagnostics = new List<string>();
            Assert.IsTrue(coordinator.ExecuteLifecycle(ScenarioLifecycleAction.NewGame, 7, diagnostics),
                string.Join("; ", diagnostics));
            AuthoredCounts authored = CaptureAuthoredCounts();

            // Spend from the player household: a real post-start transaction.
            var states = new List<HouseholdLedgerState>();
            world.Ledgers.ExportState(states);
            Assert.Greater(states.Count, 0);
            HouseholdLedger ledger = world.Ledgers.Get(states[0].HouseholdId);
            int authoredBalance = ledger.GetBalanceCents();
            Assert.Greater(authoredBalance, 0);
            Assert.IsNull(ledger.RecordOutflow(0, authoredBalance / 2, "test spend", "test"),
                "spending must succeed");
            Assert.AreNotEqual(authoredBalance, ledger.GetBalanceCents());

            // RestartSameSeed purges the run (including the spend) and
            // regenerates the identical authored world — no ghost spend, no
            // duplicated authored cash.
            diagnostics = new List<string>();
            Assert.IsTrue(coordinator.ExecuteLifecycle(ScenarioLifecycleAction.RestartSameSeed, 0, diagnostics),
                string.Join("; ", diagnostics));
            AssertCountsEqual(authored, CaptureAuthoredCounts(), "RestartSameSeed");
            var restored = new List<HouseholdLedgerState>();
            world.Ledgers.ExportState(restored);
            foreach (HouseholdLedgerState state in restored)
            {
                if (state.HouseholdId == states[0].HouseholdId)
                {
                    Assert.AreEqual(authoredBalance, world.Ledgers.Get(state.HouseholdId).GetBalanceCents(),
                        "RestartSameSeed must restore the authored starting balance, not the spent one");
                }
            }
        }

        [Test]
        public void SaveLoad_AuthorityRoundTrip_PreservesAuthoredStateExactly()
        {
            var diagnostics = new List<string>();
            Assert.IsTrue(coordinator.ExecuteLifecycle(ScenarioLifecycleAction.NewGame, 99, diagnostics),
                string.Join("; ", diagnostics));
            AuthoredCounts before = CaptureAuthoredCounts();

            // Round-trip each authority through its own save DTO (clear-then-load
            // semantics at the production authorities), then re-capture.
            var ledgerStates = new List<HouseholdLedgerState>();
            world.Ledgers.ExportState(ledgerStates);

            var fresh = new SimulatedScenarioWorld();
            fresh.Ledgers.ImportState(ledgerStates);
            fresh.Obligations.LoadFromSaveDto(world.Obligations.CaptureSaveDto());
            fresh.Employments.LoadFromSaveDto(world.Employments.CaptureSaveDto());

            var reloadedStates = new List<HouseholdLedgerState>();
            fresh.Ledgers.ExportState(reloadedStates);
            int reloadedEntries = 0;
            foreach (HouseholdLedgerState state in reloadedStates)
            {
                reloadedEntries += state?.Entries?.Count ?? 0;
            }

            Assert.AreEqual(before.LedgerEntryCount, reloadedEntries,
                "ledger round-trip must preserve every authored entry exactly once");
            Assert.AreEqual(before.ObligationCount, fresh.Obligations.Obligations.Count,
                "obligation round-trip must preserve every authored obligation exactly once");
            Assert.AreEqual(before.EmploymentCount, fresh.Employments.Count,
                "employment round-trip must preserve every authored employment exactly once");

            // Loading the same DTO again is idempotent: no duplicates, nothing cancelled.
            fresh.Obligations.LoadFromSaveDto(world.Obligations.CaptureSaveDto());
            fresh.Employments.LoadFromSaveDto(world.Employments.CaptureSaveDto());
            Assert.AreEqual(before.ObligationCount, fresh.Obligations.Obligations.Count,
                "double load must not duplicate obligations");
            Assert.AreEqual(before.EmploymentCount, fresh.Employments.Count,
                "double load must not duplicate employments");
        }
    }
}
