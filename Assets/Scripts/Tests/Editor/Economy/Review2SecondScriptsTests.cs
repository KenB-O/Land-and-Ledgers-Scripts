// Review 2 second scripts pass — bottleneck tracing A to Z with
// real meaningful changes, not a review. Every test below is tied
// to a traced bottleneck; no test here re-proves a link D1-D3
// already proved, and no value here is canon: every id, price,
// quantity, and conversion is a caller-supplied test parameter.
//
// WHAT THIS FILE PROVES, and what it deliberately does NOT:
//   - Freight: the driver-scan collection and the Pass 8
//     acceptance settlement guard EACH OTHER on the same
//     shipment object, in both orders. (D3 proved settlement
//     refuses a restored leg; nobody had proved the two live
//     mechanisms refuse each other.) Cross-instance copies —
//     two driver instances holding different objects for one
//     shipment id — are NOT guarded by the flag and are NOT
//     claimed here; that stays a detailed-only wiring rule.
//   - Save wiring: the D3 state's registry and the driver's
//     registry are joined by shared instance, so Capture reads
//     live driver truth and Restore repopulates the registry
//     the driver actually reads. The one-line Unity call site
//     (bind at bootstrap) is Codex work and is NOT claimed.
//   - Catalog: a conflicting restore diagnoses and keeps the
//     existing definition, AND feeding still works at the
//     caller-supplied conversion — because conversions live in
//     adapter constructor parameters, never in the catalog.
//     The catalog conflict can only conflate metadata
//     (display name, unit label, meal servings), never
//     feeding math or lot counts.
//   - Hiring: the full chain assessor -> brain intent ->
//     effort (the driver's step, performed explicitly here
//     and labelled as such) -> real inquiry -> real hire,
//     with the negative controls at each step.
//   - Phase H's 3 failures are NOT fixed here and NOT
//     re-reported as new: they are a policy fork (see the
//     review doc), and this pass refuses to pick a side
//     silently. What this pass DOES add for that fork is the
//     partial-fill attribution on the shopping trip result,
//     tested in HouseholdShoppingLoopTests.
//   - Unity compilation, Test Runner, PlayMode, scene wiring,
//     and the final Unity import GUID check are NOT claimed.
//   - No named-person fixture data: every person is a generic
//     test person.
using System.Collections.Generic;
using NUnit.Framework;
using LandLedgers.Animals;
using LandLedgers.Economy;
using LandLedgers.Economy.Businesses.GeneralStore;
using LandLedgers.Economy.Freight;
using LandLedgers.Economy.FreightFuel;
using LandLedgers.Economy.Labor;
using LandLedgers.Economy.Recruitment;
using LandLedgers.Persistence;
using LandLedgers.Population;
using LandLedgers.Primitives;

namespace LandLedgers.Tests.Economy
{
    public sealed class Review2SecondScriptsTests
    {
        // Caller-supplied test parameters. None is canon.
        private const string SsFeedItemId = "ss-test-horse-feed";
        private const string SsFeedCategoryId = "ss-feed-cat";
        private const string SsStoreBusinessId = "ss-general-store-1";
        private const int SsStoreUnitsPerFeedUnit = 2;
        private const int SsHouseholdUnitsPerFeedUnit = 3;

        private sealed class RuntimeCashPort : IBusinessCashPort
        {
            private readonly BusinessRuntimeState runtime;
            public RuntimeCashPort(BusinessRuntimeState runtime) { this.runtime = runtime; }
            public int BalanceCents => runtime != null ? runtime.CurrentCashCents : 0;
            public string TryDebit(int amountCents, string reason)
            {
                if (runtime == null) return "no runtime";
                return runtime.TrySpendCents(amountCents) ? null : "insufficient business cash";
            }
            public string Credit(int amountCents, string reason)
            {
                if (runtime == null) return "no runtime";
                runtime.AddCashCents(amountCents);
                return null;
            }
        }

        private static BusinessRuntimeState NewBusiness(string id, int cashCents)
        {
            var runtime = new BusinessRuntimeState();
            runtime.BindBusinessIdentity(id);
            runtime.SetCurrentCashCents(cashCents);
            return runtime;
        }

        // TryAcceptShipment stores the SAME object it accepts (no
        // copy), so returning it gives the tests the exact object
        // both collection mechanisms operate on — which is the
        // point of these tests.
        private static LogisticsShipmentState CompanyWithCompletedShipment(
            string shipmentId, string payerId, int chargeCents, List<string> diag,
            out FreightCompanyRuntime company)
        {
            company = new FreightCompanyRuntime("freight-co-1");
            var shipment = new LogisticsShipmentState
            {
                shipmentId = shipmentId,
                plannedQuantityUnits = 10,
                remainingQuantityUnits = 0,
                freightChargeCents = chargeCents,
                freightPayerBusinessInstanceId = payerId,
                haulingMode = ShipmentHaulingMode.HiredFreight,
                cargoClass = LogisticsCargoClass.PackagedGoods,
                state = LogisticsShipmentStatus.Completed,
                deliveryApplied = true,
            };
            Assert.IsTrue(company.TryAcceptShipment(shipment, diag), "test shipment must be accepted: " + string.Join("; ", diag));
            return shipment;
        }

        // ---------- freight: both collection mechanisms, both orders ----------

        [Test]
        public void Freight_SettlementThenDriverScan_SecondCollectionRefused_CashUnchanged()
        {
            var diag = new List<string>();
            LogisticsShipmentState shipment = CompanyWithCompletedShipment("ss-ship-a", "bakery-1", 250, diag, out FreightCompanyRuntime company);
            BusinessRuntimeState payer = NewBusiness("bakery-1", 1000);
            BusinessRuntimeState carrier = NewBusiness("freight-co-1", 0);
            Assert.IsNotNull(shipment);

            // First: the Pass 8 acceptance settlement.
            var settlement = new Series2CarrierSettlement();
            Series2CarrierSettlementResult settled = settlement.SettleOnAcceptance(
                shipment, new RuntimeCashPort(payer), new RuntimeCashPort(carrier), diag);
            Assert.IsTrue(settled.Settled, settled.Refusal);
            Assert.AreEqual(750, payer.CurrentCashCents, "settlement debits the payer exactly the charge");
            Assert.AreEqual(250, carrier.CurrentCashCents, "settlement credits the carrier exactly the charge");

            // Then: the driver tick scan on the SAME object must
            // find nothing uncollected — the flag is the shared
            // authority, not the per-mechanism set.
            int posted = company.CollectAndPostUncollectedRevenue(
                carrier, id => id == "bakery-1" ? payer : null, diag);
            Assert.AreEqual(0, posted, "the driver scan must not collect a leg the settlement already paid");
            Assert.AreEqual(750, payer.CurrentCashCents, "no second payer debit");
            Assert.AreEqual(250, carrier.CurrentCashCents, "no second carrier credit");
            Assert.AreEqual(0, company.FindUncollectedFreightChargeShipmentIds().Count);
        }

        [Test]
        public void Freight_DriverScanThenSettlement_SecondCollectionRefused_CashUnchanged()
        {
            var diag = new List<string>();
            LogisticsShipmentState shipment = CompanyWithCompletedShipment("ss-ship-b", "bakery-1", 250, diag, out FreightCompanyRuntime company);
            BusinessRuntimeState payer = NewBusiness("bakery-1", 1000);
            BusinessRuntimeState carrier = NewBusiness("freight-co-1", 0);
            Assert.IsNotNull(shipment);

            // First: the driver tick scan (verified: payer debited
            // before the carrier is credited).
            int posted = company.CollectAndPostUncollectedRevenue(
                carrier, id => id == "bakery-1" ? payer : null, diag);
            Assert.AreEqual(250, posted, "the verified scan collects the completed leg");
            Assert.AreEqual(750, payer.CurrentCashCents, "scan debits the payer exactly the charge");
            Assert.AreEqual(250, carrier.CurrentCashCents, "scan credits the carrier exactly the debit");

            // Then: the Pass 8 acceptance settlement on the SAME
            // object must refuse — the flag the scan set is the
            // authority the settlement checks.
            var settlement = new Series2CarrierSettlement();
            Series2CarrierSettlementResult again = settlement.SettleOnAcceptance(
                shipment, new RuntimeCashPort(payer), new RuntimeCashPort(carrier), diag);
            Assert.IsFalse(again.Settled, "settlement after a driver-scan collection is a double collection and must refuse");
            Assert.AreEqual(750, payer.CurrentCashCents, "no second payer debit");
            Assert.AreEqual(250, carrier.CurrentCashCents, "no second carrier credit");
        }

        [Test]
        public void Freight_LegacyCollect_WithoutPayerAuthorityRefusesSettlement()
        {
            var diag = new List<string>();
            LogisticsShipmentState shipment = CompanyWithCompletedShipment("ss-ship-c", "bakery-1", 250, diag, out FreightCompanyRuntime company);
            Assert.IsNotNull(shipment);
            int collected = company.CollectFreightCharge("ss-ship-c", diag);
            Assert.AreEqual(0, collected, "an unverified collection path must not settle the charge");
            string joined = string.Join(" ", diag);
            Assert.IsTrue(joined.Contains("verified till"),
                "the refusal should identify the missing payer authority: " + joined);
            Assert.IsFalse(shipment.FreightRevenueCollected,
                "the charge remains eligible for a later verified settlement");
            Assert.IsFalse(joined.Contains("from bakery-1 (external payer)"),
                "the old wording read as a verified debit from the payer and must be gone: " + joined);
        }

        // ---------- save wiring: one shared registry instance ----------

        private static BusinessRuntimeState NewSsStockedStore(Review2StoreFeedRegistry registry, int storeUnits)
        {
            var store = new BusinessRuntimeState();
            store.BindBusinessIdentity(SsStoreBusinessId);
            var diag = new List<string>();
            Assert.IsNull(Review2StoreHorseFeed.AddFeedItem(store, registry, new Review2StoreFeedItem
            {
                BusinessInstanceId = SsStoreBusinessId,
                FeedCategoryId = SsFeedCategoryId,
                FeedItemId = SsFeedItemId,
                DisplayName = "SS Test Feed",
                UnitLabel = "sack",
                StoreUnitsPerFeedUnit = SsStoreUnitsPerFeedUnit,
            }, diag), "owner add must succeed; diag: " + string.Join("; ", diag));
            Assert.AreEqual(storeUnits, store.AddCategoryStockUnits(SsFeedCategoryId, storeUnits),
                "stocking after the add must land exactly; the add itself creates no stock");
            return store;
        }

        private static AnimalState RegisterSsHorse(AnimalRegistry registry, string ownerId, AnimalOwnerKind ownerKind)
        {
            AnimalState horse = registry.RegisterAnimal(
                AnimalSpecies.Horse, "draft", AnimalSex.Female, ownerKind, ownerId, 0, "ss test horse");
            Assert.IsNotNull(horse);
            Assert.IsNull(HorseAuthority.Configure(horse, HorseUseCapabilities.Draft, "stable-1-yard", "stable-1", 0));
            return horse;
        }

        [Test]
        public void SaveWiring_BindDriverRegistry_CaptureReadsLiveDriverTruth()
        {
            // The driver registry is live truth: the owner added a
            // feed item through it, as SimulationDrivers does.
            var driverRegistry = new Review2StoreFeedRegistry();
            BusinessRuntimeState store = NewSsStockedStore(driverRegistry, 20);
            _ = store;

            // An unbound state captures nothing — this is the
            // dormant shape D3 left behind, proved, not assumed.
            var unbound = new Review2VerticalD3SaveState();
            Review2VerticalD3SaveDto unboundDto = unbound.Capture(new List<string>());
            Assert.AreEqual(0, unboundDto.StoreFeeds.Count, "an unbound state captures no store feeds");

            // Bind, then capture: the same state now sees the
            // driver's live registration, verbatim.
            var diag = new List<string>();
            var state = new Review2VerticalD3SaveState();
            Assert.IsNull(Review2SecondScriptsSaveWiring.BindDriverRegistry(state, driverRegistry, diag),
                "bind must succeed: " + string.Join("; ", diag));
            Assert.IsTrue(ReferenceEquals(state.FeedRegistry, driverRegistry), "binding is by shared instance, not a copy");
            Review2VerticalD3SaveDto dto = state.Capture(diag);
            Assert.AreEqual(1, dto.StoreFeeds.Count, "capture after binding must carry the driver's registration");
            Assert.AreEqual(SsStoreBusinessId, dto.StoreFeeds[0].BusinessInstanceId);
            Assert.AreEqual(SsStoreUnitsPerFeedUnit, dto.StoreFeeds[0].StoreUnitsPerFeedUnit);

            // Re-binding the same instance is a no-op, never an error.
            Assert.IsNull(Review2SecondScriptsSaveWiring.BindDriverRegistry(state, driverRegistry, diag),
                "idempotent re-bind must succeed");
        }

        [Test]
        public void SaveWiring_BindRefusesDifferentRegistryOverPopulatedState_NothingChanged()
        {
            var driverRegistry = new Review2StoreFeedRegistry();
            NewSsStockedStore(driverRegistry, 20);
            var state = new Review2VerticalD3SaveState();
            var diag = new List<string>();
            Assert.IsNull(Review2SecondScriptsSaveWiring.BindDriverRegistry(state, driverRegistry, diag));

            // A second, different registry must not silently
            // discard the bound registrations: refuse, change nothing.
            var otherRegistry = new Review2StoreFeedRegistry();
            string refusal = Review2SecondScriptsSaveWiring.BindDriverRegistry(state, otherRegistry, diag);
            Assert.IsNotNull(refusal, "binding over a populated state must refuse loudly");
            Assert.IsTrue(ReferenceEquals(state.FeedRegistry, driverRegistry), "a refused bind must leave the original binding intact");
            Assert.IsNotNull(state.FeedRegistry.Find(SsStoreBusinessId), "the original registration must survive the refused bind");

            // Null guards refuse without touching the state.
            Assert.IsNotNull(Review2SecondScriptsSaveWiring.BindDriverRegistry(null, driverRegistry, diag));
            Assert.IsNotNull(Review2SecondScriptsSaveWiring.BindDriverRegistry(state, null, diag));
            Assert.IsTrue(ReferenceEquals(state.FeedRegistry, driverRegistry));
        }

        [Test]
        public void SaveWiring_SharedInstance_RestoreRepopulatesTheRegistryTheDriverReads()
        {
            // Bootstrap order, as Codex must call it: bind first,
            // then save, then load into the same state object.
            var driverRegistry = new Review2StoreFeedRegistry();
            BusinessRuntimeState store = NewSsStockedStore(driverRegistry, 20);
            var state = new Review2VerticalD3SaveState();
            var diag = new List<string>();
            Assert.IsNull(Review2SecondScriptsSaveWiring.BindDriverRegistry(state, driverRegistry, diag));
            Review2VerticalD3SaveDto dto = state.Capture(diag);
            Assert.AreEqual(1, dto.StoreFeeds.Count);

            // Load: Restore mutates the shared registry in place
            // (clear-then-load), so the registry the driver reads
            // — this same object — now holds the restored entry,
            // with no re-bind step and no second registry.
            var restoreDiag = new List<string>();
            state.Restore(dto, restoreDiag);
            Assert.IsTrue(ReferenceEquals(state.FeedRegistry, driverRegistry), "restore must mutate in place, never replace the bound registry");
            Review2StoreFeedItem restoredFeed = driverRegistry.Find(SsStoreBusinessId);
            Assert.IsNotNull(restoredFeed, "the driver registry must resolve the restored feed item");
            Assert.AreEqual(SsStoreUnitsPerFeedUnit, restoredFeed.StoreUnitsPerFeedUnit);

            // And the restored registration still feeds, through
            // the driver registry object itself.
            var ids = new EntityIdRegistry();
            var animals = new AnimalRegistry(ids);
            AnimalState horse = RegisterSsHorse(animals, SsStoreBusinessId, AnimalOwnerKind.Business);
            horse.Nutrition01 = 0.70f;
            var careDiag = new List<string>();
            int unfed = HorseCareDriver.CareDay(animals,
                h => Review2StoreHorseFeed.FeedStockForHorse(h, _ => store, driverRegistry, careDiag),
                _ => false, 1, careDiag);
            Assert.AreEqual(0, unfed, "the store horse must be fed from the restored registration: " + string.Join("; ", careDiag));
            Assert.AreEqual(16, store.GetCategoryStock(SsFeedCategoryId).CurrentStockUnits,
                "idle ration is ceil(1.2) = 2 feed units x 2 store units = 4 store units");
        }

        // ---------- catalog: conflict cannot corrupt feeding math ----------

        [Test]
        public void Catalog_ConflictingRestore_FeedingStillWorksAtCallerConversion()
        {
            // Register the item first (idempotent: an identical
            // re-registration in this process returns null).
            var diag = new List<string>();
            var state = new Review2VerticalD3SaveState();
            Assert.IsNull(state.RegisterRuntimeItem(
                new HouseholdItemDefinition(SsFeedItemId, "SS Test Feed", "sack", SsFeedCategoryId, 1, false), diag),
                "registration must succeed: " + string.Join("; ", diag));

            // A save carrying a conflicting definition for the
            // same id: diagnosed, existing definition wins.
            var conflicting = new Review2VerticalD3SaveDto();
            conflicting.RuntimeItems.Add(new Review2D3RuntimeItemDto
            {
                ItemId = SsFeedItemId, DisplayName = "Different SS Feed", UnitLabel = "sack",
                ReserveCategoryId = SsFeedCategoryId, MealServingUnits = 1, IsFood = false,
            });
            var restored = new Review2VerticalD3SaveState();
            var restoreDiag = new List<string>();
            restored.Restore(conflicting, restoreDiag);
            Assert.AreEqual("SS Test Feed", HouseholdItemCatalog.Get(SsFeedItemId).DisplayName,
                "the existing catalog definition wins over the conflicting restore");
            Assert.IsTrue(string.Join("; ", restoreDiag).Contains("not re-registered"),
                "the conflict must be diagnosed: " + string.Join("; ", restoreDiag));

            // The conflict cannot corrupt feeding: conversions
            // live in adapter constructor parameters, never in
            // the catalog, and lots are per-inventory instance
            // state. AddLot still accepts the id, and an adapter
            // built with the caller conversion 3 draws exactly
            // 3 household units per feed unit.
            var inventory = new HouseholdInventory(7);
            Assert.IsNull(inventory.AddLot(SsFeedItemId, 6, 1, "test purchase", "", "", -1),
                "the known item must still accept lots after a conflicting restore");
            var ids = new EntityIdRegistry();
            var animals = new AnimalRegistry(ids);
            AnimalState horse = RegisterSsHorse(animals, "person:1", AnimalOwnerKind.Person);
            horse.Nutrition01 = 0.70f;
            var personFeed = new Review2HouseholdHorseFeed(inventory, SsFeedItemId, SsHouseholdUnitsPerFeedUnit, 1);
            var careDiag = new List<string>();
            int unfed = HorseCareDriver.CareDay(animals, h => h == horse ? personFeed : null, _ => false, 1, careDiag);
            Assert.AreEqual(0, unfed, "the person-owned horse must be fed after the conflicting restore: " + string.Join("; ", careDiag));
            Assert.AreEqual(0, inventory.GetAvailableUnits(SsFeedItemId),
                "idle ration is 2 feed units x 3 household units = 6 units drawn, exactly");
        }

        // ---------- hiring: the full chain, with negative controls ----------

        [Test]
        public void Hiring_AssessorToBrainToEffortToInquiryToHire_FullChainWithControls()
        {
            var diag = new List<string>();
            var assessor = new LaborDemandAssessor();
            LaborDemandReport report = assessor.Assess(new LaborDemandInputs
            {
                BusinessInstanceId = "bakery-1",
                DayIndex = 1,
                OpenTaskMinutesByFamily = new Dictionary<string, int> { { "Production", 2400 } },
                WaitingCustomers = 6,
                RecentBalkCount = 3,
                RequiresPersonPhaseMinutes = 2400,
                ActiveEmployeeCount = 0,
                AvailableEmployeeMinutes = 0,
            }, diag);
            Assert.IsTrue(report.HiringPressure, "missed work with no workers is hiring-pressure evidence, not a quota");

            var brain = new ManagerBrain();
            ManagerDecisionResult decision = brain.DailyCycle(new ManagerCycleInputs
            {
                BusinessInstanceId = "bakery-1",
                ManagerPersonId = 3,
                CashCents = 10000,
                DemandReport = report,
                DelegatedScopes = new List<string> { ManagerScopeNames.Staffing },
                DayIndex = 1,
                JudgmentSeed = 42,
                JudgmentQuality01 = 1f,
                PurchaseLimitCents = 2000,
                MaxBorrowingCents = 10000,
                Instructions = new List<StandingInstruction>
                {
                    new StandingInstruction
                    {
                        InstructionId = "ss-hire-1",
                        BusinessInstanceId = "bakery-1",
                        Kind = InstructionKind.ManagerJudgment,
                        TargetActKind = ManagerActKinds.OpenRecruitment,
                        Objective = "keep the bakery staffed as work goes unmet",
                        MaxSpendCentsPerAct = -1,
                        CreatedByPersonId = 7,
                        CreatedDayIndex = 0,
                    },
                },
            }, diag);
            Assert.IsTrue(decision.Acted, "the brain must act on hiring pressure within Staffing scope");
            Assert.AreEqual(ManagerActKinds.OpenRecruitment, decision.ActionKind);

            // Negative control 1: the intent alone creates nothing.
            var recruitment = new RecruitmentService();
            var employment = new EmploymentRelationshipRegistry();
            Assert.AreEqual(0, recruitment.Efforts.Count, "an intent string is not an effort");
            Assert.AreEqual(0, employment.GetByEmployer("bakery-1").Count, "no employment relationship exists from an intent");

            // The driver step, performed explicitly here and
            // labelled as such: SimulationDrivers
            // .ExecuteManagerRecruitment opens a WordOfMouth
            // effort from the brain's intent. That method is
            // MonoBehaviour code and is NOT Roslyn-provable;
            // this call is the same service call it makes, so
            // the chain below proves everything downstream of it.
            RecruitmentEffort effort = recruitment.OpenEffort(
                "bakery-1", "Baker", RecruitmentChannel.WordOfMouth, 1, 42, diag);
            Assert.IsNotNull(effort, "the driver-step effort must open: " + string.Join("; ", diag));
            Assert.AreEqual(0, effort.CostCents, "word of mouth costs nothing, unlike the handbill channel");
            Assert.AreEqual(1, recruitment.Efforts.Count, "exactly one effort exists after the explicit open");

            // Negative control 2: an open effort is not a hire.
            Assert.AreEqual(0, employment.GetByEmployer("bakery-1").Count, "an open effort hires nobody");

            // A real candidate — a generic test adult, never a
            // named fixture person — applies in person.
            var population = new PopulationState();
            population.people.Add(new PersonState
            {
                id = 3, firstName = "Test", lastName = "Applicant", age = 30,
                ageBand = AgeBand.Adult18Plus, laborAccessLevel = LaborAccessLevel.FullLaborMarket,
                householdId = 2, professionId = "laborer",
            });
            CandidateInquiry inquiry = recruitment.ApplyForEffort(
                effort.EffortId, 3, population, employment, "word of mouth", 2, diag);
            Assert.IsNotNull(inquiry, "a real adult must be able to apply: " + string.Join("; ", diag));
            Assert.AreEqual(InquiryStatus.Open, inquiry.Status);
            Assert.AreEqual(0, employment.GetByEmployer("bakery-1").Count, "an open inquiry is still not a hire");

            // Acceptance creates the one real relationship.
            string hireProblem = recruitment.HireFromInquiry(
                inquiry.InquiryId, 600, EmploymentKind.Permanent, 3, employment, diag);
            Assert.IsNull(hireProblem, "the hire must succeed: " + hireProblem);
            List<EmploymentRelationship> relationships = employment.GetByEmployer("bakery-1");
            Assert.AreEqual(1, relationships.Count, "exactly one employment relationship exists after acceptance");
            Assert.AreEqual(3, relationships[0].EmployeePersonId);
            Assert.AreEqual("bakery-1", relationships[0].EmployerBusinessId);
            Assert.AreEqual(EmploymentLifecycleState.Active, relationships[0].LifecycleState);
            Assert.AreEqual(InquiryStatus.Hired, inquiry.Status, "the inquiry closes as hired");
            Assert.IsTrue(effort.Closed, "the effort closes on hire");

            // Negative controls 3 and 4: the same inquiry cannot
            // hire twice, and an unknown inquiry hires nobody.
            Assert.IsNotNull(recruitment.HireFromInquiry(
                inquiry.InquiryId, 600, EmploymentKind.Permanent, 3, employment, diag),
                "a second hire from a closed inquiry must refuse");
            Assert.IsNotNull(recruitment.HireFromInquiry(
                "no-such-inquiry", 600, EmploymentKind.Permanent, 3, employment, diag),
                "an unknown inquiry must refuse");
            Assert.AreEqual(1, employment.GetByEmployer("bakery-1").Count, "refused hires must not create relationships");
        }
    }
}
