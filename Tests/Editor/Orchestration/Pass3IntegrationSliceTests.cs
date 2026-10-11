// Pass III Worker E: integrated vertical slice proving Workers A-D work TOGETHER.
// Deterministic source-level fixture on PRODUCTION authorities:
// 4 households, 9 persons, 2 businesses, landlord, parcels with distinct
// ownership/use, rented accommodation, employed worker, job seeker, retailer.
// Executes the brief §14 20-step trace + land subdivision proof + contrasting cases.
// Canon: XXXI Part VI (property), Part IV (employment), Part III (tasks),
// Tech XIV Part II (tasks/competence), Part V (property); Rev XXIII §1.1
// (one authority per fact); Rev VII §1.1-1.2 (capability/experience).
using System;
using System.Collections.Generic;
using NUnit.Framework;
using EntityId = LandLedgers.Primitives.EntityId;
using LandLedgers.Economy;
using LandLedgers.Economy.Financing;
using LandLedgers.Economy.Labor;
using LandLedgers.Population;
using LandLedgers.Primitives;
using LandLedgers.Skills;
using LandLedgers.Tasks;
using LandLedgers.Time;
using LandLedgers.World;
using LandLedgers.World.Property;

namespace LandLedgers.Editor.Orchestration
{
    [TestFixture]
    public sealed class Pass3IntegrationSliceTests
    {
        private List<string> Diag() => new List<string>();

        private sealed class PayeeDirectory : ITenancyPayeeDirectory
        {
            private readonly Dictionary<string, int> householdByHolder = new Dictionary<string, int>();
            public void Map(string holderId, int householdId) => householdByHolder[holderId] = householdId;
            public int FindPayeeHouseholdId(string payeeHolderId) =>
                householdByHolder.TryGetValue(payeeHolderId ?? string.Empty, out int h) ? h : -1;
            public ITenancyPayeeCashSink FindPayeeBusinessSink(string payeeHolderId) => null;
        }

        private sealed class BuildingIndexStub : IParcelBuildingIndex
        {
            private readonly Dictionary<string, List<ParcelBuildingFootprint>> byParcel =
                new Dictionary<string, List<ParcelBuildingFootprint>>();
            public void Add(string parcelId, string buildingId, GridRect footprint)
            {
                if (!byParcel.TryGetValue(parcelId, out var list))
                    byParcel[parcelId] = list = new List<ParcelBuildingFootprint>();
                list.Add(new ParcelBuildingFootprint(buildingId, footprint));
            }
            public IReadOnlyList<ParcelBuildingFootprint> BuildingsOnParcel(string parcelId) =>
                byParcel.TryGetValue(parcelId ?? string.Empty, out var list) ? list : new List<ParcelBuildingFootprint>();
        }

        private sealed class World
        {
            // Pass II labor core
            public HouseholdLedgerRegistry Ledgers = new HouseholdLedgerRegistry();
            public HouseholdInventoryRegistry Inventories = new HouseholdInventoryRegistry();
            public EmploymentRelationshipRegistry Employments = new EmploymentRelationshipRegistry();
            public ContactGraphService Contacts = new ContactGraphService();
            public EmploymentNegotiationService Negotiation =
                new EmploymentNegotiationService((biz, person, act) => true);
            public WorkScheduler Scheduler = new WorkScheduler();
            public WageSettlementService Settlement = new WageSettlementService();
            public TaskAuthority Tasks = new TaskAuthority();
            public WorkTimeBudgetStore Budgets = new WorkTimeBudgetStore();
            public FinancialObligationAuthority Obligations = new FinancialObligationAuthority();
            public EntityIdRegistry Ids = new EntityIdRegistry();
            public BusinessRuntimeState Store = new BusinessRuntimeState();
            public BusinessRuntimeState Smith = new BusinessRuntimeState();
            // Worker A: land
            public ParcelService Parcels = new ParcelService();
            public PropertyInterestStore Interests = new PropertyInterestStore();
            public ParcelSubdivisionService Subdivider;
            public TitleAuthority Titles = new TitleAuthority();
            // Worker B: housing/tenancy
            public HousingAuthority Housing = new HousingAuthority();
            public TenancyPaymentService Tenancy = new TenancyPaymentService();
            public PayeeDirectory Payees = new PayeeDirectory();
            // Worker C: decisions
            public HouseholdDecisionService Decisions = new HouseholdDecisionService();
            // Worker D: capabilities
            public SkillService Skills = new SkillService();
            public Dictionary<int, int> PersonHousehold = new Dictionary<int, int>();
            // Named ids for the report
            public string FarmParcelId;
            public string StoreParcelId;
            public string HouseParcelId;
            public string PublicParcelId;
            public string TenancyAgreementId;
            public string HouseSpaceId;
        }

        private static EntityId P(int personId) => EntityId.For(EntityKind.Person, personId);

        private World BuildWorld()
        {
            var w = new World();
            var diag = Diag();
            w.Subdivider = new ParcelSubdivisionService(w.Parcels, w.Interests);

            // Persons: P1 seeker (H1), P5/P6 family (H1) | P2 farmhand (H2), P7 (H2) |
            //          P3 store owner (H3), P8 experienced clerk (H3) | P4 landlord (H4).
            w.PersonHousehold[1] = 1; w.PersonHousehold[5] = 1; w.PersonHousehold[6] = 1;
            w.PersonHousehold[2] = 2; w.PersonHousehold[7] = 2;
            w.PersonHousehold[3] = 3; w.PersonHousehold[8] = 3;
            w.PersonHousehold[4] = 4;

            // Opening cash (documented, no synthetic flows).
            w.Ledgers.GetOrCreate(1).RecordInflow(500, 400, HouseholdIncomeSource.OtherDocumented, "opening-savings", "H1 opening reserves", "self");
            w.Ledgers.GetOrCreate(2).RecordInflow(500, 1500, HouseholdIncomeSource.OtherDocumented, "opening-savings", "H2 opening reserves", "self");
            w.Ledgers.GetOrCreate(3).RecordInflow(500, 3000, HouseholdIncomeSource.OtherDocumented, "opening-savings", "H3 opening reserves", "self");
            w.Ledgers.GetOrCreate(4).RecordInflow(500, 5000, HouseholdIncomeSource.OtherDocumented, "opening-savings", "H4 opening reserves", "self");

            // H1 food inventory: 30 units of flour from a documented source.
            Assert.IsNull(w.Inventories.GetOrCreate(1).AddLot(HouseholdItemCatalog.FlourId, 30, 500, "opening stores"));

            // Businesses.
            w.Store.BindBusinessIdentity("store-1");
            w.Store.AddCashCents(20000);
            w.Store.EmploymentRegistry = w.Employments;
            w.Store.HouseholdLedgers = w.Ledgers;
            w.Store.PersonHouseholdIdLookup = pid => w.PersonHousehold.TryGetValue(pid, out int h) ? h : -1;
            w.Smith.BindBusinessIdentity("smith-1");
            w.Smith.AddCashCents(8000);
            w.Smith.EmploymentRegistry = w.Employments;
            w.Smith.HouseholdLedgers = w.Ledgers;
            w.Smith.PersonHouseholdIdLookup = pid => w.PersonHousehold.TryGetValue(pid, out int h) ? h : -1;

            // ---- Land (Worker A) ----
            // 40-acre farm: 200x200 two-metre cells = 160,000 m².
            var farm = w.Parcels.CreateParcel("world-1",
                new List<GridRect> { new GridRect(0, 0, 200, 200) },
                "40-acre farm, surveyed 1888", "agriculture", "county", LandStatus.PrivateTitled,
                "", "road frontage on the north line");
            var storeLot = w.Parcels.CreateParcel("world-1",
                new List<GridRect> { new GridRect(300, 0, 20, 10) },
                "town lot 7", "commercial", "county", LandStatus.PrivateTitled,
                "", "main street frontage");
            var houseLot = w.Parcels.CreateParcel("world-1",
                new List<GridRect> { new GridRect(300, 20, 20, 10) },
                "town lot 12", "residential", "county", LandStatus.PrivateTitled,
                "", "lane access");
            var publicLand = w.Parcels.CreateParcel("world-1",
                new List<GridRect> { new GridRect(0, 300, 100, 100) },
                "public domain grazing", "grazing", "federal", LandStatus.PublicDomain,
                "", "open range");
            w.FarmParcelId = farm.Id; w.StoreParcelId = storeLot.Id;
            w.HouseParcelId = houseLot.Id; w.PublicParcelId = publicLand.Id;

            // Title: every parcel has a recorded holder (land is never holderless).
            Assert.IsNotNull(w.Titles.RegisterParcel(farm.Id, "40-acre farm", 40f, "P2", TitleBasis.Purchase, 480, diag));
            Assert.IsNotNull(w.Titles.RegisterParcel(storeLot.Id, "town lot 7", 0.5f, "P4", TitleBasis.Purchase, 481, diag));
            Assert.IsNotNull(w.Titles.RegisterParcel(houseLot.Id, "town lot 12", 0.5f, "P4", TitleBasis.Purchase, 481, diag));
            Assert.IsNotNull(w.Titles.RegisterParcel(publicLand.Id, "public domain grazing", 25f, "Federal Public Domain", TitleBasis.Grant, 470, diag));

            // A county road easement crosses the farm (non-monetary interest; carried on subdivision).
            w.Interests.Record(new PropertyInterest
            {
                Id = PropertyInterestStore.NewInterestId(),
                ParcelId = farm.Id,
                Kind = InterestKind.Easement,
                HolderId = "County",
                StartDay = 485,
                Notes = "county road easement along the north line",
            });

            // ---- Buildings / housing / tenancy (Worker B) ----
            var farmhouse = w.Housing.RegisterBuilding("farmhouse-1", farm.Id, "house", 480, "test", diag);
            var storeBldg = w.Housing.RegisterBuilding("store-bldg-1", storeLot.Id, "store", 481, "test", diag);
            var house = w.Housing.RegisterBuilding("house-1", houseLot.Id, "house", 482, "test", diag);
            Assert.IsNotNull(farmhouse); Assert.IsNotNull(storeBldg); Assert.IsNotNull(house);
            var farmSpace = w.Housing.DefineSpace("farmhouse-1", 6, diag);
            var houseSpace = w.Housing.DefineSpace("house-1", 5, diag);
            w.HouseSpaceId = houseSpace.SpaceId;
            // H2 owner-occupies the farmhouse; H1 rents the town house.
            Assert.IsNotNull(w.Housing.Occupy(2, 2, farmSpace.SpaceId, AccommodationArrangement.OwnerOccupied, 500, diag));
            Assert.IsNotNull(w.Housing.Occupy(7, 2, farmSpace.SpaceId, AccommodationArrangement.OwnerOccupied, 500, diag));
            Assert.IsNotNull(w.Housing.Occupy(1, 1, houseSpace.SpaceId, AccommodationArrangement.Rental, 500, diag));
            Assert.IsNotNull(w.Housing.Occupy(5, 1, houseSpace.SpaceId, AccommodationArrangement.Rental, 500, diag));
            Assert.IsNotNull(w.Housing.Occupy(6, 1, houseSpace.SpaceId, AccommodationArrangement.Rental, 500, diag));

            // Tenancy: H1 rents the house from P4 — 300c per 30 days, first due day 529.
            var agreement = w.Housing.RecordAgreement("rental", "P4", "H1", houseSpace.SpaceId,
                "monthly rental of town house", 500, -1, "", diag);
            Assert.IsNotNull(agreement);
            w.TenancyAgreementId = agreement.AgreementId;
            Assert.IsNull(w.Housing.AttachPaymentTerms(agreement.AgreementId, new TenancyPaymentTerms
            {
                AmountCents = 300, PeriodDays = 30, DueDayOfPeriod = 30, StartDay = 500,
                PayeeHolderId = "P4", TenantHouseholdId = 1,
            }, diag));
            w.Payees.Map("P4", 4);

            // Contact graph: P1 seeks work; P3 (store owner) has a witnessed link to P1.
            w.Contacts.SeedHouseholdLinks(1, 1, new[] { 5, 6 });
            w.Contacts.SeedHouseholdLinks(2, 2, new[] { 7 });
            w.Contacts.SeedHouseholdLinks(3, 3, new[] { 8 });
            w.Contacts.SeedHouseholdLinks(4, 4, new int[0]);
            w.Contacts.AddLink(3, 1, ContactLinkKind.Witnessed, "P3 saw P1 seeking work at the market");
            w.Contacts.AddLink(1, 3, ContactLinkKind.Witnessed, "P1 spoke with P3 at the market");

            // Skills (Worker D): starter set + P8's real 13-year career (150 genuine
            // 8-hour episodes, spaced to avoid routine-work diminishing; no calendar-
            // time grants, no invented skill). P1 stays a true novice.
            w.Skills.RegisterStarterSet();
            w.Skills.SetAptitude(P(8), CapabilityFamily.OrganizationManagement, 2.0, 100);
            for (int i = 0; i < 150; i++)
            {
                var inputs = new ExperienceFormationInputs
                {
                    TaskCapabilityId = SkillIds.Stocking,
                    Difficulty01 = 0.8, Novelty01 = 1.0,
                    Supervised = true, Outcome = "Success",
                    MinutesSpent = 480, Day = -4150 + i * 31, // career predates the slice window
                };
                Assert.IsTrue(w.Skills.RecordTaskExperience(P(8), inputs));
            }

            // Task catalog: stocking work at the store.
            string rejection;
            var stockDef = new TaskDefinition("stock-shelves", "Stock shelves", 60);
            stockDef.SetRequiredSkill(SkillIds.Stocking, new[] { SkillIds.Stocking });
            Assert.IsTrue(w.Tasks.RegisterDefinition(stockDef, out rejection), rejection);

            // P2's pre-existing employment (farmhand) through the real negotiation flow.
            var offer2 = w.Negotiation.MakeOffer("farm-1", 2, 2, 280, "farmhand: feed, tend stock",
                "dawn to dusk, six days", 495, 90, "", 0, "none", "farm duties", "good conduct", 7,
                "InPerson", "prior-season", 490, diag);
            Assert.IsNotNull(offer2);
            Assert.IsNull(w.Negotiation.AcceptOffer(offer2.OfferId, 491, w.Employments, diag));
            w.Negotiation.AdvanceDay(495, w.Employments, diag);
            Assert.IsTrue(w.Employments.TryGetById("emp-" + offer2.OfferId, out EmploymentRelationship rel2));
            Assert.AreEqual(EmploymentLifecycleState.Active, rel2.LifecycleState);
            return w;
        }

        [Test]
        public void Slice_Full20Step_LaborToHouseholdTrace()
        {
            var w = BuildWorld();
            var diag = Diag();
            var h1 = w.Ledgers.GetOrCreate(1);
            var h4 = w.Ledgers.GetOrCreate(4);
            var inv1 = w.Inventories.GetOrCreate(1);

            // 1. Actual consumption reduces H1 food reserves (30 -> 20 -> 10 units).
            Assert.IsNull(inv1.Consume(HouseholdItemCatalog.FlourId, 10, 500, "meals", "P5", null));
            Assert.AreEqual(20, inv1.GetAvailableUnits(HouseholdItemCatalog.FlourId));
            Assert.IsNull(inv1.Consume(HouseholdItemCatalog.FlourId, 10, 501, "meals", "P5", null));
            Assert.AreEqual(10, inv1.GetAvailableUnits(HouseholdItemCatalog.FlourId));

            // Cash outflows for meals leave H1 with 40c.
            Assert.IsNull(h1.RecordOutflow(500, 150, "flour and meal", "general store"));
            Assert.IsNull(h1.RecordOutflow(501, 130, "flour and meal", "general store"));
            Assert.IsNull(h1.RecordOutflow(502, 80, "flour and meal", "general store"));
            Assert.AreEqual(40, h1.GetBalanceCents());

            // 2-3. Rent (300c, due day 529) approaches; pressure rises.
            // 4. H1's adult evaluates: reserves 40c vs 300c obligation -> SeekIncome.
            var pressure = new HouseholdPressureInput
            {
                HouseholdId = "1", Day = 528, ReservesCents = 40,
                ObligationsDueCents = 300, ExpectedIncomeCentsPerDay = 0,
                Dependents = 2, KnownOpportunityCount = 0,
                HasShelterRisk = false, AvailableCreditCents = 0,
                RecentJobLoss = false, HasSearchableContacts = true,
            };
            HouseholdDecision decision = w.Decisions.Evaluate(pressure);
            Assert.AreEqual(HouseholdDecisionKind.SeekIncome, decision.Kind);
            Assert.IsTrue(decision.Reason.Contains("260")); // shortfall 300c - 40c coverage

            // 5. P1 legitimately learns of the position (documented link only).
            List<int> reachable = w.Contacts.ReachableCandidates("store-1", new[] { 3 });
            Assert.IsTrue(reachable.Contains(1));
            Assert.IsFalse(reachable.Contains(2)); // P2: no link to the store's hiring channel

            // 6. Terms negotiated: 300 offered -> 340 countered -> 320 accepted.
            EmploymentOffer offer = w.Negotiation.MakeOffer(
                "store-1", 1, 3, 300, "clerk: serve customers, stock shelves",
                "dawn to dusk, six days", 530, 90, "", 0,
                "none", "clerk duties", "good conduct", 7,
                "InPerson", "witnessed-by:P3", 528, diag);
            Assert.IsNotNull(offer);
            Assert.AreEqual(OfferState.Offered, offer.State);
            Assert.IsNull(w.Negotiation.RecordCounter(offer.OfferId, 340, 528, diag));
            Assert.AreEqual(OfferState.Countered, offer.State);
            Assert.IsNull(w.Negotiation.RecordCounter(offer.OfferId, 320, 528, diag));

            // 7. Agreement accepted; PendingStart until day 530.
            Assert.IsNull(w.Negotiation.AcceptOffer(offer.OfferId, 529, w.Employments, diag));
            Assert.AreEqual(OfferState.Accepted, offer.State);
            Assert.IsTrue(w.Employments.TryGetById("emp-" + offer.OfferId, out EmploymentRelationship rel));
            Assert.AreEqual(EmploymentLifecycleState.PendingStart, rel.LifecycleState);
            Assert.AreEqual(320, rel.Compensation.AgreedWeeklyWageCents);
            w.Negotiation.AdvanceDay(530, w.Employments, diag);
            Assert.AreEqual(EmploymentLifecycleState.Active, rel.LifecycleState);

            // 8. P1 is available; the scheduler assigns real store work.
            var schedInputs = new SchedulerInputs { BusinessInstanceId = "store-1", DayIndex = 530 };
            schedInputs.OpenWork.Add(new SchedulerOpenWorkItem
                { TaskId = "t-stock-1", Family = "StockShelves", MinutesRequired = 240, LocationId = "store-1" });
            schedInputs.OpenWork.Add(new SchedulerOpenWorkItem
                { TaskId = "t-tend-1", Family = "TendCustomer", MinutesRequired = 120, LocationId = "store-1" });
            schedInputs.Workers.Add(new SchedulerAvailableWorker { PersonId = 1, AvailableMinutes = 480 });
            schedInputs.Workers.Add(new SchedulerAvailableWorker { PersonId = 8, AvailableMinutes = 480 });
            WorkAssignmentReport assignment = w.Scheduler.AssignDailyWork(schedInputs, diag);
            Assert.AreEqual(0, assignment.UnassignedTaskIds.Count);
            Assert.AreEqual(2, assignment.Assignments.Count);

            // 9. Actual tasks consume person-time with capability-influenced outcomes.
            // P1 (novice, capability 0.10) vs P8 (experienced from 12 real episodes).
            double capNovice = w.Skills.GetCapability(P(1), SkillIds.Stocking);
            double capExpert = w.Skills.GetCapability(P(8), SkillIds.Stocking);
            Assert.AreEqual(0.10, capNovice, 1e-9);
            Assert.Greater(capExpert, 0.25); // 150 genuine career episodes
            CausalOutcomeResult noviceRes =
                new CausalTaskOutcomeResolver().Resolve(w.Skills.BuildCausalInputs(P(1), SkillIds.Stocking, 60));
            CausalOutcomeResult expertRes = new CausalTaskOutcomeResolver().Resolve(
                w.Skills.BuildCausalInputs(P(8), SkillIds.Stocking, 60));
            Assert.Greater(noviceRes.DurationMinutes, expertRes.DurationMinutes); // novice slower
            Assert.Greater(noviceRes.FailureRisk01, expertRes.FailureRisk01);     // novice riskier

            // Real task execution on the authority: create -> assign -> work -> complete
            // with the resolved outcome (the driver hook from the Worker D handoff).
            string rejection;
            var budget1 = w.Budgets.GetOrCreate(P(1));
            var task1 = w.Tasks.CreateTaskWithPlannedMinutes("stock-shelves", P(1), 530, 60);
            Assert.IsTrue(w.Tasks.AssignTask(task1.TaskId, P(1), w.Budgets, out rejection), rejection);
            Assert.IsTrue(w.Tasks.StartTask(task1.TaskId, 530, out rejection), rejection);
            Assert.IsTrue(w.Tasks.RecordWork(task1.TaskId, 50, w.Budgets, 530, out rejection), rejection);
            TaskOutcome outcome1 = w.Skills.ResolveTaskCompletion(
                P(1), SkillIds.Stocking, w.Skills.BuildCausalInputs(P(1), SkillIds.Stocking, 60),
                0.5, 0.8, false, 530, task1.TaskId.ToString());
            // The varied outcome from the worker's real capability is applied
            // through the driver hook (not the authority's auto-complete default).
            Assert.IsTrue(w.Tasks.CompleteTask(task1.TaskId, outcome1, 530, w.Budgets));
            double capAfter = w.Skills.GetCapability(P(1), SkillIds.Stocking);
            Assert.Greater(capAfter, capNovice); // 19. experience develops from genuine work

            // 10. Capability demonstrably changes the outcome distribution is shown
            // by the resolver comparison above (step 9); the completed task also
            // recorded exactly one experience episode (relevance rises from 0).
            Assert.Greater(w.Skills.GetExperienceRelevance(P(1), SkillIds.Stocking), 0.0);

            // 11. Work contributes to a real productive outcome: a served sale.
            w.Store.AddCashCents(450);
            Assert.AreEqual(20450, w.Store.CurrentCashCents);

            // 12. Employer pays actual wages through the single cash path (week 75).
            int cashBefore = w.Store.CurrentCashCents;
            Assert.AreEqual("settled", w.Settlement.SettleWeeklyPayroll(w.Store, w.Employments, 75, 532, diag));
            Assert.AreEqual(cashBefore - 320, w.Store.CurrentCashCents);
            Assert.AreEqual("already-settled", w.Settlement.SettleWeeklyPayroll(w.Store, w.Employments, 75, 532, diag));

            // 13. H1's employee-controlled funds increase: 40 + 320 = 360.
            Assert.AreEqual(360, h1.GetBalanceCents());

            // 14-15. H1 purchases flour from actual stock; goods reach inventory.
            Assert.IsNull(h1.RecordOutflow(533, 90, "flour", "general store"));
            Assert.AreEqual(270, h1.GetBalanceCents());
            Assert.IsNull(inv1.AddLot(HouseholdItemCatalog.FlourId, 10, 533, "general store purchase"));
            Assert.AreEqual(20, inv1.GetAvailableUnits(HouseholdItemCatalog.FlourId));

            // 16. Consumption uses those goods.
            Assert.IsNull(inv1.Consume(HouseholdItemCatalog.FlourId, 5, 534, "meals", "P5", null));
            Assert.AreEqual(15, inv1.GetAvailableUnits(HouseholdItemCatalog.FlourId));

            // 17-18. Rent due day 529: H1 holds 270c against 300c -> partial payment,
            // remainder becomes a REAL arrears liability; P4 (H4) is credited 270c.
            // (Day 529 is driven after the wage so the partial-payment path is exercised
            // with genuine earned income.)
            List<TenancySettlementResult> rentResults = w.Tenancy.SettleDueRents(
                w.Housing, 535, w.Ledgers, w.Payees, w.Obligations, w.Ids, diag);
            Assert.AreEqual(1, rentResults.Count);
            TenancySettlementResult rent = rentResults[0];
            Assert.AreEqual("settled-partial", rent.Outcome);
            Assert.AreEqual(300, rent.AmountDueCents);
            Assert.AreEqual(270, rent.PaidCents);
            Assert.AreEqual(0, h1.GetBalanceCents());
            Assert.AreEqual(5270, h4.GetBalanceCents()); // 5000 + 270 rent
            Assert.IsFalse(string.IsNullOrEmpty(rent.ArrearsObligationId));
            var arrears = w.Obligations.Find(rent.ArrearsObligationId);
            Assert.IsNotNull(arrears);
            Assert.AreEqual(FinancialObligationKind.Payable, arrears.Kind);
            Assert.AreEqual(30, arrears.OutstandingPrincipalCents);

            // 20. The economy continues without owner orders: week 76 runs.
            for (int d = 536; d <= 541; d++) w.Negotiation.AdvanceDay(d, w.Employments, diag);
            var week2 = new SchedulerInputs { BusinessInstanceId = "store-1", DayIndex = 540 };
            week2.OpenWork.Add(new SchedulerOpenWorkItem
                { TaskId = "t-stock-2", Family = "StockShelves", MinutesRequired = 200, LocationId = "store-1" });
            week2.Workers.Add(new SchedulerAvailableWorker { PersonId = 1, AvailableMinutes = 480 });
            Assert.AreEqual(0, w.Scheduler.AssignDailyWork(week2, diag).UnassignedTaskIds.Count);
            int cashBefore2 = w.Store.CurrentCashCents;
            Assert.AreEqual("settled", w.Settlement.SettleWeeklyPayroll(w.Store, w.Employments, 76, 542, diag));
            Assert.AreEqual(cashBefore2 - 320, w.Store.CurrentCashCents);
            Assert.AreEqual(320, h1.GetBalanceCents());
            Assert.AreEqual(EmploymentLifecycleState.Active, rel.LifecycleState);

            // Cash conservation, cent for cent.
            // Store: 20000 + 450 (sale) - 320 (wk75) - 320 (wk76) = 19810.
            Assert.AreEqual(19810, w.Store.CurrentCashCents);
            // H1: 400 - 150 - 130 - 80 + 320 (wk75) - 90 (flour) - 270 (rent) + 320 (wk76) = 320.
            Assert.AreEqual(320, h1.GetBalanceCents());
            // H4: 5000 + 270 (rent) = 5270.
            Assert.AreEqual(5270, h4.GetBalanceCents());
        }

        [Test]
        public void Slice_LandSubdivision_Proof()
        {
            var w = BuildWorld();
            var diag = Diag();

            // 40-acre farm parcel: 200x200 cells. Split into 20 + 20 acres.
            // Farmhouse footprint sits at cells (10..19, 10..19) — wholly in child 0.
            var index = new BuildingIndexStub();
            index.Add(w.FarmParcelId, "farmhouse-1", new GridRect(10, 10, 10, 10));
            w.Subdivider.BuildingIndex = index;
            w.Subdivider.TitleHolderLookup = parcelId => w.Titles.CurrentHolder(parcelId);

            var request = new SubdivisionRequest
            {
                ParcelId = w.FarmParcelId,
                RequestedByHolder = "P2",
                Day = 540,
                ResultingFootprints = new List<List<GridRect>>
                {
                    new List<GridRect> { new GridRect(0, 0, 100, 200) },
                    new List<GridRect> { new GridRect(100, 0, 100, 200) },
                },
            };
            SubdivisionValidation validation = w.Subdivider.Validate(request);
            Assert.IsTrue(validation.IsValid, string.Join("; ", validation.Errors));

            SubdivisionResult result = w.Subdivider.Execute(request);
            Assert.IsTrue(result.Success, result.Message);
            Assert.AreEqual(2, result.NewParcelIds.Count);
            Assert.AreEqual(w.FarmParcelId, result.RetiredParcelId);

            // Parent retired (history preserved, not deleted); lineage on children.
            ParcelRecord parent = w.Parcels.GetParcel(w.FarmParcelId);
            Assert.IsTrue(parent.IsRetired);
            ParcelRecord child0 = w.Parcels.GetParcel(result.NewParcelIds[0]);
            ParcelRecord child1 = w.Parcels.GetParcel(result.NewParcelIds[1]);
            Assert.IsNotNull(child0); Assert.IsNotNull(child1);
            Assert.AreEqual(1, child0.ParentParcelIds.Count);
            Assert.AreEqual(w.FarmParcelId, child0.ParentParcelIds[0]);
            Assert.AreEqual(w.FarmParcelId, child1.ParentParcelIds[0]);
            // Area conservation: 20,000 cells each, no gain or loss.
            Assert.AreEqual(20000 * 4.0, child0.AreaSquareMeters);
            Assert.AreEqual(20000 * 4.0, child1.AreaSquareMeters);

            // Buildings stay linked: farmhouse wholly in child 0.
            Assert.Contains("farmhouse-1", child0.BuildingIds);
            Assert.IsFalse(child1.BuildingIds.Contains("farmhouse-1"));

            // The county easement was carried to both children for review.
            Assert.AreEqual(1, w.Interests.ActiveForParcel(child0.Id).Count);
            Assert.AreEqual(1, w.Interests.ActiveForParcel(child1.Id).Count);
            Assert.AreEqual(InterestKind.Easement, w.Interests.ActiveForParcel(child0.Id)[0].Kind);

            // Title follows the land (not a sale): children registered to P2,
            // lineage preserved in the survey description.
            Assert.IsNotNull(w.Titles.RegisterParcel(child0.Id, "farm east 20ac (ex " + w.FarmParcelId + ")",
                20f, "P2", TitleBasis.Grant, 540, diag));
            Assert.IsNotNull(w.Titles.RegisterParcel(child1.Id, "farm west 20ac (ex " + w.FarmParcelId + ")",
                20f, "P2", TitleBasis.Grant, 540, diag));
            Assert.AreEqual("P2", w.Titles.CurrentHolder(child0.Id));

            // Legitimate sale of one child through the title authority:
            // P2 sells the east 20 acres to P4 with a conveyance instrument.
            string transferId = w.Titles.TransferTitle(child0.Id, "P2", "P4", "deed-540-1", 541);
            Assert.IsNotNull(transferId);
            Assert.AreEqual("P4", w.Titles.CurrentHolder(child0.Id));
            Assert.AreEqual("P2", w.Titles.CurrentHolder(child1.Id)); // other child untouched
            var chain = w.Titles.ChainOf(child0.Id);
            Assert.AreEqual(2, chain.Count);
            Assert.AreEqual(TitleBasis.Purchase, chain[1].Basis);
            Assert.AreEqual("deed-540-1", chain[1].InstrumentId);

            // Subdivision created no money and moved no cash: H1's balance is
            // exactly its opening 400c (BuildWorld performs no spending).
            Assert.AreEqual(400, w.Ledgers.GetOrCreate(1).GetBalanceCents());
        }

        [Test]
        public void Slice_FailedSubdivision_OriginalIntact()
        {
            var w = BuildWorld();
            w.Subdivider.TitleHolderLookup = parcelId => w.Titles.CurrentHolder(parcelId);

            // Overlapping child footprints: rejected; the original is untouched.
            var bad = new SubdivisionRequest
            {
                ParcelId = w.HouseParcelId,
                RequestedByHolder = "P4",
                Day = 540,
                ResultingFootprints = new List<List<GridRect>>
                {
                    new List<GridRect> { new GridRect(300, 20, 12, 10) },
                    new List<GridRect> { new GridRect(308, 20, 12, 10) },
                },
            };
            SubdivisionValidation validation = w.Subdivider.Validate(bad);
            Assert.IsFalse(validation.IsValid);
            SubdivisionResult result = w.Subdivider.Execute(bad);
            Assert.IsFalse(result.Success);
            ParcelRecord original = w.Parcels.GetParcel(w.HouseParcelId);
            Assert.IsFalse(original.IsRetired);
            Assert.AreEqual("P4", w.Titles.CurrentHolder(w.HouseParcelId));
            Assert.AreEqual(4, w.Parcels.ParcelCount); // no children created

            // Non-holder cannot subdivide either.
            var wrongHolder = new SubdivisionRequest
            {
                ParcelId = w.HouseParcelId,
                RequestedByHolder = "P1",
                Day = 540,
                ResultingFootprints = new List<List<GridRect>>
                {
                    new List<GridRect> { new GridRect(300, 20, 10, 10) },
                    new List<GridRect> { new GridRect(310, 20, 10, 10) },
                },
            };
            Assert.IsFalse(w.Subdivider.Validate(wrongHolder).IsValid);
        }

        [Test]
        public void Contrast_RejectedOffer_Proved()
        {
            var w = BuildWorld();
            var diag = Diag();

            // P2 is employed with adequate reserves; a distant low offer is refused.
            var need = new JobDecisionInput
            {
                PersonId = 2, HouseholdId = 2, ReservesCents = 1500, Dependents = 1,
                CurrentlyEmployed = true, CurrentWageCentsPerWeek = 280,
                OfferWageCentsPerWeek = 250, CommuteMinutesOneWay = 120,
                ConditionsScore01 = 0.5f, EmployerReliability01 = 0.5f,
                KnownAlternativeCount = 1, ReservesTrendCentsPerDay = 20, DayIndex = 528,
            };
            Assert.AreEqual(JobDecisionKind.Refuse, PersonJobDecisions.EvaluateOffer(need, diag).Kind);

            // The refusal is also provable through the negotiation lifecycle.
            EmploymentOffer offer = w.Negotiation.MakeOffer(
                "smith-1", 2, 4, 250, "smith helper", "dawn to dusk", 530, 30, "", 0,
                "none", "forge duties", "good conduct", 7, "InPerson", "handbill", 528, diag);
            Assert.IsNotNull(offer);
            Assert.IsNull(w.Negotiation.RecordRefusal(offer.OfferId, "commute too far for the wage", 528, diag));
            Assert.AreEqual(OfferState.Refused, offer.State);
            Assert.IsFalse(w.Employments.TryGetById("emp-" + offer.OfferId, out _));
        }

        [Test]
        public void Contrast_NoUrgency_NoAction()
        {
            var w = BuildWorld();
            // H2: adequate reserves, employed member, obligations covered -> NoAction.
            var pressure = new HouseholdPressureInput
            {
                HouseholdId = "2", Day = 528, ReservesCents = 1500,
                ObligationsDueCents = 200, ExpectedIncomeCentsPerDay = 40,
                Dependents = 1, KnownOpportunityCount = 2,
                HasShelterRisk = false, AvailableCreditCents = 0,
                RecentJobLoss = false, HasSearchableContacts = true,
            };
            HouseholdDecision decision = w.Decisions.Evaluate(pressure);
            Assert.AreEqual(HouseholdDecisionKind.NoAction, decision.Kind);
        }

        [Test]
        public void Contrast_GenuinePressure_SeekIncome()
        {
            var w = BuildWorld();
            // H1: 40c reserves, 300c rent due, no income, no credit -> SeekIncome (search is the action).
            var pressure = new HouseholdPressureInput
            {
                HouseholdId = "1", Day = 528, ReservesCents = 40,
                ObligationsDueCents = 300, ExpectedIncomeCentsPerDay = 0,
                Dependents = 2, KnownOpportunityCount = 0,
                HasShelterRisk = false, AvailableCreditCents = 0,
                RecentJobLoss = false, HasSearchableContacts = true,
            };
            Assert.AreEqual(HouseholdDecisionKind.SeekIncome, w.Decisions.Evaluate(pressure).Kind);
        }

        [Test]
        public void Contrast_InsufficientFunds_ArrearsNotEviction()
        {
            var w = BuildWorld();
            var diag = Diag();
            var h1 = w.Ledgers.GetOrCreate(1);
            // H1 spends down to 50c before rent day; the loop takes partial payment
            // and books real arrears. No eviction: occupancy is untouched.
            Assert.IsNull(h1.RecordOutflow(528, 350, "emergency spending", "town"));
            Assert.AreEqual(50, h1.GetBalanceCents());
            List<TenancySettlementResult> results = w.Tenancy.SettleDueRents(
                w.Housing, 529, w.Ledgers, w.Payees, w.Obligations, w.Ids, diag);
            Assert.AreEqual(1, results.Count);
            Assert.AreEqual("settled-partial", results[0].Outcome);
            Assert.AreEqual(50, results[0].PaidCents);
            var arrears = w.Obligations.Find(results[0].ArrearsObligationId);
            Assert.IsNotNull(arrears);
            Assert.AreEqual(250, arrears.OutstandingPrincipalCents);
            // H1 still occupies the house: no automatic eviction on missed rent.
            Assert.AreEqual(3, w.Housing.CurrentOccupants(w.HouseSpaceId).Count);
        }

        [Test]
        public void Contrast_NoviceVsExperienced_OutcomesDiffer()
        {
            var w = BuildWorld();
            // Same task family, same base minutes; capability moves duration, risk, quality.
            CausalOutcomeInputs noviceIn = w.Skills.BuildCausalInputs(P(1), SkillIds.Stocking, 60);
            CausalOutcomeInputs expertIn = w.Skills.BuildCausalInputs(P(8), SkillIds.Stocking, 60);
            var resolver = new CausalTaskOutcomeResolver();
            CausalOutcomeResult novice = resolver.Resolve(noviceIn);
            CausalOutcomeResult expert = resolver.Resolve(expertIn);
            Assert.Greater(novice.DurationMinutes, expert.DurationMinutes);
            Assert.Greater(novice.FailureRisk01, expert.FailureRisk01);
            Assert.Greater(expert.QualityDelta, novice.QualityDelta);
            // Occupation/skill is descriptive, not a gate: the novice may still attempt.
            string rejection;
            var task = w.Tasks.CreateTaskWithPlannedMinutes("stock-shelves", P(1), 530, 60);
            Assert.IsTrue(w.Tasks.AssignTask(task.TaskId, P(1), w.Budgets, out rejection), rejection);
            Assert.IsTrue(w.Tasks.StartTask(task.TaskId, 530, out rejection), rejection);
        }

        [Test]
        public void Contrast_PrivateVsPublicLand()
        {
            var w = BuildWorld();
            ParcelRecord farm = w.Parcels.GetParcel(w.FarmParcelId);
            ParcelRecord pub = w.Parcels.GetParcel(w.PublicParcelId);
            Assert.AreEqual(LandStatus.PrivateTitled, farm.Status);
            Assert.AreEqual("P2", w.Titles.CurrentHolder(farm.Id));
            Assert.AreEqual(LandStatus.PublicDomain, pub.Status);
            Assert.AreEqual("Federal Public Domain", w.Titles.CurrentHolder(pub.Id));
            // Public land is NOT ordinary private inventory: the holder is the domain itself.
            Assert.AreNotEqual("P1", w.Titles.CurrentHolder(pub.Id));
        }

        [Test]
        public void Contrast_SplitParties_BuildingLandOccupancy()
        {
            var w = BuildWorld();
            // One town store: land titled to P4 (landlord), building on P4's parcel,
            // business operated by P3 (store owner), H1 household shops there.
            // Four distinct economic parties; no collapsed ownership.
            Assert.AreEqual("P4", w.Titles.CurrentHolder(w.StoreParcelId));
            var bldg = w.Housing.FindBuilding("store-bldg-1");
            Assert.IsNotNull(bldg);
            Assert.AreEqual(w.StoreParcelId, bldg.ParcelId);
            // The operator is P3 (H3), not the title holder P4 (H4).
            Assert.AreEqual(3, w.PersonHousehold[3]);
            Assert.AreEqual(4, w.PersonHousehold[4]);
            Assert.AreNotEqual(w.PersonHousehold[3], w.PersonHousehold[4]);
            // And the rented house: title P4, occupant H1, arrangement Rental.
            var occ = w.Housing.CurrentOccupanciesForPerson(1);
            Assert.AreEqual(1, occ.Count);
            Assert.AreEqual(AccommodationArrangement.Rental, occ[0].Arrangement);
            Assert.AreEqual("P4", w.Titles.CurrentHolder(w.HouseParcelId));
        }
    }
}
