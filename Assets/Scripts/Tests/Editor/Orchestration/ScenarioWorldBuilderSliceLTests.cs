using System;
using System.Collections.Generic;
using NUnit.Framework;
using LandLedgers.Economy;
using LandLedgers.Economy.Financing;
using LandLedgers.Orchestration.Scenarios;
using LandLedgers.Orchestration.Scenarios.Generation;
using LandLedgers.Persistence;
using LandLedgers.Population;

namespace LandLedgers.EditorTests.Orchestration
{
    /// <summary>
    /// Pass V slice L (Worker 1): the authored slice-L scenario generates two
    /// different valid worlds from two seeds, regenerates the same seed to an
    /// exactly identical world, honors population constraints, and produces
    /// coherent ownership and finances with scenario provenance. Save/load of
    /// one generated campaign is verified to restore without regenerating.
    /// </summary>
    [TestFixture]
    public sealed class ScenarioWorldBuilderSliceLTests
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

        private bool NewGame(int seed, out List<string> diagnostics, out ScenarioGenerationOutcome outcome)
        {
            diagnostics = new List<string>();
            bool ok = coordinator.ExecuteLifecycle(ScenarioLifecycleAction.NewGame, seed, diagnostics);
            outcome = null;
            return ok;
        }

        private static string ProvenanceTag(int seed)
        {
            return ScenarioCampaignRecord.BuildProvenanceTag(
                SliceLPresetFactory.PresetId,
                ScenarioPresetLibrary.CurrentDefinitionVersion,
                seed);
        }

        // ---------- generation across seeds ----------

        [Test]
        public void TwoDifferentSeeds_BothValid_DifferentWorlds()
        {
            Assert.IsTrue(NewGame(1234, out List<string> d1, out _), string.Join("; ", d1));
            string first = coordinator.ActiveCampaign.GenerationFingerprint;
            ScenarioSeedPopulation firstDraft = world.Builder.LastDraft;
            int firstResidents = firstDraft.ResidentCount;

            Assert.IsTrue(NewGame(98765, out List<string> d2, out _), string.Join("; ", d2));
            string second = coordinator.ActiveCampaign.GenerationFingerprint;
            ScenarioSeedPopulation secondDraft = world.Builder.LastDraft;

            Assert.IsFalse(string.IsNullOrWhiteSpace(first));
            Assert.IsFalse(string.IsNullOrWhiteSpace(second));
            Assert.IsFalse(string.Equals(first, second), "Different seeds must yield different worlds.");

            foreach (ScenarioSeedPopulation draft in new[] { firstDraft, secondDraft })
            {
                Assert.IsTrue(draft.ResidentCount >= 60 && draft.ResidentCount <= 90,
                    $"Residents {draft.ResidentCount} outside the authored [60,90] range.");
                Assert.IsTrue(draft.ResidentCount >= 55 && draft.ResidentCount <= 95,
                    $"Residents {draft.ResidentCount} outside the Canon [55,95] calibration.");
                foreach (ScenarioSeedHousehold household in draft.Households)
                {
                    Assert.IsTrue(household.Members.Count >= 1,
                        $"Household {household.HouseholdId} has no members.");
                }

                Assert.IsTrue(draft.Businesses.Count >= 3,
                    "The three fixed businesses must exist.");
            }

            Assert.IsTrue(firstResidents >= 60 && firstResidents <= 90);
        }

        [Test]
        public void SameSeed_RegeneratesExactlyIdenticalWorld()
        {
            Assert.IsTrue(NewGame(4242, out _, out _));
            Reconciliation first = Reconcile(world, coordinator, 4242);

            // Fresh authorities, same preset + seed: must reproduce exactly.
            var world2 = new SimulatedScenarioWorld();
            var coordinator2 = world2.CreateCoordinator();
            var diagnostics = new List<string>();
            Assert.IsTrue(coordinator2.RegisterPreset(SliceLPresetFactory.Build(), diagnostics),
                string.Join("; ", diagnostics));
            coordinator2.SelectedPresetId = SliceLPresetFactory.PresetId;
            Assert.IsTrue(coordinator2.ExecuteLifecycle(ScenarioLifecycleAction.NewGame, 4242, diagnostics),
                string.Join("; ", diagnostics));
            Reconciliation second = Reconcile(world2, coordinator2, 4242);

            Assert.AreEqual(first.WorldFingerprint, second.WorldFingerprint);
            Assert.AreEqual(first.ResidentCount, second.ResidentCount);
            Assert.AreEqual(first.HouseholdCount, second.HouseholdCount);
            Assert.AreEqual(first.TotalCashCents, second.TotalCashCents);
            Assert.AreEqual(first.ObligationCount, second.ObligationCount);
            Assert.AreEqual(first.TotalDebtCents, second.TotalDebtCents);
            Assert.AreEqual(first.EmploymentCount, second.EmploymentCount);
            Assert.AreEqual(first.RosterHash, second.RosterHash,
                "The person roster (names, ages, wages) must be byte-identical.");
            Assert.AreEqual(first.InventoryHash, second.InventoryHash,
                "Opening-stock lots (ids, quantities, provenance) must be identical.");
        }

        [Test]
        public void RestartSameSeed_ReproducesIdenticalWorld()
        {
            Assert.IsTrue(NewGame(777, out _, out _));
            Reconciliation first = Reconcile(world, coordinator, 777);

            var diagnostics = new List<string>();
            Assert.IsTrue(coordinator.ExecuteLifecycle(ScenarioLifecycleAction.RestartSameSeed, 0, diagnostics),
                string.Join("; ", diagnostics));
            Reconciliation second = Reconcile(world, coordinator, 777);

            Assert.AreEqual(first.WorldFingerprint, second.WorldFingerprint);
            Assert.AreEqual(first.TotalCashCents, second.TotalCashCents);
            Assert.AreEqual(first.RosterHash, second.RosterHash);
        }

        // ---------- population constraints ----------

        [Test]
        public void PopulationConstraints_Honored()
        {
            foreach (int seed in new[] { 1, 2, 3, 555, 90210 })
            {
                var fresh = new SimulatedScenarioWorld();
                var coord = fresh.CreateCoordinator();
                var diagnostics = new List<string>();
                Assert.IsTrue(coord.RegisterPreset(SliceLPresetFactory.Build(), diagnostics));
                coord.SelectedPresetId = SliceLPresetFactory.PresetId;
                Assert.IsTrue(coord.ExecuteLifecycle(ScenarioLifecycleAction.NewGame, seed, diagnostics),
                    $"seed {seed}: " + string.Join("; ", diagnostics));

                ScenarioSeedPopulation draft = fresh.Builder.LastDraft;
                Assert.IsTrue(draft.ResidentCount >= 60 && draft.ResidentCount <= 90,
                    $"seed {seed}: {draft.ResidentCount} residents outside [60,90].");
            }
        }

        [Test]
        public void ConfigurableStartDate_FlowsIntoWorld()
        {
            var fresh = new SimulatedScenarioWorld();
            var coord = fresh.CreateCoordinator();
            ScenarioPreset preset = SliceLPresetFactory.Build(1875, 6, 15);
            var diagnostics = new List<string>();
            Assert.IsTrue(ScenarioPresetLibrary.Validate(preset, diagnostics), string.Join("; ", diagnostics));
            Assert.IsTrue(coord.RegisterPreset(preset, diagnostics));
            coord.SelectedPresetId = SliceLPresetFactory.PresetId;
            Assert.IsTrue(coord.ExecuteLifecycle(ScenarioLifecycleAction.NewGame, 5, diagnostics),
                string.Join("; ", diagnostics));

            int expectedDay = ScenarioDateUtil.ToDayIndex(1875, 6, 15);
            Assert.AreEqual(expectedDay, coord.ActiveCampaign.StartDayIndex);
            Assert.AreEqual("1875-06-15", coord.ActiveCampaign.StartDateIso);

            var states = new List<HouseholdLedgerState>();
            fresh.Ledgers.ExportState(states);
            Assert.IsTrue(states.Count > 0);
            foreach (HouseholdLedgerState state in states)
            {
                foreach (HouseholdLedgerEntry entry in state.Entries)
                {
                    Assert.AreEqual(expectedDay, entry.DayIndex,
                        "Scenario-authored cash must be dated to the configured start date.");
                }
            }
        }

        // ---------- ownership / finance coherence ----------

        [Test]
        public void OwnershipAndFinances_AreCoherent_WithProvenance()
        {
            const int seed = 31337;
            Assert.IsTrue(NewGame(seed, out List<string> diagnostics, out _), string.Join("; ", diagnostics));

            ScenarioSeedPopulation draft = world.Builder.LastDraft;
            string tag = ProvenanceTag(seed);
            var householdIds = new HashSet<int>();
            var personIds = new HashSet<int>();
            var businessIds = new HashSet<string>();
            foreach (ScenarioSeedHousehold household in draft.Households)
            {
                householdIds.Add(household.HouseholdId);
                foreach (ScenarioSeedPerson person in household.Members)
                {
                    personIds.Add(person.PersonId);
                }
            }

            foreach (ScenarioSeedBusiness business in draft.Businesses)
            {
                businessIds.Add(business.BusinessId);
            }

            // Cash: every inflow carries provenance; balances derive from entries.
            var ledgerStates = new List<HouseholdLedgerState>();
            world.Ledgers.ExportState(ledgerStates);
            Assert.AreEqual(draft.Households.Count, ledgerStates.Count,
                "Every generated household must have exactly one ledger.");
            int totalCash = 0;
            foreach (HouseholdLedgerState state in ledgerStates)
            {
                Assert.IsTrue(householdIds.Contains(state.HouseholdId),
                    $"Ledger for unknown household {state.HouseholdId}.");
                int balance = 0;
                foreach (HouseholdLedgerEntry entry in state.Entries)
                {
                    balance += entry.AmountCents;
                    if (entry.IsInflow)
                    {
                        Assert.AreNotEqual(HouseholdIncomeSource.Unspecified, entry.Source,
                            "Every inflow must name its source class.");
                        Assert.IsFalse(string.IsNullOrWhiteSpace(entry.Reason),
                            "Every inflow must carry its economic reason.");
                        Assert.IsTrue((entry.SourceReference ?? string.Empty).Contains("scenario:"),
                            "Starting cash must carry scenario provenance, distinguishable from simulated events.");
                        Assert.IsTrue((entry.Reason ?? string.Empty).Contains(tag),
                            "Starting cash must carry the campaign provenance tag.");
                    }

                    Assert.AreEqual(balance, entry.BalanceAfterCents,
                        "BalanceAfterCents must equal the running entry total.");
                }

                Assert.IsTrue(balance >= 0, "Household cash must never go negative.");
                totalCash += balance;
            }

            Assert.IsTrue(totalCash > 0, "The generated world must hold starting cash.");

            // Debt: debtors and creditors are real; every obligation is provenance-tagged.
            int totalDebt = 0;
            int obligationCount = 0;
            foreach (FinancialObligation obligation in world.Obligations.Obligations)
            {
                obligationCount++;
                Assert.IsTrue(obligation.Debtor.StartsWith("household:"),
                    $"Obligation debtor '{obligation.Debtor}' must reference a household.");
                int debtorId = int.Parse(obligation.Debtor.Substring("household:".Length));
                Assert.IsTrue(householdIds.Contains(debtorId),
                    $"Obligation debtor household {debtorId} is not a real household.");
                Assert.IsTrue(businessIds.Contains(obligation.Creditor),
                    $"Obligation creditor '{obligation.Creditor}' is not a real business.");
                Assert.IsTrue(obligation.OriginalPrincipalCents > 0);
                Assert.AreEqual(tag, obligation.SourceEconomicEventId,
                    "Every starting debt must carry the campaign provenance tag.");
                Assert.IsTrue(obligation.OutstandingPrincipalCents <= 150000,
                    "No starting debt may exceed the authored per-household maximum.");
                totalDebt += obligation.OutstandingPrincipalCents;
            }

            // Employment: real persons, real businesses, authored source, agreed terms.
            EmploymentRegistrySaveDto employmentDto = world.Employments.CaptureSaveDto();
            var employmentIds = new HashSet<string>();
            foreach (EmploymentRelationship relationship in employmentDto.relationships)
            {
                Assert.IsTrue(employmentIds.Add(relationship.Id), $"Duplicate employment id '{relationship.Id}'.");
                Assert.IsTrue(personIds.Contains(relationship.EmployeePersonId),
                    $"Employment '{relationship.Id}' names unknown person {relationship.EmployeePersonId}.");
                Assert.IsTrue(businessIds.Contains(relationship.EmployerBusinessId),
                    $"Employment '{relationship.Id}' names unknown business '{relationship.EmployerBusinessId}'.");
                Assert.AreEqual(EmploymentSource.Authored, relationship.Source);
                Assert.IsTrue(relationship.Compensation.AgreedWeeklyWageCents > 0,
                    "Agreed compensation must be positive and explicit.");
                Assert.AreEqual(EmploymentLifecycleState.Active, relationship.LifecycleState);
            }

            Assert.IsTrue(employmentDto.relationships.Count > 0,
                "A coherent starting economy employs a share of working-age residents.");

            // Inventory: authored opening stock with scenario provenance.
            Assert.IsTrue(world.Inventory.Positions.Count > 0);
            foreach (var kv in world.Inventory.Positions)
            {
                string[] parts = kv.Key.Split('|');
                Assert.IsTrue(businessIds.Contains(parts[0]),
                    $"Inventory position for unknown business '{parts[0]}'.");
                Assert.IsTrue(kv.Value.Quantity > 0);
                foreach (var lot in kv.Value.Lots)
                {
                    Assert.IsTrue((lot.Provenance ?? string.Empty).Contains("scenario-authored:"),
                        "Opening stock must carry scenario-authored provenance.");
                }
            }

            // Property: grants name real households and real buildings.
            var buildingIds = new HashSet<int>();
            foreach (ScenarioSeedHousehold household in draft.Households)
            {
                buildingIds.Add(household.HomeBuildingId);
            }

            foreach (ScenarioSeedBusiness business in draft.Businesses)
            {
                buildingIds.Add(business.BuildingId);
            }

            Assert.IsTrue(world.Property.RecordedGrants.Count > 0);
            foreach (ScenarioSeedPropertyGrant grant in world.Property.RecordedGrants)
            {
                Assert.IsTrue(householdIds.Contains(grant.HouseholdId),
                    $"Property grant '{grant.GrantId}' names unknown household {grant.HouseholdId}.");
                Assert.IsTrue(buildingIds.Contains(grant.BuildingId),
                    $"Property grant '{grant.GrantId}' names unknown building {grant.BuildingId}.");
            }

            // Player start: the authored player household exists with its stake.
            ScenarioSeedHousehold playerHousehold = null;
            foreach (ScenarioSeedHousehold household in draft.Households)
            {
                if (household.IsPlayerHousehold)
                {
                    playerHousehold = household;
                }
            }

            Assert.IsNotNull(playerHousehold, "The authored player household must exist.");
            HouseholdLedger playerLedger = world.Ledgers.Get(playerHousehold.HouseholdId);
            Assert.IsNotNull(playerLedger);
            Assert.AreEqual(250000, playerLedger.GetBalanceCents(),
                "The authored player stake must land in the player ledger through the cash authority.");
        }

        // ---------- save / load ----------

        [Test]
        public void SaveLoad_RestoresCampaign_WithoutRegenerating()
        {
            const int seed = 60606;
            Assert.IsTrue(NewGame(seed, out _, out _));
            Reconciliation before = Reconcile(world, coordinator, seed);

            // Simulate an app restart: fresh world, same campaign store.
            var fresh = new SimulatedScenarioWorld();
            // Share the saved campaign slot (the save pipeline owns world restore).
            fresh.CampaignStore.Save(ScenarioCoordinator.DefaultCampaignSlot,
                coordinator.ActiveCampaign.Copy());
            var loader = new ScenarioCoordinator(fresh.Builder, fresh.ResetPort, fresh.CampaignStore);

            var diagnostics = new List<string>();
            Assert.IsTrue(loader.ExecuteLifecycle(ScenarioLifecycleAction.LoadGame, 0, diagnostics),
                string.Join("; ", diagnostics));

            Assert.AreEqual(0, fresh.Builder.GenerationCalls,
                "LoadGame must not regenerate the world.");
            Assert.IsTrue(loader.ActiveCampaign.DescribesSameWorld(before.Campaign),
                "The loaded campaign must describe the same realized world.");
            Assert.AreEqual(seed, loader.ActiveSeed);

            // The fresh world's authorities are untouched: loading restores the
            // record; world restoration belongs to the save pipeline.
            var states = new List<HouseholdLedgerState>();
            fresh.Ledgers.ExportState(states);
            Assert.AreEqual(0, states.Count);
        }

        [Test]
        public void InvalidPreset_NeverGeneratesWorld()
        {
            var fresh = new SimulatedScenarioWorld();
            var coord = fresh.CreateCoordinator();
            var diagnostics = new List<string>();
            Assert.IsFalse(coord.RegisterPreset(SliceLPresetFactory.BuildInvalidPopulationRange(), diagnostics));
            coord.SelectedPresetId = SliceLPresetFactory.PresetId;
            Assert.IsFalse(coord.ExecuteLifecycle(ScenarioLifecycleAction.NewGame, 1, diagnostics));
            Assert.AreEqual(0, fresh.Builder.GenerationCalls);
            var states = new List<HouseholdLedgerState>();
            fresh.Ledgers.ExportState(states);
            Assert.AreEqual(0, states.Count, "Failed generation must leave the world empty, not half-built.");
        }

        // ---------- reconciliation ----------

        private static Reconciliation Reconcile(
            SimulatedScenarioWorld target, ScenarioCoordinator coord, int seed)
        {
            var reconciliation = new Reconciliation();
            reconciliation.Campaign = coord.ActiveCampaign;
            reconciliation.WorldFingerprint = reconciliation.Campaign?.GenerationFingerprint ?? string.Empty;

            ScenarioSeedPopulation draft = target.Builder.LastDraft;
            reconciliation.ResidentCount = draft.ResidentCount;
            reconciliation.HouseholdCount = draft.Households.Count;

            var ledgerStates = new List<HouseholdLedgerState>();
            target.Ledgers.ExportState(ledgerStates);
            foreach (HouseholdLedgerState state in ledgerStates)
            {
                foreach (HouseholdLedgerEntry entry in state.Entries)
                {
                    if (entry.IsInflow)
                    {
                        reconciliation.TotalCashCents += entry.AmountCents;
                    }
                }
            }

            foreach (FinancialObligation obligation in target.Obligations.Obligations)
            {
                reconciliation.ObligationCount++;
                reconciliation.TotalDebtCents += obligation.OutstandingPrincipalCents;
            }

            EmploymentRegistrySaveDto employmentDto = target.Employments.CaptureSaveDto();
            reconciliation.EmploymentCount = employmentDto.relationships.Count;

            // Roster hash: canonical person roster (names, ages, wages) for exactness.
            var roster = new System.Text.StringBuilder(4096);
            foreach (ScenarioSeedHousehold household in draft.Households)
            {
                foreach (ScenarioSeedPerson person in household.Members)
                {
                    roster.Append(person.PersonId).Append(':')
                        .Append(person.FirstName).Append(' ').Append(person.LastName).Append(':')
                        .Append(person.Age).Append(':').Append(person.WeeklyWageCents).Append(';');
                }
            }

            reconciliation.RosterHash = Hash64(roster.ToString());

            // Inventory hash: lot ids, quantities, costs, provenance per position.
            var inventory = new System.Text.StringBuilder(2048);
            var positionKeys = new List<string>(target.Inventory.Positions.Keys);
            positionKeys.Sort();
            foreach (string key in positionKeys)
            {
                var position = target.Inventory.Positions[key];
                inventory.Append(key).Append(':').Append(position.Quantity).Append(':')
                    .Append(position.AverageUnitCostCents).Append(';');
                foreach (var lot in position.Lots)
                {
                    inventory.Append(lot.LotId).Append(':').Append(lot.Quantity).Append(':')
                        .Append(lot.UnitCostCents).Append(':').Append(lot.Provenance).Append(';');
                }
            }

            reconciliation.InventoryHash = Hash64(inventory.ToString());
            return reconciliation;
        }

        private static string Hash64(string canonical)
        {
            unchecked
            {
                ulong hash = 1469598103934665603ul;
                for (int i = 0; i < canonical.Length; i++)
                {
                    hash ^= canonical[i];
                    hash *= 1099511628211ul;
                }

                return hash.ToString("X16");
            }
        }

        private sealed class Reconciliation
        {
            public string WorldFingerprint;
            public int ResidentCount;
            public int HouseholdCount;
            public int TotalCashCents;
            public int ObligationCount;
            public int TotalDebtCents;
            public int EmploymentCount;
            public string RosterHash;
            public string InventoryHash;
            public ScenarioCampaignRecord Campaign;
        }
    }
}
