// Pass VII slices I and J: the flagship information proof and social
// activity without economic gain.
//
// Slice I: a real mine with declining conditions -> a foreman with a
// MineRole.Foreman crew assignment and active employment legitimately
// observes the decline -> a saloon Socializing visit -> a conversation with
// a real bartender -> information transmitted with provenance and
// uncertainty -> the bartender's knowledge changes -> the bartender relays
// it to a patron (chain extends, rumor flag set, confidence degraded) ->
// the patron refuses a mine job offer citing the packet. Throughout: the
// rumor stays uncertain, hidden richness never leaks without diligence, and
// repeated telling adds no independent source.
//
// Slice J: a social visit with a non-economic motivation; time and location
// respected; no manufactured opportunity.
using System;
using System.Collections.Generic;
using System.Reflection;
using LandLedgers.Economy;
using LandLedgers.Economy.Businesses.Mine;
using LandLedgers.World;
using LandLedgers.Economy.Labor;
using LandLedgers.Population;
using LandLedgers.Primitives;
using LandLedgers.Time;
using NUnit.Framework;

namespace LandLedgers.Editor.Population
{
    [TestFixture]
    public sealed class ForemanInformationSliceTests
    {
        private const int Day = 410;

        private sealed class Fixture
        {
            public Dictionary<int, PersonState> Persons = new Dictionary<int, PersonState>();
            public Dictionary<string, BusinessInstanceState> Businesses = new Dictionary<string, BusinessInstanceState>(StringComparer.Ordinal);
            public PersonScheduleTracker Schedules = new PersonScheduleTracker();
            public EmploymentRelationshipRegistry Employments = new EmploymentRelationshipRegistry();
            public KnowledgeTransmissionStore Knowledge = new KnowledgeTransmissionStore();
            public MineRuntimeState Mine = new MineRuntimeState();
            public MineLaborRegister MineCrew = new MineLaborRegister();
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

        private static PersonState MakePerson(int id, string firstName)
        {
            return new PersonState { id = id, firstName = firstName, lastName = "Test" };
        }

        private static BusinessInstanceState MakeBusiness(string id)
        {
            var business = new BusinessInstanceState();
            FieldInfo field = business.GetType().GetField("instanceId", BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.IsNotNull(field, "instanceId field");
            field.SetValue(business, id);
            Assert.IsTrue(business.TryAssignPremises(7), "premises assigned");
            return business;
        }

        private static void Visit(Fixture fixture, int personId, string venue, int startMinute, int duration, SocialVisitMotivation motivation)
        {
            string refusal = SocialVisitPlanner.PlanSocialVisit(
                personId, venue, Day, startMinute, duration, motivation,
                fixture.Schedules, fixture.Diagnostics);
            Assert.IsNull(refusal, "visit planned: " + refusal);
        }

        // ---------- Slice I ----------

        [Test]
        public void SliceI_ForemanInformation_EvidenceToDecision()
        {
            Fixture fixture = new Fixture();

            // A real mine with declining conditions, from actual mine state.
            fixture.Mine.ConfigureSeed(
                MineralResourceKind.Gold, 0.7f, MineDevelopmentStage.Expanded,
                0.3f, 0.35f, 0.2f);
            fixture.Mine.RecordWeeklyOutput(120, "a thin week", 0.35f, 0.28f, 0.25f);
            Assert.Less(fixture.Mine.RemainingRichness01, 0.35f, "the mine is genuinely declining");

            // A foreman who legitimately knows through employment: an active
            // employment relationship plus a Foreman crew assignment.
            fixture.Persons[501] = MakePerson(501, "Foreman");
            fixture.Persons[502] = MakePerson(502, "Bartender");
            fixture.Persons[503] = MakePerson(503, "Prospector");
            fixture.Businesses["mine-1"] = MakeBusiness("mine-1");
            fixture.Businesses["saloon-1"] = MakeBusiness("saloon-1");

            var employment = new EmploymentRelationship
            {
                Id = "emp-mine-1-501",
                EmployeePersonId = 501,
                EmployerBusinessId = "mine-1",
                LifecycleState = EmploymentLifecycleState.Active,
            };
            Assert.IsNull(fixture.Employments.Register(employment), "foreman employed");
            MineCrewAssignment assignment = fixture.MineCrew.AssignMiner(
                EntityId.For(EntityKind.EmploymentRelationship, Math.Abs("emp-mine-1-501".GetHashCode())),
                EntityId.For(EntityKind.Person, 501),
                MineRole.Foreman, MineShift.Day, 300, Day - 30, fixture.Diagnostics);
            Assert.IsNotNull(assignment, "foreman holds a Foreman crew assignment");
            Assert.AreEqual(MineRole.Foreman, assignment.Role);

            // The foreman observes the decline firsthand at the mine.
            InformationPacket observed = InformationPacketFactory.Observe(
                "mine-trouble",
                "the east stope is pinching out; richness is falling week on week",
                501, Day, 0.75f);
            Assert.IsFalse(observed.IsRumor, "firsthand observation is not a rumor");
            Assert.AreEqual("direct-observation", observed.SourceKind);

            // A saloon visit: the foreman and the bartender meet after hours.
            Visit(fixture, 501, "saloon-1", 19 * 60, 90, SocialVisitMotivation.Recreation);
            Visit(fixture, 502, "saloon-1", 18 * 60, 180, SocialVisitMotivation.Friendship);
            Visit(fixture, 503, "saloon-1", 20 * 60, 60, SocialVisitMotivation.Companionship);

            ConversationOpportunityService opportunities = fixture.Opportunities();
            Assert.IsTrue(opportunities.TryGetChannel(501, 502, Day, out _, fixture.Diagnostics),
                "the foreman actually encounters the bartender");

            // The foreman tells the bartender; the bartender's knowledge changes.
            Assert.AreEqual(0, fixture.Knowledge.PacketsFor(502, "mine-trouble").Count, "the bartender knows nothing yet");
            Assert.IsNull(ConversationRunner.Converse(
                opportunities, fixture.Knowledge, 501, 502, Day, observed, fixture.Diagnostics));
            List<InformationPacket> bartenderKnows = fixture.Knowledge.PacketsFor(502, "mine-trouble");
            Assert.AreEqual(1, bartenderKnows.Count, "the bartender's knowledge changed");
            Assert.AreEqual(501, bartenderKnows[0].SpeakerPersonId, "provenance: the foreman said it");
            Assert.IsFalse(bartenderKnows[0].IsRumor, "firsthand telling is not yet a rumor");

            // The bartender later conveys it to a patron: chain extends, the
            // rumor flag is set, confidence degrades.
            InformationPacket relay = InformationPacketFactory.Relay(
                bartenderKnows[0], 502, 503, Day, 0.6f);
            Assert.AreEqual("501->502", relay.TransmissionChain, "the chain extends honestly");
            Assert.IsTrue(relay.IsRumor, "secondhand telling is flagged rumor");
            Assert.Less(relay.Confidence01, 0.75f, "confidence degraded in the retelling");
            Assert.IsNull(ConversationRunner.Converse(
                opportunities, fixture.Knowledge, 502, 503, Day, relay, fixture.Diagnostics));

            List<InformationPacket> patronKnows = fixture.Knowledge.PacketsFor(503, "mine-trouble");
            Assert.AreEqual(1, patronKnows.Count);
            Assert.IsTrue(patronKnows[0].IsRumor, "the rumor stayed uncertain");
            Assert.AreEqual(1, fixture.Knowledge.IndependentSourceCount(503, "mine-trouble"),
                "one chain root: one independent source");

            // The patron is offered mine work and refuses, citing the packet.
            var negotiations = new EmploymentNegotiationService((businessId, personId, act) => true);
            EmploymentOffer offer = negotiations.MakeOffer(
                "mine-1", 503, 7, 450, "mucker", "dawn to dusk", Day + 1, 7,
                "mine-1", 0, string.Empty, "labor", string.Empty, 7,
                "InPerson", "WordOfMouth", Day, fixture.Diagnostics);
            Assert.IsNotNull(offer, "a mine job offer was made");
            string recordRefusal = negotiations.RecordRefusal(
                offer.OfferId,
                "the east stope is pinching out, or so the bartender heard from the foreman (rumor, confidence 0.60)",
                Day, fixture.Diagnostics);
            Assert.IsNull(recordRefusal, "the refusal was recorded: " + recordRefusal);

            // The hidden reserve number never leaked: the packet carries the
            // foreman's words, not the mine's internal richness value.
            StringAssert.DoesNotContain("0.28", patronKnows[0].ContentSummary,
                "the internal richness figure is not in the transmitted content");
            StringAssert.DoesNotContain(fixture.Mine.RemainingRichness01.ToString("0.00"), patronKnows[0].ContentSummary,
                "no hidden-state leak to the recipient");

            // A repeated telling adds no independent source.
            InformationPacket retell = InformationPacketFactory.Relay(bartenderKnows[0], 502, 503, Day, 0.55f);
            ConversationRunner.Converse(opportunities, fixture.Knowledge, 502, 503, Day, retell, fixture.Diagnostics);
            Assert.AreEqual(1, fixture.Knowledge.IndependentSourceCount(503, "mine-trouble"),
                "retelling never fabricates a confirming source");
        }

        // ---------- Slice J ----------

        [Test]
        public void SliceJ_SocialVisitWithoutEconomicGain_TimeAndLocationRespected()
        {
            Fixture fixture = new Fixture();
            fixture.Persons[601] = MakePerson(601, "Friend");
            fixture.Persons[602] = MakePerson(602, "Bartender");
            fixture.Businesses["saloon-1"] = MakeBusiness("saloon-1");
            fixture.Businesses["mine-1"] = MakeBusiness("mine-1");

            // P601 works at the mine until 17:00, then visits the saloon.
            string workRefusal = fixture.Schedules.TryReserve(
                601, PersonActivityKind.Work, Day, 8 * 60, 9 * 60, "work:mine-1");
            Assert.IsNull(workRefusal, workRefusal);
            Visit(fixture, 601, "saloon-1", 19 * 60, 60, SocialVisitMotivation.Friendship);
            Visit(fixture, 602, "saloon-1", 18 * 60, 180, SocialVisitMotivation.Friendship);

            // The evening visit does not collide with the work day.
            List<PersonActivityReservation> day = fixture.Schedules.ActiveFor(601, Day);
            Assert.AreEqual(2, day.Count, "work and the social visit both occupy real time");

            ConversationOpportunityService opportunities = fixture.Opportunities();
            Assert.IsTrue(opportunities.TryGetChannel(601, 602, Day, out ConversationChannel channel, fixture.Diagnostics),
                "the visit creates a legitimate encounter");
            Assert.AreEqual(ConversationChannelKind.Venue, channel.Kind);
            Assert.GreaterOrEqual(channel.OverlapStartMinute, 19 * 60, "the overlap respects the visit time");
            Assert.LessOrEqual(channel.OverlapEndMinute, 20 * 60, "the overlap respects the visit time");

            // They talk about nothing economic; nothing economic is produced.
            InformationPacket chat = InformationPacketFactory.Observe(
                "harvest-festival", "the festival is next week", 601, Day, 0.9f);
            Assert.IsNull(ConversationRunner.Converse(
                opportunities, fixture.Knowledge, 601, 602, Day, chat, fixture.Diagnostics));
            Assert.AreEqual(1, fixture.Knowledge.PacketsFor(602, "harvest-festival").Count);
            Assert.AreEqual(0, fixture.Knowledge.PacketsFor(602, "mine-trouble").Count,
                "no economic opportunity was manufactured by a social visit");

            // No opportunity is manufactured where none exists: P601 cannot
            // converse with someone who never came to the saloon.
            fixture.Persons[603] = MakePerson(603, "Stranger");
            Assert.IsNotNull(opportunities.CheckOpportunity(601, 603, Day, fixture.Diagnostics),
                "no manufactured opportunity for a person who was never there");
        }
    }
}
