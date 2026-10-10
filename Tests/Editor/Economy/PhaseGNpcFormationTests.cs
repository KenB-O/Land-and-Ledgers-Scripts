using System.Collections.Generic;
using System.Reflection;
using LandLedgers.Economy;
using LandLedgers.Economy.Creation;
using LandLedgers.Population;
using LandLedgers.Primitives;
using NUnit.Framework;

namespace LandLedgers.EditorTests.Economy
{
    /// <summary>
    /// Phase G (G1-G3): NPC opportunity observation (real information only,
    /// no omniscience), investigation (real time, walk-aways recorded), and
    /// formation with full resource accounting through the generic
    /// creation port. All run in the Roslyn harness.
    /// </summary>
    [TestFixture]
    public sealed class PhaseGNpcFormationTests
    {
        private sealed class FakeCreationPort : INpcBusinessCreationPort
        {
            public EntityIdRegistry Ids = new EntityIdRegistry();
            public NpcBusinessFormationIntent LastIntent;
            public int CreateCalls;
            public bool RefuseNext;
            public string RefuseReason = "fake port refused.";

            public string TryCreateBusiness(NpcBusinessFormationIntent intent, List<string> diagnostics,
                out NpcCreatedBusiness created)
            {
                created = null;
                CreateCalls++;
                LastIntent = intent;
                if (RefuseNext)
                {
                    diagnostics?.Add(RefuseReason);
                    return RefuseReason;
                }

                EntityId id = Ids.Allocate(EntityKind.Business);
                var capabilityIds = new List<string>();
                if (intent.Activities != null)
                {
                    foreach (NpcBusinessActivitySpec activity in intent.Activities)
                    {
                        if (activity?.CapabilityIds == null) continue;
                        foreach (string cap in activity.CapabilityIds)
                        {
                            if (!string.IsNullOrWhiteSpace(cap) && !capabilityIds.Contains(cap))
                            {
                                capabilityIds.Add(cap);
                            }
                        }
                    }
                }

                created = new NpcCreatedBusiness
                {
                    BusinessEntityKey = id.ToString(),
                    InstanceId = "biz-" + id.ToString(),
                    DisplayName = intent.DisplayName,
                    CapabilityIds = capabilityIds,
                    OwnerName = intent.FounderName,
                    FounderPersonId = intent.FounderPersonId,
                };
                diagnostics?.Add($"fake port created {created.BusinessEntityKey} (records intent, same shape as the generic flow).");
                return null;
            }
        }

        private sealed class Fixture
        {
            public EntityIdRegistry Ids = new EntityIdRegistry();
            public HouseholdLedger Ledger;
            public EmploymentRelationshipRegistry Employments = new EmploymentRelationshipRegistry();
            public PersonScheduleTracker Scheduler = new PersonScheduleTracker();
            public FakeCreationPort Port = new FakeCreationPort();
            public NpcFormationAssetRegister Assets = new NpcFormationAssetRegister();
            public NpcBusinessEventLog Events = new NpcBusinessEventLog();
            public List<string> Diag = new List<string>();

            public Fixture(int householdCashCents = 100000)
            {
                Ledger = new HouseholdLedger(7);
                Assert.IsNull(Ledger.RecordInflow(0, householdCashCents, HouseholdIncomeSource.OwnerContribution,
                    "test-seed", "test seed capital", "test"));
            }

            public NpcFormationContext Context(int day = 10)
            {
                return new NpcFormationContext
                {
                    HouseholdLedger = Ledger,
                    Employments = Employments,
                    ScheduleTracker = Scheduler,
                    CreationPort = Port,
                    AssetRegister = Assets,
                    Events = Events,
                    Ids = Ids,
                    DayIndex = day,
                };
            }

            public NpcBusinessFormationIntent BakeryIntent()
            {
                return new NpcBusinessFormationIntent
                {
                    FounderPersonId = 42,
                    FounderName = "Marta",
                    FounderSurname = "Kessler",
                    FounderHouseholdId = 7,
                    DisplayName = "Kessler Bakery",
                    Activities = new List<NpcBusinessActivitySpec>
                    {
                        new NpcBusinessActivitySpec
                        {
                            BusinessType = BusinessType.Bakery,
                            CapabilityIds = new List<string> { "bake-bread", "sell-baked-goods" },
                            RequiredRoleIds = new List<string> { "baker" },
                            RequiredStockItemIds = new List<string> { "flour", "bread" },
                            RequiredEquipmentIds = new List<string> { "oven" },
                        },
                    },
                    Premises = new NpcPremisesArrangement
                    {
                        Tenure = NpcPremisesTenure.Rented,
                        ParcelOrBuildingRef = "bldg-shop-3",
                        AgreementId = "lease-77",
                        UpfrontCostCents = 20000,
                        RecurringCostCents = 1500,
                        CustomerFacing = true,
                        FoodHandling = true,
                    },
                    Assets = new List<NpcFormationAsset>
                    {
                        new NpcFormationAsset { AssetKind = "equipment", ItemId = "oven", Units = 1, CostCents = 15000, Provenance = "purchase:smith-ledger-12" },
                        new NpcFormationAsset { AssetKind = "inventory", ItemId = "flour", Units = 40, CostCents = 20000, Provenance = "lot:mill-9" },
                        new NpcFormationAsset { AssetKind = "inventory", ItemId = "bread", Units = 30, CostCents = 5000, Provenance = "produced:batch-1" },
                    },
                    Hires = new List<NpcFormationHire>
                    {
                        new NpcFormationHire { PersonId = 43, RoleId = "baker", AgreedWeeklyWageCents = 900, StartDayIndex = 10 },
                    },
                    Agreements = new List<NpcFormationAgreement>
                    {
                        new NpcFormationAgreement { AgreementKind = "supplier", AgreementId = "sup-flour-1", Counterparty = "Grain Mill", Summary = "40 units flour/week at 480c/unit" },
                        new NpcFormationAgreement { AgreementKind = "premises", AgreementId = "lease-77", Counterparty = "Shop Owner", Summary = "storefront lease 1500c/week" },
                    },
                    OwnerLaborHoursPerWeek = 30,
                    WorkingCapitalCents = 10000,
                };
            }
        }

        // ---------- G1: observation ----------

        [Test]
        public void Observation_WitnessedWithoutEvidence_Refused()
        {
            var log = new NpcOpportunityObservationLog(42, 7);
            var events = new NpcBusinessEventLog();
            var obs = new NpcMarketObservation
            {
                ObserverPersonId = 42,
                ObserverHouseholdId = 7,
                DayIndex = 3,
                Kind = NpcObservationKind.WitnessedUnfulfilledDemand,
                ItemOrServiceId = "flour",
                EvidenceReference = string.Empty, // hearsay — no witness event
            };
            string refusal = log.RecordObservation(obs, events);
            Assert.IsNotNull(refusal, "Witnessed observations without an evidence reference must be refused.");
            Assert.AreEqual(0, log.Observations.Count);
        }

        [Test]
        public void Observation_OwnHousehold_ByMembership_IsLegitimate()
        {
            var log = new NpcOpportunityObservationLog(42, 7);
            var events = new NpcBusinessEventLog();
            NpcMarketObservation obs = NpcOpportunityObservationLog.FromHouseholdNeed(
                needSequence: 5, needHouseholdId: 7, itemId: "flour", unitsNeeded: 12,
                createdDayIndex: 3, observerPersonId: 42, observerHouseholdId: 7,
                witnessEventReference: null);
            Assert.AreEqual(NpcObservationKind.OwnHouseholdUnfulfilledDemand, obs.Kind);
            Assert.IsNull(log.RecordObservation(obs, events));
            Assert.AreEqual(1, log.Observations.Count);
            Assert.AreEqual(1, events.CountKind(NpcBusinessEventKind.OpportunityObserved));
        }

        [Test]
        public void Observation_CannotBorrowAnotherPersonsEyes()
        {
            var log = new NpcOpportunityObservationLog(42, 7);
            var events = new NpcBusinessEventLog();
            var obs = new NpcMarketObservation
            {
                ObserverPersonId = 99, // not this log's person
                ObserverHouseholdId = 7,
                DayIndex = 3,
                Kind = NpcObservationKind.OwnHouseholdUnfulfilledDemand,
                ItemOrServiceId = "flour",
            };
            Assert.IsNotNull(log.RecordObservation(obs, events), "A Person cannot record observations as another Person.");
            Assert.AreEqual(0, log.Observations.Count);
        }

        [Test]
        public void Observation_OwnHousehold_WrongHousehold_Refused()
        {
            var log = new NpcOpportunityObservationLog(42, 7);
            var events = new NpcBusinessEventLog();
            var obs = new NpcMarketObservation
            {
                ObserverPersonId = 42,
                ObserverHouseholdId = 9, // not their household
                DayIndex = 3,
                Kind = NpcObservationKind.OwnHouseholdUnfulfilledDemand,
                ItemOrServiceId = "flour",
            };
            Assert.IsNotNull(log.RecordObservation(obs, events));
            Assert.AreEqual(0, log.Observations.Count);
        }

        [Test]
        public void Recognition_OnlyFromOwnRepeatedObservations_NoOmniscience()
        {
            var events = new NpcBusinessEventLog();
            var diag = new List<string>();
            var marta = new NpcOpportunityObservationLog(42, 7);
            // Three witnessed observations over two days: flour stockouts she saw.
            for (int i = 0; i < 3; i++)
            {
                var obs = new NpcMarketObservation
                {
                    ObserverPersonId = 42, ObserverHouseholdId = 7,
                    DayIndex = i < 2 ? 3 : 4,
                    Kind = NpcObservationKind.WitnessedUnfulfilledDemand,
                    ItemOrServiceId = "flour",
                    UnmetUnitsPerDay = 10,
                    EvidenceReference = "market-visit-" + (i < 2 ? "a" : "b"),
                };
                Assert.IsNull(marta.RecordObservation(obs, events));
            }

            List<NpcOpportunity> recognized = marta.RecognizeOpportunities(3, 2, 5, events, diag);
            Assert.AreEqual(1, recognized.Count);
            Assert.AreEqual("flour", recognized[0].ItemOrServiceId);
            Assert.AreEqual(3, recognized[0].ObservationCount);
            Assert.AreEqual(2, recognized[0].DistinctWitnessDays);
            Assert.AreEqual(15, recognized[0].EstimatedUnmetUnitsPerDay, "30 witnessed unmet units over 2 days = 15/day — derived, not market-wide.");

            // Another person who saw nothing recognizes nothing — no omniscient scan.
            var stranger = new NpcOpportunityObservationLog(77, 9);
            List<NpcOpportunity> strangerRec = stranger.RecognizeOpportunities(1, 1, 5, events, diag);
            Assert.AreEqual(0, strangerRec.Count, "A Person with no observations recognizes no opportunities.");
        }

        [Test]
        public void Recognition_NeverCreatesABusiness()
        {
            var events = new NpcBusinessEventLog();
            var diag = new List<string>();
            var log = new NpcOpportunityObservationLog(42, 7);
            for (int i = 0; i < 4; i++)
            {
                Assert.IsNull(log.RecordObservation(new NpcMarketObservation
                {
                    ObserverPersonId = 42, ObserverHouseholdId = 7, DayIndex = 3 + i,
                    Kind = NpcObservationKind.VisibleQueue, ItemOrServiceId = "bread",
                    UnmetUnitsPerDay = 20, EvidenceReference = "queue-day-" + i,
                }, events));
            }

            var port = new FakeCreationPort();
            log.RecognizeOpportunities(2, 2, 10, events, diag);
            Assert.AreEqual(0, port.CreateCalls, "Recognition must never create a business — §29 regression.");
            Assert.AreEqual(1, log.Opportunities.Count);
        }

        [Test]
        public void NoManufacturing_ReflectionGuard_ObservationAndInvestigationCannotCreateBusinesses()
        {
            // The observation and investigation classes must have no path to
            // business creation: no method mentioning creation/manufacture,
            // no port field. This is the §29 regression guard at the API surface.
            foreach (System.Type type in new[] { typeof(NpcOpportunityObservationLog), typeof(NpcBusinessInvestigation) })
            {
                foreach (MethodInfo method in type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static))
                {
                    string name = method.Name.ToLowerInvariant();
                    Assert.IsFalse(name.Contains("createbusiness") || name.Contains("manufacture"),
                        $"{type.Name}.{method.Name} looks like a business-creation path — forbidden.");
                }

                foreach (FieldInfo field in type.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance))
                {
                    Assert.IsFalse(typeof(INpcBusinessCreationPort).IsAssignableFrom(field.FieldType),
                        $"{type.Name} holds an INpcBusinessCreationPort — observation/investigation must not reach creation.");
                }
            }
        }

        // ---------- G2: investigation ----------

        private NpcBusinessInvestigation StartInvestigation(Fixture f, NpcOpportunity opp, int day = 10)
        {
            var investigation = new NpcBusinessInvestigation();
            string refusal = investigation.Start("inv-1", opp, f.Scheduler, 120, day, f.Events, f.Diag);
            Assert.IsNull(refusal, "investigation start refused: " + refusal);
            return investigation;
        }

        private NpcOpportunity FlourOpportunity()
        {
            return new NpcOpportunity
            {
                OpportunityId = "opp-p42-0",
                RecognizerPersonId = 42,
                ItemOrServiceId = "flour",
                FirstObservedDayIndex = 3,
                LastObservedDayIndex = 5,
                ObservationCount = 3,
                DistinctWitnessDays = 2,
                EstimatedUnmetUnitsPerDay = 15,
                Status = NpcOpportunityStatus.Watch,
            };
        }

        private NpcInvestigationEvidence FullEvidence()
        {
            return new NpcInvestigationEvidence
            {
                CustomerObservationIds = new List<string> { "obs-p42-0", "obs-p42-1" },
                CandidateSuppliers = new List<NpcCandidateSupplier>
                {
                    new NpcCandidateSupplier { SupplierId = "grain-mill", ItemId = "flour", UnitPriceCents = 480, TermsNote = "weekly delivery" },
                },
                PremisesOptions = new List<NpcPremisesOption>
                {
                    new NpcPremisesOption { Description = "shop on Main", Tenure = NpcPremisesTenure.Rented, ParcelOrBuildingRef = "bldg-shop-3", AgreementRef = "lease-77", UpfrontCostCents = 20000, RecurringCostCents = 1500 },
                },
                EquipmentOptions = new List<NpcEquipmentOption>
                {
                    new NpcEquipmentOption { Description = "brick oven", ItemId = "oven", CostCents = 15000, Provenance = "purchase:smith-ledger-12" },
                },
                Financing = new NpcFinancingPlan { OwnCashCents = 100000 },
                InvestigatorRevenueEstimateCentsPerWeek = 9000,
                InvestigatorCostEstimateCentsPerWeek = 6000,
                EstimatedFormationCostCents = 70000,
            };
        }

        [Test]
        public void Investigation_WalkAway_WhenNoSupplier()
        {
            var f = new Fixture();
            var log = new NpcOpportunityObservationLog(42, 7);
            foreach (string obsId in new[] { "obs-p42-0", "obs-p42-1" })
            {
                log.RecordObservation(new NpcMarketObservation
                {
                    ObservationId = obsId, ObserverPersonId = 42, ObserverHouseholdId = 7, DayIndex = 3,
                    Kind = NpcObservationKind.OwnHouseholdUnfulfilledDemand, ItemOrServiceId = "flour",
                    UnmetUnitsPerDay = 10, EvidenceReference = "need:1",
                }, f.Events);
            }

            NpcBusinessInvestigation inv = StartInvestigation(f, FlourOpportunity());
            NpcInvestigationEvidence evidence = FullEvidence();
            evidence.CandidateSuppliers.Clear(); // no real supplier found

            Assert.IsNull(inv.Conclude(evidence, log, 11, f.Events, f.Diag));
            Assert.AreEqual(NpcInvestigationVerdict.WalkAway, inv.Verdict);
            Assert.IsTrue(inv.WalkAwayReasons.Count > 0);
            StringAssert.Contains("supplier", string.Join(" ", inv.WalkAwayReasons).ToLowerInvariant());
            Assert.AreEqual(1, f.Events.CountKind(NpcBusinessEventKind.InvestigationWalkAway),
                "The walk-away decision is recorded, never silent.");
        }

        [Test]
        public void Investigation_WalkAway_WhenCustomersInvented()
        {
            var f = new Fixture();
            var log = new NpcOpportunityObservationLog(42, 7); // no observations on the log
            NpcBusinessInvestigation inv = StartInvestigation(f, FlourOpportunity());
            NpcInvestigationEvidence evidence = FullEvidence();
            evidence.CustomerObservationIds = new List<string> { "obs-p42-999" }; // not on the log

            Assert.IsNull(inv.Conclude(evidence, log, 11, f.Events, f.Diag));
            Assert.AreEqual(NpcInvestigationVerdict.WalkAway, inv.Verdict,
                "Invented customers are refused — the NPC walks away.");
        }

        [Test]
        public void Investigation_WalkAway_WhenFinancingShort()
        {
            var f = new Fixture();
            var log = new NpcOpportunityObservationLog(42, 7);
            log.RecordObservation(new NpcMarketObservation
            {
                ObservationId = "obs-p42-0", ObserverPersonId = 42, ObserverHouseholdId = 7, DayIndex = 3,
                Kind = NpcObservationKind.OwnHouseholdUnfulfilledDemand, ItemOrServiceId = "flour",
                EvidenceReference = "need:1",
            }, f.Events);

            NpcBusinessInvestigation inv = StartInvestigation(f, FlourOpportunity());
            NpcInvestigationEvidence evidence = FullEvidence();
            evidence.CustomerObservationIds = new List<string> { "obs-p42-0" };
            evidence.Financing = new NpcFinancingPlan { OwnCashCents = 1000 }; // far short of 70000

            Assert.IsNull(inv.Conclude(evidence, log, 11, f.Events, f.Diag));
            Assert.AreEqual(NpcInvestigationVerdict.WalkAway, inv.Verdict);
            StringAssert.Contains("financing", string.Join(" ", inv.WalkAwayReasons).ToLowerInvariant());
        }

        [Test]
        public void Investigation_Proceed_WhenEveryAreaSupported()
        {
            var f = new Fixture();
            var log = new NpcOpportunityObservationLog(42, 7);
            foreach (string obsId in new[] { "obs-p42-0", "obs-p42-1" })
            {
                log.RecordObservation(new NpcMarketObservation
                {
                    ObservationId = obsId, ObserverPersonId = 42, ObserverHouseholdId = 7, DayIndex = 3,
                    Kind = NpcObservationKind.OwnHouseholdUnfulfilledDemand, ItemOrServiceId = "flour",
                    EvidenceReference = "need:1",
                }, f.Events);
            }

            NpcBusinessInvestigation inv = StartInvestigation(f, FlourOpportunity());
            Assert.IsNull(inv.Conclude(FullEvidence(), log, 11, f.Events, f.Diag));
            Assert.AreEqual(NpcInvestigationVerdict.Proceed, inv.Verdict);
            Assert.AreEqual(1, f.Events.CountKind(NpcBusinessEventKind.InvestigationProceed));
            Assert.IsTrue(inv.Findings.Count >= 5, "Every area leaves a finding.");
        }

        [Test]
        public void Investigation_CostsRealTime_DoubleBookingRefused()
        {
            var f = new Fixture();
            // P42 is already working 10:00-14:00 on day 10.
            Assert.IsNull(f.Scheduler.TryReserve(42, PersonActivityKind.Work, 10, 600, 240, "mill shift"));

            var inv = new NpcBusinessInvestigation();
            string refusal = inv.Start("inv-1", FlourOpportunity(), f.Scheduler, 120, 10, f.Events, f.Diag);
            Assert.IsNotNull(refusal, "Double-booking a Person's time must be refused loudly.");
            Assert.AreEqual(NpcInvestigationVerdict.Unspecified, inv.Verdict, "No investigation starts on refused time.");
        }

        [Test]
        public void Investigation_ReservesRealTime_WhenFree()
        {
            var f = new Fixture();
            NpcBusinessInvestigation inv = StartInvestigation(f, FlourOpportunity());
            Assert.IsFalse(string.IsNullOrWhiteSpace(inv.TimeReservationId));
            Assert.AreEqual(1, f.Events.CountKind(NpcBusinessEventKind.InvestigationTimeReserved));
        }

        // ---------- G3: formation ----------

        [Test]
        public void Formation_FullResourceAccounting_Conserved()
        {
            var f = new Fixture(100000);
            var service = new NpcBusinessFormationService();
            NpcBusinessFormationIntent intent = f.BakeryIntent();

            List<string> problems = service.Validate(intent, f.Context());
            Assert.AreEqual(0, problems.Count, "validation: " + string.Join("; ", problems));

            NpcBusinessFormationResult result = service.Execute(intent, f.Context());
            Assert.IsTrue(result.Success, "formation failed: " + result.FailureReason);
            Assert.IsNotNull(result.CreatedBusiness);
            Assert.AreEqual(42, result.CreatedBusiness.FounderPersonId, "Founder identity preserved as the NPC principal.");
            Assert.AreEqual("Marta", result.CreatedBusiness.OwnerName);

            // Conservation: 20000 premises + 15000 equipment + 25000 inventory + 10000 working capital = 70000.
            Assert.AreEqual(70000, result.HouseholdCashOutCents);
            Assert.AreEqual(20000, result.PremisesCostCents);
            Assert.AreEqual(15000, result.EquipmentCostCents);
            Assert.AreEqual(25000, result.InventoryCostCents);
            Assert.AreEqual(10000, result.WorkingCapitalCents);
            Assert.AreEqual(30000, f.Ledger.GetBalanceCents(), "100000 - 70000 = 30000: every cent traceable.");

            // The SAME generic flow shape: the port received the full intent.
            Assert.AreEqual(1, f.Port.CreateCalls);
            Assert.AreSame(intent, f.Port.LastIntent);

            // Hires through the real employment registry.
            Assert.AreEqual(1, result.EmploymentIds.Count);
            Assert.IsTrue(f.Employments.TryGetById(result.EmploymentIds[0], out EmploymentRelationship rel));
            Assert.AreEqual(43, rel.EmployeePersonId);
            Assert.AreEqual(EmploymentSource.NpcFormation, rel.Source);

            // Assets with provenance.
            Assert.AreEqual(3, f.Assets.Assets.Count);
            foreach (NpcFormationAsset asset in f.Assets.Assets)
            {
                Assert.IsFalse(string.IsNullOrWhiteSpace(asset.Provenance), "No asset without provenance.");
            }

            // Agreements recorded.
            Assert.AreEqual(2, result.AgreementIds.Count);

            // §26: the whole story is observable.
            Assert.IsTrue(f.Events.CountKind(NpcBusinessEventKind.FormationValidated) > 0);
            Assert.IsTrue(f.Events.CountKind(NpcBusinessEventKind.FormationCashMoved) > 0);
            Assert.IsTrue(f.Events.CountKind(NpcBusinessEventKind.FormationCreated) > 0);
            Assert.IsTrue(f.Events.CountKind(NpcBusinessEventKind.FormationReady) > 0);
        }

        [Test]
        public void Formation_MixedBusiness_OneIdentity_ManyCapabilities_NoTypeGate()
        {
            var f = new Fixture(150000);
            var service = new NpcBusinessFormationService();
            NpcBusinessFormationIntent intent = f.BakeryIntent();
            // A MIXED business: bakery + general store on one identity.
            intent.Activities.Add(new NpcBusinessActivitySpec
            {
                BusinessType = BusinessType.GeneralStore,
                CapabilityIds = new List<string> { "sell-general-goods" },
                RequiredRoleIds = new List<string> { "clerk" },
                RequiredStockItemIds = new List<string> { "flour" },
            });
            intent.Hires.Add(new NpcFormationHire { PersonId = 44, RoleId = "clerk", AgreedWeeklyWageCents = 700, StartDayIndex = 10 });

            List<string> problems = service.Validate(intent, f.Context());
            Assert.AreEqual(0, problems.Count, "A mixed business is NOT refused by type: " + string.Join("; ", problems));

            NpcBusinessFormationResult result = service.Execute(intent, f.Context());
            Assert.IsTrue(result.Success, result.FailureReason);
            CollectionAssert.AreEquivalent(
                new[] { "bake-bread", "sell-baked-goods", "sell-general-goods" },
                result.CreatedBusiness.CapabilityIds);
        }

        [Test]
        public void Formation_Aborted_WhenCreationRefused_CashRefunded_NoPartialState()
        {
            var f = new Fixture(100000);
            f.Port.RefuseNext = true;
            var service = new NpcBusinessFormationService();

            NpcBusinessFormationResult result = service.Execute(f.BakeryIntent(), f.Context());
            Assert.IsFalse(result.Success);
            Assert.IsTrue(result.CashRefunded, "Cash that moved must come back.");
            Assert.AreEqual(100000, f.Ledger.GetBalanceCents(), "No partial state: the household is whole.");
            Assert.AreEqual(1, f.Events.CountKind(NpcBusinessEventKind.FormationAborted));
            Assert.AreEqual(0, f.Employments.Count, "No hires registered on abort.");
        }

        [Test]
        public void Formation_Refused_WhenCashInsufficient()
        {
            var f = new Fixture(50000); // formation costs 70000
            var service = new NpcBusinessFormationService();
            List<string> problems = service.Validate(f.BakeryIntent(), f.Context());
            Assert.IsTrue(problems.Count > 0);
            StringAssert.Contains("insufficient funds", string.Join(" ", problems).ToLowerInvariant());

            NpcBusinessFormationResult result = service.Execute(f.BakeryIntent(), f.Context());
            Assert.IsFalse(result.Success);
            Assert.AreEqual(50000, f.Ledger.GetBalanceCents(), "Nothing moved.");
        }

        [Test]
        public void Formation_Refused_WhenAssetHasNoProvenance()
        {
            var f = new Fixture();
            var service = new NpcBusinessFormationService();
            NpcBusinessFormationIntent intent = f.BakeryIntent();
            intent.Assets[0].Provenance = string.Empty; // conjured oven

            List<string> problems = service.Validate(intent, f.Context());
            Assert.IsTrue(problems.Count > 0);
            StringAssert.Contains("provenance", string.Join(" ", problems).ToLowerInvariant());
        }

        [Test]
        public void Formation_Refused_WhenNotGenuinelyOperable()
        {
            var f = new Fixture();
            var service = new NpcBusinessFormationService();
            NpcBusinessFormationIntent intent = f.BakeryIntent();
            intent.Assets.RemoveAll(a => a.ItemId == "bread"); // opens with no bread to sell

            List<string> problems = service.Validate(intent, f.Context());
            Assert.IsTrue(problems.Count > 0, "Opening conditions must refuse an empty shop.");
            StringAssert.Contains("bread", string.Join(" ", problems));
        }

        [Test]
        public void SaveLoad_ObservationLog_RoundTrips_WithoutDuplication()
        {
            var events = new NpcBusinessEventLog();
            var log = new NpcOpportunityObservationLog(42, 7);
            Assert.IsNull(log.RecordObservation(new NpcMarketObservation
            {
                ObserverPersonId = 42, ObserverHouseholdId = 7, DayIndex = 3,
                Kind = NpcObservationKind.OwnHouseholdUnfulfilledDemand, ItemOrServiceId = "flour",
                EvidenceReference = "need:1",
            }, events));
            Assert.IsNull(log.RecordObservation(new NpcMarketObservation
            {
                ObserverPersonId = 42, ObserverHouseholdId = 7, DayIndex = 4,
                Kind = NpcObservationKind.WitnessedUnfulfilledDemand, ItemOrServiceId = "flour",
                EvidenceReference = "market-visit-a",
            }, events));
            log.RecognizeOpportunities(1, 1, 5, events, new List<string>());

            NpcOpportunityObservationLogSaveDto dto = log.CaptureSaveDto();
            var restored = new NpcOpportunityObservationLog();
            restored.LoadFromSaveDto(dto);

            Assert.AreEqual(2, restored.Observations.Count);
            Assert.AreEqual(1, restored.Opportunities.Count);
            Assert.AreEqual(42, restored.PersonId);

            // Loading twice does not duplicate.
            restored.LoadFromSaveDto(dto);
            Assert.AreEqual(2, restored.Observations.Count);
            Assert.AreEqual(1, restored.Opportunities.Count);
        }

        [Test]
        public void SaveLoad_EventLog_RoundTrips()
        {
            var log = new NpcBusinessEventLog();
            log.Record(10, NpcBusinessEventKind.FormationReady, 42, "biz-1", "ready to trade");
            log.Record(11, NpcBusinessEventKind.LifeEvaluated, 42, "biz-1", "healthy");

            var restored = new NpcBusinessEventLog();
            restored.LoadFromSaveDto(log.CaptureSaveDto());

            Assert.AreEqual(2, restored.Count);
            Assert.AreEqual(1, restored.CountKind(NpcBusinessEventKind.FormationReady));
            Assert.AreEqual(1, restored.ForBusiness("biz-1").Count - 1, "ForBusiness filters by business ref.");
            Assert.AreEqual(2, restored.ForPerson(42).Count);
        }
    }
}
