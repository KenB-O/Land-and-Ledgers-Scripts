// Pass VIII worker 8: social knowledge integration with ordinary autonomy.
//
// The foreman chain must not remain a one-off scripted behavior. These
// tests prove three distinct emergent chains end to end, each running:
// observation of a real state -> conversation through a legitimate channel
// -> packet with provenance -> a decision authority that actually consumes
// the packet -> a real economic consequence.
//
// Chain 1 (employment): a miner hears of layoffs and seeks another job.
// Chain 2 (supplier choice): a merchant learns a supplier is struggling and
//   steers the order to another known supplier with real goods.
// Chain 3 (credit): a lender hears of borrower trouble, seeks corroboration,
//   and the credit decision moves only on corroborated evidence.
//
// Safeguard properties proven throughout: provenance intact (chain roots
// cited, never invented); no fabricated independent sources (repeated
// tellings keep one root); confidence decay along relay chains; no
// omniscient decisions (a person who heard nothing decides on nothing); a
// single uncorroborated rumor never moves a decision on its own.
using System;
using System.Collections.Generic;
using System.Reflection;
using LandLedgers.Economy;
using LandLedgers.Economy.Delegation;
using LandLedgers.Economy.Financing;
using LandLedgers.Economy.Labor;
using LandLedgers.Economy.Recruitment;
using LandLedgers.Population;
using LandLedgers.Primitives;
using LandLedgers.ReadModels.Valuation;
using LandLedgers.Tasks;
using LandLedgers.Time;
using NUnit.Framework;

namespace LandLedgers.Editor.Population
{
    [TestFixture]
    public sealed class SocialKnowledgeIntegrationTests
    {
        private const int Day = 410;

        private sealed class Fixture
        {
            public Dictionary<int, PersonState> Persons = new Dictionary<int, PersonState>();
            public Dictionary<string, BusinessInstanceState> Businesses = new Dictionary<string, BusinessInstanceState>(StringComparer.Ordinal);
            public PersonScheduleTracker Schedules = new PersonScheduleTracker();
            public EmploymentRelationshipRegistry Employments = new EmploymentRelationshipRegistry();
            public KnowledgeTransmissionStore Knowledge = new KnowledgeTransmissionStore();
            public FinancialObligationAuthority Obligations = new FinancialObligationAuthority();
            public EntityIdRegistry Ids = new EntityIdRegistry();
            public PopulationState Population = new PopulationState();
            public RecruitmentService Recruiting = new RecruitmentService();
            public List<string> Diagnostics = new List<string>();

            public ConversationOpportunityService Opportunities()
            {
                return new ConversationOpportunityService(
                    Schedules,
                    id => Persons.TryGetValue(id, out PersonState person) ? person : null,
                    Employments,
                    id =>
                    {
                        Businesses.TryGetValue(id, out BusinessInstanceState business);
                        return business;
                    });
            }
        }

        private static void SetField(object target, string name, object value)
        {
            FieldInfo field = target.GetType().GetField(name, BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.IsNotNull(field, "field " + name);
            field.SetValue(target, value);
        }

        private static PersonState MakePerson(int id, string firstName)
        {
            return new PersonState
            {
                id = id,
                firstName = firstName,
                lastName = "Test",
                ageBand = AgeBand.Adult18Plus,
                laborAccessLevel = LaborAccessLevel.FullLaborMarket,
            };
        }

        private static BusinessInstanceState MakeBusiness(string id)
        {
            var business = new BusinessInstanceState();
            SetField(business, "instanceId", id);
            Assert.IsTrue(business.TryAssignPremises(7), "premises assigned");
            return business;
        }

        private static BusinessInstanceState MakeBusinessWithCash(string id, int cashCents)
        {
            BusinessInstanceState business = MakeBusiness(id);
            var runtime = new BusinessRuntimeState();
            runtime.AddCashCents(cashCents);
            SetField(business, "runtimeState", runtime);
            return business;
        }

        private static void Visit(Fixture fixture, int personId, string venue, int startMinute, int duration, SocialVisitMotivation motivation)
        {
            string refusal = SocialVisitPlanner.PlanSocialVisit(
                personId, venue, Day, startMinute, duration, motivation,
                fixture.Schedules, fixture.Diagnostics);
            Assert.IsNull(refusal, "visit planned: " + refusal);
        }

        private static void Employ(Fixture fixture, string employmentId, int personId, string employerId, int startDay)
        {
            string problem = fixture.Employments.Register(new EmploymentRelationship
            {
                Id = employmentId,
                EmployeePersonId = personId,
                EmployerBusinessId = employerId,
                RoleDisplayName = "miner",
                Kind = EmploymentKind.Permanent,
                LifecycleState = EmploymentLifecycleState.Active,
                Compensation = CompensationTerms.FromWeeklyWage(700, "test wage"),
                StartDayIndex = startDay,
                Source = EmploymentSource.Manual,
            });
            Assert.IsNull(problem, "employment registered: " + problem);
        }

        private static void Dismiss(Fixture fixture, string employmentId, int endDay)
        {
            Assert.IsTrue(fixture.Employments.TryGetById(employmentId, out EmploymentRelationship relationship),
                "employment exists");
            relationship.LifecycleState = EmploymentLifecycleState.Ended;
            relationship.EndReason = EmploymentEndReason.Dismissed;
            relationship.EndDayIndex = endDay;
        }

        // ---------- Chain 1: layoff rumor -> job search -> real inquiry ----------

        [Test]
        public void LayoffRumor_MinerSeeksAnotherJob_EndToEnd()
        {
            Fixture fixture = new Fixture();
            foreach (int id in new[] { 101, 102, 103, 105, 106, 107 })
            {
                fixture.Persons[id] = MakePerson(id, "P" + id);
                fixture.Population.people.Add(fixture.Persons[id]);
            }
            fixture.Businesses["saloon-1"] = MakeBusiness("saloon-1");
            fixture.Businesses["mine-1"] = MakeBusiness("mine-1");
            fixture.Businesses["mine-2"] = MakeBusiness("mine-2");

            // Real state: mine-1 dismissed three miners, including P101.
            Employ(fixture, "emp-101", 101, "mine-1", Day - 60);
            Employ(fixture, "emp-102", 102, "mine-1", Day - 60);
            Employ(fixture, "emp-106", 106, "mine-1", Day - 60);
            Employ(fixture, "emp-107", 107, "mine-1", Day - 60);
            Dismiss(fixture, "emp-101", Day - 1);

            // P101 directly observed the layoffs (firsthand: it happened to him).
            InformationPacket observed = InformationPacketFactory.Observe(
                SocialEvidenceEvaluator.LayoffRiskSubject("mine-1"),
                "mine-1 dismissed three miners, including me", 101, Day - 1, 0.9f);

            // Legitimate channel: both socializing at the saloon, overlapping time.
            Visit(fixture, 101, "saloon-1", 19 * 60, 90, SocialVisitMotivation.Recreation);
            Visit(fixture, 102, "saloon-1", 19 * 60, 90, SocialVisitMotivation.Friendship);
            Assert.IsNull(ConversationRunner.Converse(
                fixture.Opportunities(), fixture.Knowledge, 101, 102, Day, observed, fixture.Diagnostics),
                "the telling transmits: " + string.Join(" | ", fixture.Diagnostics));

            // The duplicate telling is refused: hearing it twice from the
            // same chain never fabricates a second source.
            InformationPacket retelling = InformationPacketFactory.Observe(
                SocialEvidenceEvaluator.LayoffRiskSubject("mine-1"),
                "mine-1 dismissed three miners, including me", 101, Day - 1, 0.9f);
            Assert.IsNotNull(ConversationRunner.Converse(
                fixture.Opportunities(), fixture.Knowledge, 101, 102, Day, retelling, fixture.Diagnostics),
                "the duplicate telling is refused");
            Assert.AreEqual(1, fixture.Knowledge.IndependentSourceCount(102, SocialEvidenceEvaluator.LayoffRiskSubject("mine-1")),
                "one telling, one chain root: no fabricated independent sources");

            // The ordinary employment decision consumes the packet.
            JobSecurityDecision decision = WorkerJobSecurityEvaluator.Evaluate(
                102, "mine-1", fixture.Knowledge, fixture.Employments, fixture.Diagnostics);
            Assert.IsNotNull(decision, "a decision was made");
            Assert.IsTrue(decision.SeekWork, "corroborated layoff risk moves the worker to seek other work");
            StringAssert.Contains("101", decision.Reason, "the decision cites its provenance");
            Assert.IsTrue(decision.Evidence.HasFirsthandTelling, "the evidence records the firsthand telling");

            // Real economic consequence one: the worker commits real
            // person-time to job seeking through the schedule tracker.
            string visitRefusal = SocialVisitPlanner.PlanSocialVisit(
                102, "saloon-1", Day + 1, 19 * 60, 90, SocialVisitMotivation.JobSeeking,
                fixture.Schedules, fixture.Diagnostics);
            Assert.IsNull(visitRefusal, "job-seeking time reserved: " + visitRefusal);

            // Real economic consequence two: the worker applies to a real
            // open hiring effort at another business — a real inquiry record
            // the hiring side can act on.
            RecruitmentEffort effort = fixture.Recruiting.OpenEffort(
                "mine-2", "miner", RecruitmentChannel.WordOfMouth, Day, 4242, fixture.Diagnostics);
            Assert.IsNotNull(effort, "mine-2 runs a real hiring effort");
            CandidateInquiry inquiry = fixture.Recruiting.ApplyForEffort(
                effort.EffortId, 102, fixture.Population, fixture.Employments,
                "word of mouth: heard of layoffs at mine-1 from P101 at the saloon",
                Day, fixture.Diagnostics);
            Assert.IsNotNull(inquiry, "the application became a real inquiry: " + string.Join(" | ", fixture.Diagnostics));
            Assert.AreEqual(102, inquiry.PersonId, "the inquiry is from the real worker");
            Assert.AreEqual(effort.EffortId, inquiry.EffortId, "the inquiry targets the real effort");
            StringAssert.Contains("mine-1", inquiry.Note, "the inquiry honestly records current employment");

            // The hiring side can complete the chain: a real hire creates a
            // real employment relationship.
            string hireRefusal = fixture.Recruiting.HireFromInquiry(
                inquiry.InquiryId, 750, EmploymentKind.Permanent, Day + 2, fixture.Employments, fixture.Diagnostics);
            Assert.IsNull(hireRefusal, "the hire completes: " + hireRefusal);
            bool employedAtMine2 = false;
            foreach (EmploymentRelationship rel in fixture.Employments.GetActiveByEmployee(102))
            {
                if (rel != null && string.Equals(rel.EmployerBusinessId, "mine-2", StringComparison.Ordinal))
                    employedAtMine2 = true;
            }
            Assert.IsTrue(employedAtMine2, "the worker holds a real new employment at mine-2");
        }

        [Test]
        public void LayoffRumor_RelayedOnce_WorkerStays_ConfidenceDecays()
        {
            Fixture fixture = new Fixture();
            foreach (int id in new[] { 101, 105, 106 })
            {
                fixture.Persons[id] = MakePerson(id, "P" + id);
            }
            fixture.Businesses["saloon-1"] = MakeBusiness("saloon-1");

            Employ(fixture, "emp-101", 101, "mine-1", Day - 60);
            Employ(fixture, "emp-106", 106, "mine-1", Day - 60);
            Dismiss(fixture, "emp-101", Day - 1);

            Visit(fixture, 101, "saloon-1", 19 * 60, 90, SocialVisitMotivation.Recreation);
            Visit(fixture, 105, "saloon-1", 19 * 60, 90, SocialVisitMotivation.Friendship);
            Visit(fixture, 106, "saloon-1", 20 * 60, 60, SocialVisitMotivation.Recreation);

            // Firsthand telling: P101 -> P105.
            InformationPacket observed = InformationPacketFactory.Observe(
                SocialEvidenceEvaluator.LayoffRiskSubject("mine-1"), "mine-1 dismissed three miners", 101, Day - 1, 0.9f);
            Assert.IsNull(ConversationRunner.Converse(
                fixture.Opportunities(), fixture.Knowledge, 101, 105, Day, observed, fixture.Diagnostics));

            // Relayed once: P105 -> P106. Confidence decays, rumor flag set.
            InformationPacket relay = InformationPacketFactory.Relay(
                fixture.Knowledge.PacketsFor(105, SocialEvidenceEvaluator.LayoffRiskSubject("mine-1"))[0],
                105, 106, Day, 0.7f);
            Assert.IsNull(ConversationRunner.Converse(
                fixture.Opportunities(), fixture.Knowledge, 105, 106, Day, relay, fixture.Diagnostics));
            InformationPacket held = fixture.Knowledge.PacketsFor(106, SocialEvidenceEvaluator.LayoffRiskSubject("mine-1"))[0];
            Assert.IsTrue(held.IsRumor, "the relay is flagged as rumor");
            Assert.Less(held.Confidence01, 0.9f, "confidence decayed along the chain");
            Assert.AreEqual("101->105", held.TransmissionChain, "the chain is intact");

            // One relayed rumor, one chain root: not corroborated, no action.
            Assert.AreEqual(1, fixture.Knowledge.IndependentSourceCount(106, SocialEvidenceEvaluator.LayoffRiskSubject("mine-1")),
                "the retelling did not invent a second source");
            JobSecurityDecision decision = WorkerJobSecurityEvaluator.Evaluate(
                106, "mine-1", fixture.Knowledge, fixture.Employments, fixture.Diagnostics);
            Assert.IsNotNull(decision);
            Assert.IsFalse(decision.SeekWork, "a single uncorroborated rumor does not move the worker");
            Assert.IsFalse(decision.Evidence.Corroborated, "the evidence honestly reports uncorroborated");
        }

        [Test]
        public void LayoffRumor_NoPackets_WorkerStays_NoOmniscience()
        {
            Fixture fixture = new Fixture();
            fixture.Persons[107] = MakePerson(107, "P107");
            Employ(fixture, "emp-107", 107, "mine-1", Day - 60);

            // P107 heard nothing: the layoffs happened, but knowledge does
            // not travel without conversation.
            JobSecurityDecision decision = WorkerJobSecurityEvaluator.Evaluate(
                107, "mine-1", fixture.Knowledge, fixture.Employments, fixture.Diagnostics);
            Assert.IsNotNull(decision);
            Assert.IsFalse(decision.SeekWork, "a worker who heard nothing decides on nothing");
            Assert.AreEqual(0, decision.Evidence.PacketCount, "no packets, no knowledge");

            // And a non-employee gets no decision at all.
            fixture.Persons[108] = MakePerson(108, "P108");
            Assert.IsNull(WorkerJobSecurityEvaluator.Evaluate(
                108, "mine-1", fixture.Knowledge, fixture.Employments, fixture.Diagnostics),
                "no employment at the business: no decision to make");
        }

        // ---------- Chain 2: supplier trouble -> supplier switch ----------

        private sealed class ProcureFixture
        {
            public Fixture Base = new Fixture();
            public ManagerDecisionExecutionService Service = new ManagerDecisionExecutionService();
            public ShopperSellerKnowledge SupplierKnowledge = new ShopperSellerKnowledge();

            public void Configure(List<BusinessInstanceState> orderedBusinesses)
            {
                Fixture f = Base;
                Service.Configure(new ManagerExecutionServices
                {
                    FindBusiness = id =>
                    {
                        BusinessInstanceState found;
                        return f.Businesses.TryGetValue(id, out found) ? found : null;
                    },
                    AllBusinesses = () => orderedBusinesses,
                    DemandFor = id => new LaborDemandReport { BusinessInstanceId = id, DayIndex = Day },
                    BusinessEntityFor = id => EntityId.For(EntityKind.Business, Math.Abs(id.GetHashCode())),
                    Tasks = new TaskAuthority(),
                    WorkScheduling = new WorkScheduler(),
                    PersonSchedules = f.Schedules,
                    WorkTimeBudgets = new WorkTimeBudgetStore(),
                    Employments = f.Employments,
                    FinancialObligations = f.Obligations,
                    Ids = f.Ids,
                    SupplierKnowledge = SupplierKnowledge,
                    SocialKnowledge = f.Knowledge,
                    BusinessLender = null,
                    OpenRecruitment = (business, demand, decision, day, diag) => "effort:" + business.InstanceId,
                });
            }
        }

        private static BusinessInstanceState MakeFlourBusiness(string id, int cashCents, float flourUnits)
        {
            BusinessInstanceState business = MakeBusinessWithCash(id, cashCents);
            business.GenericConfiguration.Retail.AddOrReplaceLine(
                new GenericRetailProductLine("flour", "shelf-a", 50f, 100f, 10f, 100));
            business.GenericConfiguration.AddInventory("flour", flourUnits, 80);
            return business;
        }

        private static DelegatedAuthority MakePurchasingAuthority(string businessId, int managerPersonId, int purchaseLimitCents)
        {
            return new DelegatedAuthority
            {
                AuthorityId = "auth-" + businessId,
                BusinessInstanceId = businessId,
                DelegatePersonId = managerPersonId,
                Scopes = DelegationScope.Purchasing,
                PurchaseLimitCents = purchaseLimitCents,
                GrantedDayIndex = Day - 10,
            };
        }

        [Test]
        public void SupplierTrouble_MerchantSteersOrderToSoundSupplier_EndToEnd()
        {
            var pf = new ProcureFixture();
            Fixture fixture = pf.Base;
            fixture.Persons[201] = MakePerson(201, "Manager");
            fixture.Persons[202] = MakePerson(202, "Clerk");
            fixture.Businesses["saloon-1"] = MakeBusiness("saloon-1");

            // The buyer needs flour: 2 units against a reorder point of 5.
            BusinessInstanceState buyer = MakeBusinessWithCash("store-1", 100000);
            buyer.GenericConfiguration.Retail.AddOrReplaceLine(
                new GenericRetailProductLine("flour", "shelf-a", 20f, 40f, 5f, 120));
            buyer.GenericConfiguration.AddInventory("flour", 2f, 80);
            fixture.Businesses["store-1"] = buyer;

            // Two known suppliers with real flour. mill-1 is listed first:
            // without the trouble report the order would go there.
            BusinessInstanceState mill1 = MakeFlourBusiness("mill-1", 50000, 100f);
            BusinessInstanceState mill2 = MakeFlourBusiness("mill-2", 50000, 100f);
            fixture.Businesses["mill-1"] = mill1;
            fixture.Businesses["mill-2"] = mill2;
            pf.SupplierKnowledge.LearnSupplier(201, "mill-1", "long-standing trade", Day - 30);
            pf.SupplierKnowledge.LearnSupplier(201, "mill-2", "long-standing trade", Day - 30);

            // Real state: mill-1 defaulted on a real trade obligation.
            FinancialObligation defaulted = fixture.Obligations.CreateWithTerms(
                fixture.Ids, FinancialObligationKind.TradeCredit, "mill-1", "creditor-x",
                40000, Day - 20, "grain on account", "feed delivery",
                new FinancialPaymentTerms { InstallmentPrincipalCents = 10000, PaymentIntervalDays = 30 });
            Assert.IsNotNull(defaulted, "the real obligation exists");
            defaulted.Status = FinancialObligationStatus.Defaulted;

            // The creditor's clerk directly observed the default and tells
            // the store manager at the saloon (business interest).
            InformationPacket observed = InformationPacketFactory.Observe(
                SocialEvidenceEvaluator.SupplierTroubleSubject("mill-1"),
                "mill-1 defaulted on its grain account with creditor-x", 202, Day - 1, 0.85f);
            Visit(fixture, 202, "saloon-1", 19 * 60, 90, SocialVisitMotivation.BusinessInterest);
            Visit(fixture, 201, "saloon-1", 19 * 60, 90, SocialVisitMotivation.BusinessInterest);
            Assert.IsNull(ConversationRunner.Converse(
                fixture.Opportunities(), fixture.Knowledge, 202, 201, Day, observed, fixture.Diagnostics),
                "the trouble report transmits: " + string.Join(" | ", fixture.Diagnostics));

            pf.Configure(new List<BusinessInstanceState> { mill1, mill2, buyer });

            int buyerCashBefore = buyer.RuntimeState.CurrentCashCents;
            int mill2CashBefore = mill2.RuntimeState.CurrentCashCents;
            float mill1StockBefore = mill1.GenericConfiguration.GetAvailableInventoryQuantity("flour");

            var decision = new ManagerDecisionResult { Acted = true, ActionKind = ManagerActKinds.Procure, DayIndex = Day };
            var context = new ManagerExecutionContext
            {
                BusinessInstanceId = "store-1",
                ManagerPersonId = 201,
                DayIndex = Day,
                PurchaseLimitCents = 50000,
                DelegatedScopes = new List<string> { ManagerScopeNames.Purchasing },
            };
            ManagerExecutionOutcome outcome = pf.Service.ExecuteDecision(
                buyer, MakePurchasingAuthority("store-1", 201, 50000),
                201, new LaborDemandReport { BusinessInstanceId = "store-1", DayIndex = Day },
                decision, Day, fixture.Diagnostics);
            Assert.IsTrue(outcome.Executed, "executed: " + outcome.Reason);

            // The real economic consequence: the order went to the sound
            // supplier, cash moved buyer -> mill-2, mill-2's real goods left
            // its shelves, and mill-1 was not touched.
            Assert.AreEqual(1, pf.Service.Ledger.Shipments.Count, "one shipment booked");
            Assert.AreEqual("mill-2", pf.Service.Ledger.Shipments[0].SupplierBusinessId,
                "the order steered away from the troubled supplier");
            int paid = buyerCashBefore - buyer.RuntimeState.CurrentCashCents;
            Assert.Greater(paid, 0, "the buyer paid real cash");
            Assert.AreEqual(paid, mill2.RuntimeState.CurrentCashCents - mill2CashBefore,
                "cash conserved across the transaction");
            Assert.Less(mill2.GenericConfiguration.GetAvailableInventoryQuantity("flour"), 100f,
                "mill-2's real goods left its shelves");
            Assert.AreEqual(mill1StockBefore, mill1.GenericConfiguration.GetAvailableInventoryQuantity("flour"), 0.001f,
                "mill-1's stock is untouched: steered away, never harmed");
            bool cited = false;
            foreach (string line in fixture.Diagnostics)
            {
                if (line.Contains("mill-1") && line.Contains("202")) cited = true;
            }
            Assert.IsTrue(cited, "the decision cites its provenance in the diagnostics");
        }

        [Test]
        public void SupplierTrouble_UncorroboratedRumor_OrderUnchanged()
        {
            var pf = new ProcureFixture();
            Fixture fixture = pf.Base;
            fixture.Persons[201] = MakePerson(201, "Manager");
            fixture.Persons[202] = MakePerson(202, "Gossip");
            fixture.Persons[203] = MakePerson(203, "Drifter");
            fixture.Businesses["saloon-1"] = MakeBusiness("saloon-1");

            BusinessInstanceState buyer = MakeBusinessWithCash("store-1", 100000);
            buyer.GenericConfiguration.Retail.AddOrReplaceLine(
                new GenericRetailProductLine("flour", "shelf-a", 20f, 40f, 5f, 120));
            buyer.GenericConfiguration.AddInventory("flour", 2f, 80);
            fixture.Businesses["store-1"] = buyer;
            BusinessInstanceState mill1 = MakeFlourBusiness("mill-1", 50000, 100f);
            BusinessInstanceState mill2 = MakeFlourBusiness("mill-2", 50000, 100f);
            fixture.Businesses["mill-1"] = mill1;
            fixture.Businesses["mill-2"] = mill2;
            pf.SupplierKnowledge.LearnSupplier(201, "mill-1", "long-standing trade", Day - 30);
            pf.SupplierKnowledge.LearnSupplier(201, "mill-2", "long-standing trade", Day - 30);

            // The manager hears only a relayed rumor: P202 told P203, who
            // tells the manager. One chain root, no firsthand telling.
            InformationPacket hearsay = InformationPacketFactory.Observe(
                SocialEvidenceEvaluator.SupplierTroubleSubject("mill-1"), "mill-1 is struggling", 202, Day - 1, 0.8f);
            Visit(fixture, 202, "saloon-1", 18 * 60, 60, SocialVisitMotivation.Recreation);
            Visit(fixture, 203, "saloon-1", 18 * 60, 60, SocialVisitMotivation.Recreation);
            Visit(fixture, 201, "saloon-1", 20 * 60, 60, SocialVisitMotivation.BusinessInterest);
            Assert.IsNull(ConversationRunner.Converse(
                fixture.Opportunities(), fixture.Knowledge, 202, 203, Day, hearsay, fixture.Diagnostics));
            InformationPacket relay = InformationPacketFactory.Relay(
                fixture.Knowledge.PacketsFor(203, SocialEvidenceEvaluator.SupplierTroubleSubject("mill-1"))[0],
                203, 201, Day, 0.5f);
            // P203 visits again later to meet the manager; the channel needs
            // overlapping time, so extend with a second visit.
            string revisit = SocialVisitPlanner.PlanSocialVisit(
                203, "saloon-1", Day, 20 * 60, 60, SocialVisitMotivation.Recreation,
                fixture.Schedules, fixture.Diagnostics);
            Assert.IsNull(revisit, "second visit planned: " + revisit);
            Assert.IsNull(ConversationRunner.Converse(
                fixture.Opportunities(), fixture.Knowledge, 203, 201, Day, relay, fixture.Diagnostics));

            pf.Configure(new List<BusinessInstanceState> { mill1, mill2, buyer });

            var decision = new ManagerDecisionResult { Acted = true, ActionKind = ManagerActKinds.Procure, DayIndex = Day };
            var context = new ManagerExecutionContext
            {
                BusinessInstanceId = "store-1",
                ManagerPersonId = 201,
                DayIndex = Day,
                PurchaseLimitCents = 50000,
                DelegatedScopes = new List<string> { ManagerScopeNames.Purchasing },
            };
            ManagerExecutionOutcome outcome = pf.Service.ExecuteDecision(
                buyer, MakePurchasingAuthority("store-1", 201, 50000),
                201, new LaborDemandReport { BusinessInstanceId = "store-1", DayIndex = Day },
                decision, Day, fixture.Diagnostics);
            Assert.IsTrue(outcome.Executed, "executed: " + outcome.Reason);
            Assert.AreEqual("mill-1", pf.Service.Ledger.Shipments[0].SupplierBusinessId,
                "an uncorroborated rumor does not move the order");
            bool noted = false;
            foreach (string line in fixture.Diagnostics)
            {
                if (line.Contains("uncorroborated rumor about supplier mill-1")) noted = true;
            }
            Assert.IsTrue(noted, "the rumor was noted, not acted on");
        }

        // ---------- Chain 3: borrower trouble -> lender corroboration -> credit ----------

        private sealed class LenderFixture
        {
            public EntityIdRegistry Ids = new EntityIdRegistry();
            public FinancialObligationAuthority Obligations = new FinancialObligationAuthority();
            public CreditOfferWorkflow Workflow = new CreditOfferWorkflow();
            public CreditEventLog Events = new CreditEventLog();
            public EnterpriseValuationReadModel Valuation = new EnterpriseValuationReadModel();
            public Dictionary<string, BusinessInstanceState> Businesses =
                new Dictionary<string, BusinessInstanceState>(StringComparer.Ordinal);
            public BusinessLenderService Lender;
            public List<string> Diagnostics = new List<string>();

            public LenderFixture()
            {
                Lender = new BusinessLenderService("Test Town Bank");
                Lender.BindHubServices(Ids, Obligations, Workflow, Events, Valuation);
                Lender.BindBusinessLookup(id =>
                {
                    BusinessInstanceState found;
                    return Businesses.TryGetValue(id, out found) ? found : null;
                });
                string problem = Lender.EnsureCharteredCapital(2500000, "test charter capital", Day, Diagnostics);
                Assert.IsNull(problem, "charter refused: " + problem);
            }
        }

        private static void MakeCreditworthyBorrower(LenderFixture fixture, string id)
        {
            BusinessInstanceState business = MakeBusinessWithCash(id, 100000);
            business.BusinessReputation.stockReliability01 = 0.9f;
            business.BusinessReputation.valueFairness01 = 0.9f;
            business.BusinessReputation.serviceExperience01 = 0.9f;
            business.BusinessReputation.conditionPresentationTrust01 = 0.9f;
            business.BusinessReputation.productTradeConfidence01 = 0.9f;
            fixture.Businesses[id] = business;
            fixture.Valuation.RegisterBusiness(id, "owner:" + id, false);
            for (int i = 0; i < 6; i++)
                fixture.Valuation.RecordWeeklyProfit(id, 20000);
            fixture.Valuation.RecordTransferableAssets(id, 200000);
        }

        private static void MarkDelinquent(LenderFixture fixture, string debtorId)
        {
            FinancialObligation obligation = fixture.Obligations.CreateWithTerms(
                fixture.Ids, FinancialObligationKind.TradeCredit, debtorId, "supplier-x",
                40000, Day - 20, "feed on account", "feed delivery",
                new FinancialPaymentTerms { InstallmentPrincipalCents = 10000, PaymentIntervalDays = 30 });
            Assert.IsNotNull(obligation, "the real obligation exists");
            obligation.Status = FinancialObligationStatus.Delinquent;
        }

        private static int ArrangeLoanRateBps(LenderFixture fixture, string borrowerId, int amountCents)
        {
            FinancialObligation obligation = fixture.Lender.TryArrangeLoan(
                fixture.Ids, fixture.Obligations, borrowerId, amountCents, Day,
                fixture.Diagnostics, out string refusalReason);
            Assert.IsNotNull(obligation, "loan refused: " + refusalReason);
            return obligation.PaymentTerms.AnnualInterestRateBps;
        }

        [Test]
        public void BorrowerTrouble_Corroborated_RaisesCaution_RealPricingConsequence()
        {
            // With social evidence: the officer heard two independent
            // firsthand tellings of real trouble.
            var withSocial = new LenderFixture();
            MakeCreditworthyBorrower(withSocial, "farm-1");
            MarkDelinquent(withSocial, "farm-1");
            var knowledge = new KnowledgeTransmissionStore();
            var schedules = new PersonScheduleTracker();
            var persons = new Dictionary<int, PersonState>();
            foreach (int id in new[] { 301, 302, 303 })
                persons[id] = MakePerson(id, "P" + id);
            var employments = new EmploymentRelationshipRegistry();
            var businesses = new Dictionary<string, BusinessInstanceState>(StringComparer.Ordinal)
            {
                { "saloon-1", MakeBusiness("saloon-1") },
            };
            var opportunities = new ConversationOpportunityService(
                schedules, id => persons.TryGetValue(id, out PersonState p) ? p : null,
                employments, id => { businesses.TryGetValue(id, out BusinessInstanceState b); return b; });
            var diag = new List<string>();
            Assert.IsNull(SocialVisitPlanner.PlanSocialVisit(301, "saloon-1", Day, 19 * 60, 90,
                SocialVisitMotivation.BusinessInterest, schedules, diag));
            Assert.IsNull(SocialVisitPlanner.PlanSocialVisit(302, "saloon-1", Day, 19 * 60, 60,
                SocialVisitMotivation.BusinessInterest, schedules, diag));
            Assert.IsNull(SocialVisitPlanner.PlanSocialVisit(303, "saloon-1", Day, 19 * 60, 90,
                SocialVisitMotivation.BusinessInterest, schedules, diag));
            // P302 directly observed the missed payment; P303 independently
            // observed farm-1 behind with another supplier.
            Assert.IsNull(ConversationRunner.Converse(opportunities, knowledge, 302, 301, Day,
                InformationPacketFactory.Observe(SocialEvidenceEvaluator.BorrowerTroubleSubject("farm-1"),
                    "farm-1 missed its payment to supplier-x", 302, Day - 1, 0.85f), diag));
            Assert.IsNull(ConversationRunner.Converse(opportunities, knowledge, 303, 301, Day,
                InformationPacketFactory.Observe(SocialEvidenceEvaluator.BorrowerTroubleSubject("farm-1"),
                    "farm-1 is behind with supplier-y too", 303, Day - 1, 0.8f), diag));
            withSocial.Lender.BindSocialKnowledge(knowledge, 301);
            int rateWithSocial = ArrangeLoanRateBps(withSocial, "farm-1", 50000);

            // Baseline: identical real records, no social intake.
            var baseline = new LenderFixture();
            MakeCreditworthyBorrower(baseline, "farm-1");
            MarkDelinquent(baseline, "farm-1");
            int rateBaseline = ArrangeLoanRateBps(baseline, "farm-1", 50000);

            Assert.Greater(rateWithSocial, rateBaseline,
                "corroborated trouble reduced trust, and the real underwriting priced it");
            bool cited = false;
            foreach (string line in withSocial.Diagnostics)
            {
                if (line.Contains("borrower-trouble") && line.Contains("302") && line.Contains("303"))
                    cited = true;
            }
            Assert.IsTrue(cited, "the credit file cites the provenance (chain roots 302, 303)");
            bool disclosed = false;
            foreach (CreditEvidenceRecord record in withSocial.Workflow.Evidence)
            {
                if (record != null && string.Equals(record.Source, "social-evidence", StringComparison.Ordinal))
                    disclosed = true;
            }
            Assert.IsTrue(disclosed, "the social evidence was disclosed on the credit file with provenance");
        }

        [Test]
        public void BorrowerTrouble_Uncorroborated_ConfirmedByRecords_CautionRises()
        {
            // The officer hears one relayed rumor; the bank's own records
            // confirm real delinquency: corroboration by evidence.
            var fixture = new LenderFixture();
            MakeCreditworthyBorrower(fixture, "farm-2");
            MarkDelinquent(fixture, "farm-2");
            var knowledge = new KnowledgeTransmissionStore();
            var schedules = new PersonScheduleTracker();
            var persons = new Dictionary<int, PersonState>();
            foreach (int id in new[] { 301, 302, 303 })
                persons[id] = MakePerson(id, "P" + id);
            var employments = new EmploymentRelationshipRegistry();
            var businesses = new Dictionary<string, BusinessInstanceState>(StringComparer.Ordinal)
            {
                { "saloon-1", MakeBusiness("saloon-1") },
            };
            var opportunities = new ConversationOpportunityService(
                schedules, id => persons.TryGetValue(id, out PersonState p) ? p : null,
                employments, id => { businesses.TryGetValue(id, out BusinessInstanceState b); return b; });
            var diag = new List<string>();
            Assert.IsNull(SocialVisitPlanner.PlanSocialVisit(302, "saloon-1", Day, 18 * 60, 60,
                SocialVisitMotivation.Recreation, schedules, diag));
            Assert.IsNull(SocialVisitPlanner.PlanSocialVisit(303, "saloon-1", Day, 18 * 60, 60,
                SocialVisitMotivation.Recreation, schedules, diag));
            Assert.IsNull(SocialVisitPlanner.PlanSocialVisit(301, "saloon-1", Day, 20 * 60, 60,
                SocialVisitMotivation.BusinessInterest, schedules, diag));
            Assert.IsNull(ConversationRunner.Converse(opportunities, knowledge, 302, 303, Day,
                InformationPacketFactory.Observe(SocialEvidenceEvaluator.BorrowerTroubleSubject("farm-2"),
                    "farm-2 missed a payment", 302, Day - 1, 0.8f), diag));
            InformationPacket relay = InformationPacketFactory.Relay(
                knowledge.PacketsFor(303, SocialEvidenceEvaluator.BorrowerTroubleSubject("farm-2"))[0],
                303, 301, Day, 0.6f);
            string revisit = SocialVisitPlanner.PlanSocialVisit(303, "saloon-1", Day, 20 * 60, 60,
                SocialVisitMotivation.Recreation, schedules, diag);
            Assert.IsNull(revisit, "overlapping visit: " + revisit);
            Assert.IsNull(ConversationRunner.Converse(opportunities, knowledge, 303, 301, Day, relay, diag));
            fixture.Lender.BindSocialKnowledge(knowledge, 301);

            int rateWithRumor = ArrangeLoanRateBps(fixture, "farm-2", 50000);

            var baseline = new LenderFixture();
            MakeCreditworthyBorrower(baseline, "farm-2");
            MarkDelinquent(baseline, "farm-2");
            int rateBaseline = ArrangeLoanRateBps(baseline, "farm-2", 50000);

            Assert.Greater(rateWithRumor, rateBaseline,
                "the rumor was corroborated by real records, so caution moved");
            bool confirmed = false;
            foreach (string line in fixture.Diagnostics)
            {
                if (line.Contains("confirmed by the bank's own obligation records")) confirmed = true;
            }
            Assert.IsTrue(confirmed, "the file says the rumor was confirmed by records");
        }

        [Test]
        public void BorrowerTrouble_Uncorroborated_CleanRecords_NoCreditEffect()
        {
            // One relayed rumor, and the bank's own records show nothing:
            // the lender keeps seeking corroboration and the loan prices
            // exactly as if the rumor never existed.
            var fixture = new LenderFixture();
            MakeCreditworthyBorrower(fixture, "farm-3");
            var knowledge = new KnowledgeTransmissionStore();
            var schedules = new PersonScheduleTracker();
            var persons = new Dictionary<int, PersonState>();
            foreach (int id in new[] { 301, 302, 303 })
                persons[id] = MakePerson(id, "P" + id);
            var employments = new EmploymentRelationshipRegistry();
            var businesses = new Dictionary<string, BusinessInstanceState>(StringComparer.Ordinal)
            {
                { "saloon-1", MakeBusiness("saloon-1") },
            };
            var opportunities = new ConversationOpportunityService(
                schedules, id => persons.TryGetValue(id, out PersonState p) ? p : null,
                employments, id => { businesses.TryGetValue(id, out BusinessInstanceState b); return b; });
            var diag = new List<string>();
            Assert.IsNull(SocialVisitPlanner.PlanSocialVisit(302, "saloon-1", Day, 18 * 60, 60,
                SocialVisitMotivation.Recreation, schedules, diag));
            Assert.IsNull(SocialVisitPlanner.PlanSocialVisit(303, "saloon-1", Day, 18 * 60, 60,
                SocialVisitMotivation.Recreation, schedules, diag));
            Assert.IsNull(SocialVisitPlanner.PlanSocialVisit(301, "saloon-1", Day, 20 * 60, 60,
                SocialVisitMotivation.BusinessInterest, schedules, diag));
            Assert.IsNull(ConversationRunner.Converse(opportunities, knowledge, 302, 303, Day,
                InformationPacketFactory.Observe(SocialEvidenceEvaluator.BorrowerTroubleSubject("farm-3"),
                    "farm-3 is in trouble, or so I heard", 302, Day - 1, 0.7f), diag));
            InformationPacket relay = InformationPacketFactory.Relay(
                knowledge.PacketsFor(303, SocialEvidenceEvaluator.BorrowerTroubleSubject("farm-3"))[0],
                303, 301, Day, 0.5f);
            string revisit = SocialVisitPlanner.PlanSocialVisit(303, "saloon-1", Day, 20 * 60, 60,
                SocialVisitMotivation.Recreation, schedules, diag);
            Assert.IsNull(revisit, "overlapping visit: " + revisit);
            Assert.IsNull(ConversationRunner.Converse(opportunities, knowledge, 303, 301, Day, relay, diag));
            fixture.Lender.BindSocialKnowledge(knowledge, 301);

            int rateWithRumor = ArrangeLoanRateBps(fixture, "farm-3", 50000);

            var baseline = new LenderFixture();
            MakeCreditworthyBorrower(baseline, "farm-3");
            int rateBaseline = ArrangeLoanRateBps(baseline, "farm-3", 50000);

            Assert.AreEqual(rateBaseline, rateWithRumor,
                "an uncorroborated rumor with clean records has no credit effect");
            bool seeking = false;
            foreach (string line in fixture.Diagnostics)
            {
                if (line.Contains("seeking corroboration") && line.Contains("no credit effect")) seeking = true;
            }
            Assert.IsTrue(seeking, "the file records that corroboration is still sought");
        }

        [Test]
        public void BorrowerTrouble_IntakeUnwired_PacketsNeverReachUnderwriting()
        {
            // The officer heard real trouble, but the intake was never bound:
            // underwriting cannot see it.
            var fixture = new LenderFixture();
            MakeCreditworthyBorrower(fixture, "farm-4");
            MarkDelinquent(fixture, "farm-4");
            var knowledge = new KnowledgeTransmissionStore();
            InformationPacket observed = InformationPacketFactory.Observe(
                SocialEvidenceEvaluator.BorrowerTroubleSubject("farm-4"),
                "farm-4 missed its payment", 302, Day - 1, 0.9f);
            observed.RecipientPersonId = 301;
            observed.TransmittedDayIndex = Day;
            Assert.IsNull(knowledge.RecordTransmission(observed, fixture.Diagnostics));
            // BindSocialKnowledge is deliberately NOT called.

            int rateUnwired = ArrangeLoanRateBps(fixture, "farm-4", 50000);

            var baseline = new LenderFixture();
            MakeCreditworthyBorrower(baseline, "farm-4");
            MarkDelinquent(baseline, "farm-4");
            int rateBaseline = ArrangeLoanRateBps(baseline, "farm-4", 50000);

            Assert.AreEqual(rateBaseline, rateUnwired,
                "without the binding, packets in the store cannot reach underwriting");
            foreach (string line in fixture.Diagnostics)
            {
                bool lenderSocialLine = line.Contains("BusinessLenderService")
                    && (line.Contains("borrower-trouble") || line.Contains("corroboration"));
                Assert.IsFalse(lenderSocialLine,
                    "no lender social-evidence diagnostics when the intake is unwired: " + line);
            }
        }
    }
}
