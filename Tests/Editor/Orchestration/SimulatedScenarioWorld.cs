using System;
using System.Collections.Generic;
using LandLedgers.Economy;
using LandLedgers.Economy.Financing;
using LandLedgers.Economy;
using LandLedgers.Orchestration.Scenarios;
using LandLedgers.Orchestration.Scenarios.Generation;
using LandLedgers.Persistence;
using LandLedgers.Population;
using LandLedgers.Primitives;

namespace LandLedgers.EditorTests.Orchestration
{
    /// <summary>
    /// Pass V P1-5 (Worker 1): an in-memory stand-in for the scene's domain
    /// authorities, used to verify coordinator lifecycle semantics and
    /// ghost-state prevention without Unity. Holds the same authorities the
    /// production hub owns (ledgers, obligations, employments, ids) plus the
    /// eight ghost-state categories from ScenarioLifecycleSemantics.
    /// </summary>
    public sealed class SimulatedScenarioWorld
    {
        public readonly HouseholdLedgerRegistry Ledgers = new HouseholdLedgerRegistry();
        public readonly FinancialObligationAuthority Obligations = new FinancialObligationAuthority();
        public readonly EmploymentRelationshipRegistry Employments = new EmploymentRelationshipRegistry();
        public readonly EntityIdRegistry Ids = new EntityIdRegistry();

        public readonly SimulatedInventoryPort Inventory = new SimulatedInventoryPort();
        public readonly SimulatedPropertyPort Property = new SimulatedPropertyPort();
        public readonly SimulatedCampaignStore CampaignStore = new SimulatedCampaignStore();

        // Ghost-state categories under test (mirrors ScenarioLifecycleSemantics.NewGameMustNotInherit).
        public readonly Dictionary<string, string> PlayerOwnership = new Dictionary<string, string>();
        public readonly Dictionary<int, string> Memberships = new Dictionary<int, string>();
        public readonly List<string> Objectives = new List<string>();
        public readonly List<string> ManagerDecisions = new List<string>();
        public readonly List<string> Businesses = new List<string>();
        public readonly Dictionary<string, int> BankingState = new Dictionary<string, int>();
        public readonly List<string> Reservations = new List<string>();

        public readonly ScenarioEconomyAuthorityPorts EconomyPorts;
        public readonly ScenarioWorldBuilder Builder;
        public readonly SimulatedResetPort ResetPort;

        public SimulatedScenarioWorld()
        {
            EconomyPorts = new ScenarioEconomyAuthorityPorts(Ledgers, Obligations, Employments, Ids);
            Builder = new ScenarioWorldBuilder(EconomyPorts, Inventory, Property);
            ResetPort = new SimulatedResetPort(this);
        }

        public ScenarioCoordinator CreateCoordinator()
        {
            return new ScenarioCoordinator(Builder, ResetPort, CampaignStore);
        }

        /// <summary>Plants prior-run state in every ghost category, tagged for detection.</summary>
        public void PlantGhostState(string tag)
        {
            PlayerOwnership[$"deed-{tag}"] = $"player-owned parcel {tag}";
            Memberships[-1000] = $"ghost membership {tag}";
            Objectives.Add($"ghost-objective-{tag}");
            ManagerDecisions.Add($"ghost-decision-{tag}");
            Businesses.Add($"ghost-business-{tag}");
            BankingState[$"ghost-account-{tag}"] = 999999;
            Reservations.Add($"ghost-reservation-{tag}");

            // Ghost rows inside the real authorities, too.
            Ledgers.GetOrCreate(-1000).RecordInflow(0, 100, HouseholdIncomeSource.OtherDocumented,
                $"ghost:{tag}", $"Ghost cash {tag}", "ghost");
            Obligations.Create(Ids, FinancialObligationKind.Loan, $"ghost-debtor-{tag}",
                $"ghost-creditor-{tag}", 1000, 0, "ghost", "ghost");
            Employments.Register(new EmploymentRelationship
            {
                Id = $"ghost-employment-{tag}",
                EmployeePersonId = -1000,
                EmployerBusinessId = $"ghost-business-{tag}",
                Compensation = CompensationTerms.FromWeeklyWage(100, "ghost"),
                Source = EmploymentSource.Manual,
            });
            Inventory.GetOrCreatePosition($"ghost-business-{tag}", "ghost-goods")
                .Add(5f, 100, null, $"ghost:{tag}", 0);
            Property.RecordGrant(new ScenarioSeedPropertyGrant
            {
                GrantId = $"ghost-grant-{tag}",
                BuildingId = -999,
                HouseholdId = -1000,
            }, $"ghost:{tag}");
        }

        /// <summary>True when any ghost-tagged record survives in any category.</summary>
        public bool HasGhostTag(string tag)
        {
            foreach (var kv in PlayerOwnership) if (kv.Key.Contains(tag) || kv.Value.Contains(tag)) return true;
            foreach (var kv in Memberships) if (kv.Value.Contains(tag)) return true;
            foreach (string s in Objectives) if (s.Contains(tag)) return true;
            foreach (string s in ManagerDecisions) if (s.Contains(tag)) return true;
            foreach (string s in Businesses) if (s.Contains(tag)) return true;
            foreach (var kv in BankingState) if (kv.Key.Contains(tag)) return true;
            foreach (string s in Reservations) if (s.Contains(tag)) return true;

            var ledgerStates = new List<HouseholdLedgerState>();
            Ledgers.ExportState(ledgerStates);
            foreach (HouseholdLedgerState state in ledgerStates)
            {
                foreach (HouseholdLedgerEntry entry in state.Entries)
                {
                    if ((entry.Reason ?? string.Empty).Contains(tag)
                        || (entry.SourceReference ?? string.Empty).Contains(tag))
                    {
                        return true;
                    }
                }
            }

            foreach (FinancialObligation obligation in Obligations.Obligations)
            {
                if ((obligation.Debtor ?? string.Empty).Contains(tag)
                    || (obligation.Creditor ?? string.Empty).Contains(tag))
                {
                    return true;
                }
            }

            EmploymentRegistrySaveDto employmentDto = Employments.CaptureSaveDto();
            foreach (EmploymentRelationship relationship in employmentDto.relationships)
            {
                if ((relationship.Id ?? string.Empty).Contains(tag))
                {
                    return true;
                }
            }

            foreach (var kv in Inventory.Positions)
            {
                if (kv.Key.Contains(tag))
                {
                    return true;
                }
            }

            foreach (ScenarioSeedPropertyGrant grant in Property.RecordedGrants)
            {
                if ((grant.GrantId ?? string.Empty).Contains(tag))
                {
                    return true;
                }
            }

            return false;
        }

        public sealed class SimulatedInventoryPort : IScenarioInventoryPort
        {
            public readonly Dictionary<string, GenericInventoryPosition> Positions =
                new Dictionary<string, GenericInventoryPosition>(StringComparer.Ordinal);

            public GenericInventoryPosition GetOrCreatePosition(string businessId, string productId)
            {
                string key = businessId + "|" + productId;
                if (!Positions.TryGetValue(key, out GenericInventoryPosition position))
                {
                    position = new GenericInventoryPosition(productId);
                    Positions[key] = position;
                }

                return position;
            }

            public void Clear()
            {
                Positions.Clear();
            }

            public void ClearAuthoredState(string reason)
            {
                Positions.Clear();
            }
        }

        public sealed class SimulatedPropertyPort : IScenarioPropertyPort
        {
            private readonly List<ScenarioSeedPropertyGrant> grants = new List<ScenarioSeedPropertyGrant>();

            public void RecordGrant(ScenarioSeedPropertyGrant grant, string provenanceTag)
            {
                if (grant != null)
                {
                    grants.Add(grant);
                }
            }

            public IReadOnlyList<ScenarioSeedPropertyGrant> RecordedGrants => grants;

            public void Clear()
            {
                grants.Clear();
            }

            public void ClearAuthoredState(string reason)
            {
                grants.Clear();
            }
        }

        public sealed class SimulatedCampaignStore : IScenarioCampaignStore
        {
            private readonly Dictionary<string, ScenarioCampaignRecord> slots =
                new Dictionary<string, ScenarioCampaignRecord>(StringComparer.Ordinal);

            public bool TryLoad(string slotId, out ScenarioCampaignRecord record)
            {
                if (slots.TryGetValue(slotId ?? string.Empty, out ScenarioCampaignRecord stored))
                {
                    record = stored.Copy();
                    return true;
                }

                record = null;
                return false;
            }

            public void Save(string slotId, ScenarioCampaignRecord record)
            {
                slots[slotId ?? string.Empty] = record?.Copy();
            }

            public void Delete(string slotId)
            {
                slots.Remove(slotId ?? string.Empty);
            }
        }

        public sealed class SimulatedResetPort : IScenarioWorldResetPort
        {
            private readonly SimulatedScenarioWorld world;

            public SimulatedResetPort(SimulatedScenarioWorld world)
            {
                this.world = world ?? throw new ArgumentNullException(nameof(world));
            }

            public IReadOnlyList<string> ClearedCategories =>
                ScenarioLifecycleSemantics.NewGameMustNotInherit;

            public void ResetForNewGame(List<string> diagnostics)
            {
                world.Ledgers.ClearForNewGame("simulated new game reset");
                world.Obligations.LoadFromSaveDto(new FinancialObligationSaveDto());
                world.Employments.LoadFromSaveDto(new EmploymentRegistrySaveDto());
                foreach (EntityKind kind in (EntityKind[])Enum.GetValues(typeof(EntityKind)))
                {
                    if (kind == EntityKind.Unspecified)
                    {
                        continue;
                    }

                    world.Ids.RestoreCursor(kind, 0);
                }

                world.Inventory.Clear();
                world.Property.Clear();

                world.PlayerOwnership.Clear();
                world.Memberships.Clear();
                world.Objectives.Clear();
                world.ManagerDecisions.Clear();
                world.Businesses.Clear();
                world.BankingState.Clear();
                world.Reservations.Clear();

                diagnostics?.Add("SimulatedResetPort: all run-scoped state purged " +
                    $"({string.Join(", ", ScenarioLifecycleSemantics.NewGameMustNotInherit)}).");
            }
        }
    }
}
