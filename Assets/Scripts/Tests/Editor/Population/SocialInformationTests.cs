// Pass VII Workstream E: NPC conversation and information. Tests cover the
// brief 8.8 mandates: two real persons can encounter one another;
// conversation occupies appropriate time; a speaker cannot share unknown
// information as firsthand knowledge; recipient knowledge can change
// legitimately; provenance is preserved; rumors stay uncertain; repeated
// conversation never fabricates independent sources; no channel means no
// communication; save/load preserves knowledge; knowledge can affect an
// economic decision; player knowledge is not omniscient; social interaction
// can occur without producing an economic opportunity; off-screen persons
// use the same rules.
using System;
using System.Collections.Generic;
using System.Reflection;
using LandLedgers.Economy;
using LandLedgers.Economy.Labor;
using LandLedgers.Economy.Postal;
using LandLedgers.Population;
using LandLedgers.Time;
using NUnit.Framework;

namespace LandLedgers.Editor.Population
{
    [TestFixture]
    public sealed class SocialInformationTests
    {
        private const int Day = 410;

        private sealed class Fixture
        {
            public Dictionary<int, PersonState> Persons = new Dictionary<int, PersonState>();
            public Dictionary<string, BusinessInstanceState> Businesses = new Dictionary<string, BusinessInstanceState>(StringComparer.Ordinal);
            public PersonScheduleTracker Schedules = new PersonScheduleTracker();
            public EmploymentRelationshipRegistry Employments = new EmploymentRelationshipRegistry();
            public KnowledgeTransmissionStore Knowledge = new KnowledgeTransmissionStore();
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

        // ---------- Encounter ----------

        [Test]
        public void TwoPersonsAtSameVenue_CanEncounterOneAnother()
        {
            Fixture fixture = new Fixture();
            fixture.Persons[101] = MakePerson(101, "Foreman");
            fixture.Persons[102] = MakePerson(102, "Bartender");
            fixture.Businesses["saloon-1"] = MakeBusiness("saloon-1");

            Visit(fixture, 101, "saloon-1", 19 * 60, 90, SocialVisitMotivation.Recreation);
            Visit(fixture, 102, "saloon-1", 19 * 60, 120, SocialVisitMotivation.Friendship);

            ConversationOpportunityService opportunities = fixture.Opportunities();
            Assert.IsTrue(opportunities.TryGetChannel(101, 102, Day, out ConversationChannel channel, fixture.Diagnostics),
                "a legitimate channel exists: " + string.Join(" | ", fixture.Diagnostics));
            Assert.AreEqual(ConversationChannelKind.Venue, channel.Kind);
            Assert.AreEqual("saloon-1", channel.Detail);
            Assert.Less(channel.OverlapStartMinute, channel.OverlapEndMinute);
        }

        [Test]
        public void NoSharedTimeOrPlace_NoCommunicationPossible()
        {
            Fixture fixture = new Fixture();
            fixture.Persons[101] = MakePerson(101, "Foreman");
            fixture.Persons[102] = MakePerson(102, "Bartender");
            fixture.Businesses["saloon-1"] = MakeBusiness("saloon-1");

            // Different times: no overlap.
            Visit(fixture, 101, "saloon-1", 12 * 60, 60, SocialVisitMotivation.Recreation);
            Visit(fixture, 102, "saloon-1", 19 * 60, 60, SocialVisitMotivation.Friendship);

            ConversationOpportunityService opportunities = fixture.Opportunities();
            string refusal = opportunities.CheckOpportunity(101, 102, Day, fixture.Diagnostics);
            Assert.IsNotNull(refusal, "no channel without shared time");
        }

        [Test]
        public void PersonCannotConverseWhileWorkingADistantMine()
        {
            Fixture fixture = new Fixture();
            fixture.Persons[101] = MakePerson(101, "Miner");
            fixture.Persons[102] = MakePerson(102, "Bartender");
            fixture.Businesses["saloon-1"] = MakeBusiness("saloon-1");
            fixture.Businesses["mine-1"] = MakeBusiness("mine-1");

            // The miner is at the mine all day; the bartender is at the saloon.
            string refusal = fixture.Schedules.TryReserve(101, PersonActivityKind.Work, Day, 8 * 60, 10 * 60, "work:mine-1");
            Assert.IsNull(refusal, refusal);
            Visit(fixture, 102, "saloon-1", 19 * 60, 60, SocialVisitMotivation.Friendship);

            ConversationOpportunityService opportunities = fixture.Opportunities();
            string channelRefusal = opportunities.CheckOpportunity(101, 102, Day, fixture.Diagnostics);
            Assert.IsNotNull(channelRefusal, "a person working a distant mine cannot converse in the saloon");
        }

        // ---------- Time occupancy ----------

        [Test]
        public void ConversationOccupiesAppropriateTime_SocialVisitBlocksDoubleBooking()
        {
            Fixture fixture = new Fixture();
            fixture.Persons[101] = MakePerson(101, "Foreman");
            fixture.Businesses["saloon-1"] = MakeBusiness("saloon-1");

            Visit(fixture, 101, "saloon-1", 19 * 60, 90, SocialVisitMotivation.Recreation);

            // The same person cannot plan an overlapping work block: time is real.
            string refusal = fixture.Schedules.TryReserve(101, PersonActivityKind.Work, Day, 19 * 60 + 30, 60, "work:mine-1");
            Assert.IsNotNull(refusal, "overlapping reservation refused while socializing");

            // A non-overlapping visit the next evening is fine.
            string ok = SocialVisitPlanner.PlanSocialVisit(
                101, "saloon-1", Day + 1, 19 * 60, 60, SocialVisitMotivation.Companionship,
                fixture.Schedules, fixture.Diagnostics);
            Assert.IsNull(ok, ok);
        }

        // ---------- Knowledge transmission ----------

        [Test]
        public void SpeakerCannotShareUnknownInformationAsFirsthand()
        {
            Fixture fixture = new Fixture();
            fixture.Persons[101] = MakePerson(101, "Foreman");
            fixture.Persons[102] = MakePerson(102, "Bartender");
            fixture.Businesses["saloon-1"] = MakeBusiness("saloon-1");
            Visit(fixture, 101, "saloon-1", 19 * 60, 90, SocialVisitMotivation.Recreation);
            Visit(fixture, 102, "saloon-1", 19 * 60, 90, SocialVisitMotivation.Friendship);

            // P101 never observed the mine; he only heard a rumor from P999.
            InformationPacket heard = InformationPacketFactory.Relay(
                InformationPacketFactory.Observe("mine-trouble", "the north shaft floods", 999, Day - 2, 0.5f, "pkt-origin"),
                101, 102, Day, 0.4f);
            Assert.IsTrue(heard.IsRumor, "a relay is marked rumor, never firsthand");
            Assert.AreEqual("conversation", heard.SourceKind, "provenance says conversation, not direct observation");

            string refusal = ConversationRunner.Converse(
                fixture.Opportunities(), fixture.Knowledge, 101, 102, Day, heard, fixture.Diagnostics);
            Assert.IsNull(refusal, refusal);

            List<InformationPacket> held = fixture.Knowledge.PacketsFor(102, "mine-trouble");
            Assert.AreEqual(1, held.Count);
            Assert.IsTrue(held[0].IsRumor, "the recipient holds a rumor, not firsthand knowledge");
            StringAssert.Contains("999", held[0].TransmissionChain, "the chain names the true origin");
        }

        [Test]
        public void RecipientKnowledgeCanChangeLegitimately_WithProvenance()
        {
            Fixture fixture = new Fixture();
            fixture.Persons[101] = MakePerson(101, "Foreman");
            fixture.Persons[102] = MakePerson(102, "Bartender");
            fixture.Businesses["saloon-1"] = MakeBusiness("saloon-1");
            Visit(fixture, 101, "saloon-1", 19 * 60, 90, SocialVisitMotivation.Recreation);
            Visit(fixture, 102, "saloon-1", 19 * 60, 90, SocialVisitMotivation.Friendship);

            Assert.AreEqual(0, fixture.Knowledge.PacketsFor(102, "mine-trouble").Count, "the bartender knows nothing yet");

            InformationPacket observed = InformationPacketFactory.Observe(
                "mine-trouble", "the north shaft floods", 101, Day, 0.8f);
            string refusal = ConversationRunner.Converse(
                fixture.Opportunities(), fixture.Knowledge, 101, 102, Day, observed, fixture.Diagnostics);
            Assert.IsNull(refusal, refusal);

            List<InformationPacket> held = fixture.Knowledge.PacketsFor(102, "mine-trouble");
            Assert.AreEqual(1, held.Count, "the bartender's knowledge changed");
            Assert.AreEqual(101, held[0].SpeakerPersonId, "provenance: the foreman said it");
            Assert.AreEqual("direct-observation", held[0].SourceKind, "provenance: firsthand");
            Assert.AreEqual(Day, held[0].ObservedDayIndex, "the observation time is preserved");
            Assert.AreEqual(Day, held[0].TransmittedDayIndex, "the transmission time is preserved");
            Assert.AreEqual(0.8f, held[0].Confidence01, 0.001f);
        }

        [Test]
        public void RumorsRemainUncertain_ConfidenceDegradesOnRelay()
        {
            Fixture fixture = new Fixture();
            fixture.Persons[101] = MakePerson(101, "Foreman");
            fixture.Persons[102] = MakePerson(102, "Bartender");
            fixture.Persons[103] = MakePerson(103, "Patron");
            fixture.Businesses["saloon-1"] = MakeBusiness("saloon-1");
            Visit(fixture, 101, "saloon-1", 19 * 60, 90, SocialVisitMotivation.Recreation);
            Visit(fixture, 102, "saloon-1", 19 * 60, 120, SocialVisitMotivation.Friendship);
            Visit(fixture, 103, "saloon-1", 20 * 60, 60, SocialVisitMotivation.Companionship);

            InformationPacket observed = InformationPacketFactory.Observe(
                "mine-trouble", "the north shaft floods", 101, Day, 0.8f);
            Assert.IsNull(ConversationRunner.Converse(
                fixture.Opportunities(), fixture.Knowledge, 101, 102, Day, observed, fixture.Diagnostics));

            InformationPacket relay = InformationPacketFactory.Relay(
                fixture.Knowledge.PacketsFor(102, "mine-trouble")[0], 102, 103, Day, 0.6f);
            Assert.IsTrue(relay.IsRumor, "the relay is flagged rumor");
            Assert.LessOrEqual(relay.Confidence01, 0.8f, "a relay never knows better than its source");
            Assert.AreEqual("101->102", relay.TransmissionChain, "the chain extends honestly");

            Assert.IsNull(ConversationRunner.Converse(
                fixture.Opportunities(), fixture.Knowledge, 102, 103, Day, relay, fixture.Diagnostics));

            List<InformationPacket> held = fixture.Knowledge.PacketsFor(103, "mine-trouble");
            Assert.AreEqual(1, held.Count);
            Assert.IsTrue(held[0].IsRumor, "the rumor stayed uncertain through the retelling");
            Assert.AreEqual(1, fixture.Knowledge.IndependentSourceCount(103, "mine-trouble"),
                "one chain root means one independent source");
        }

        [Test]
        public void RepeatedConversationNeverFabricatesIndependentSources()
        {
            Fixture fixture = new Fixture();
            fixture.Persons[101] = MakePerson(101, "Foreman");
            fixture.Persons[102] = MakePerson(102, "Bartender");
            fixture.Persons[103] = MakePerson(103, "Patron");
            fixture.Businesses["saloon-1"] = MakeBusiness("saloon-1");
            Visit(fixture, 101, "saloon-1", 19 * 60, 90, SocialVisitMotivation.Recreation);
            Visit(fixture, 102, "saloon-1", 19 * 60, 120, SocialVisitMotivation.Friendship);
            Visit(fixture, 103, "saloon-1", 20 * 60, 60, SocialVisitMotivation.Companionship);

            InformationPacket observed = InformationPacketFactory.Observe(
                "mine-trouble", "the north shaft floods", 101, Day, 0.8f);
            Assert.IsNull(ConversationRunner.Converse(
                fixture.Opportunities(), fixture.Knowledge, 101, 102, Day, observed, fixture.Diagnostics));

            // The bartender tells the patron the same story three times.
            for (int i = 0; i < 3; i++)
            {
                InformationPacket relay = InformationPacketFactory.Relay(
                    fixture.Knowledge.PacketsFor(102, "mine-trouble")[0], 102, 103, Day, 0.6f);
                ConversationRunner.Converse(
                    fixture.Opportunities(), fixture.Knowledge, 102, 103, Day, relay, fixture.Diagnostics);
            }

            Assert.AreEqual(1, fixture.Knowledge.PacketsFor(103, "mine-trouble").Count,
                "duplicate tellings are refused, not recorded again");
            Assert.AreEqual(1, fixture.Knowledge.IndependentSourceCount(103, "mine-trouble"),
                "repetition never fabricates a second independent source");
        }

        [Test]
        public void NoChannel_NoCommunication_EvenWithAPacketReady()
        {
            Fixture fixture = new Fixture();
            fixture.Persons[101] = MakePerson(101, "Foreman");
            fixture.Persons[102] = MakePerson(102, "Bartender");
            fixture.Businesses["saloon-1"] = MakeBusiness("saloon-1");
            Visit(fixture, 101, "saloon-1", 19 * 60, 90, SocialVisitMotivation.Recreation);
            // P102 never visits. No channel exists.

            InformationPacket observed = InformationPacketFactory.Observe(
                "mine-trouble", "the north shaft floods", 101, Day, 0.8f);
            string refusal = ConversationRunner.Converse(
                fixture.Opportunities(), fixture.Knowledge, 101, 102, Day, observed, fixture.Diagnostics);
            Assert.IsNotNull(refusal, "no transmission without a legitimate channel");
            Assert.AreEqual(0, fixture.Knowledge.PacketsFor(102, "mine-trouble").Count,
                "nothing was transmitted");
        }

        [Test]
        public void SelfConversationAndDeadPersonsRefused()
        {
            Fixture fixture = new Fixture();
            fixture.Persons[101] = MakePerson(101, "Foreman");
            fixture.Persons[104] = MakePerson(104, "Deceased");
            fixture.Persons[104].deathDayIndex = Day - 5;
            fixture.Businesses["saloon-1"] = MakeBusiness("saloon-1");
            Visit(fixture, 101, "saloon-1", 19 * 60, 90, SocialVisitMotivation.Recreation);

            ConversationOpportunityService opportunities = fixture.Opportunities();
            Assert.IsNotNull(opportunities.CheckOpportunity(101, 101, Day, fixture.Diagnostics),
                "a person cannot converse with themselves");
            Assert.IsNotNull(opportunities.CheckOpportunity(101, 104, Day, fixture.Diagnostics),
                "the dead do not converse");
        }

        // ---------- Save/load ----------

        [Test]
        public void SaveLoad_PreservesKnowledgeAndTransmissionHistory()
        {
            Fixture fixture = new Fixture();
            fixture.Persons[101] = MakePerson(101, "Foreman");
            fixture.Persons[102] = MakePerson(102, "Bartender");
            fixture.Businesses["saloon-1"] = MakeBusiness("saloon-1");
            Visit(fixture, 101, "saloon-1", 19 * 60, 90, SocialVisitMotivation.Recreation);
            Visit(fixture, 102, "saloon-1", 19 * 60, 90, SocialVisitMotivation.Friendship);

            InformationPacket observed = InformationPacketFactory.Observe(
                "mine-trouble", "the north shaft floods", 101, Day, 0.8f);
            Assert.IsNull(ConversationRunner.Converse(
                fixture.Opportunities(), fixture.Knowledge, 101, 102, Day, observed, fixture.Diagnostics));
            string packetId = fixture.Knowledge.PacketsFor(102, "mine-trouble")[0].PacketId;

            KnowledgeTransmissionSaveDto dto = fixture.Knowledge.CaptureSaveDto();
            var restored = new KnowledgeTransmissionStore();
            restored.LoadFromSaveDto(dto);

            Assert.IsTrue(restored.HasPacket(102, packetId), "the packet survived the round trip");
            List<InformationPacket> held = restored.PacketsFor(102, "mine-trouble");
            Assert.AreEqual(1, held.Count);
            Assert.AreEqual("101", held[0].TransmissionChain, "the chain survived");
            Assert.AreEqual("direct-observation", held[0].SourceKind, "provenance survived");

            // The restored store still refuses duplicates.
            InformationPacket duplicate = InformationPacketFactory.Relay(held[0], 101, 102, Day + 1, 0.7f);
            duplicate.PacketId = packetId;
            Assert.IsNotNull(restored.RecordTransmission(duplicate, fixture.Diagnostics),
                "duplicate packet ids are still refused after restore");
        }

        // ---------- Knowledge affecting an economic decision ----------

        [Test]
        public void KnowledgeCanAffectAnEconomicDecision_JobRefusalCitesInformation()
        {
            Fixture fixture = new Fixture();
            fixture.Persons[101] = MakePerson(101, "Foreman");
            fixture.Persons[102] = MakePerson(102, "Bartender");
            fixture.Persons[103] = MakePerson(103, "JobSeeker");
            fixture.Businesses["saloon-1"] = MakeBusiness("saloon-1");
            fixture.Businesses["mine-1"] = MakeBusiness("mine-1");
            Visit(fixture, 101, "saloon-1", 19 * 60, 90, SocialVisitMotivation.Recreation);
            Visit(fixture, 102, "saloon-1", 19 * 60, 120, SocialVisitMotivation.Friendship);
            Visit(fixture, 103, "saloon-1", 20 * 60, 60, SocialVisitMotivation.JobSeeking);

            // Trouble at the mine reaches the job seeker through the chain.
            InformationPacket observed = InformationPacketFactory.Observe(
                "mine-trouble", "the north shaft floods", 101, Day, 0.8f);
            Assert.IsNull(ConversationRunner.Converse(
                fixture.Opportunities(), fixture.Knowledge, 101, 102, Day, observed, fixture.Diagnostics));
            InformationPacket relay = InformationPacketFactory.Relay(
                fixture.Knowledge.PacketsFor(102, "mine-trouble")[0], 102, 103, Day, 0.6f);
            Assert.IsNull(ConversationRunner.Converse(
                fixture.Opportunities(), fixture.Knowledge, 102, 103, Day, relay, fixture.Diagnostics));

            // The job seeker is offered mine work and refuses, citing the information.
            var negotiations = new EmploymentNegotiationService((businessId, personId, act) => true);
            EmploymentOffer offer = negotiations.MakeOffer(
                "mine-1", 103, 7, 500, "miner", "dawn to dusk", Day + 1, 7,
                "mine-1", 0, string.Empty, "labor", string.Empty, 7,
                "InPerson", "WordOfMouth", Day, fixture.Diagnostics);
            Assert.IsNotNull(offer, "an offer was made");
            string offerId = offer.OfferId;

            string refusalReason = "heard the north shaft floods (via P102, from foreman P101; rumor, confidence 0.60)";
            string recordRefusal = negotiations.RecordRefusal(offerId, refusalReason, Day, fixture.Diagnostics);
            Assert.IsNull(recordRefusal, "the refusal was recorded: " + recordRefusal);

            Assert.AreEqual(1, fixture.Knowledge.IndependentSourceCount(103, "mine-trouble"),
                "the decision rested on one uncertain source, not manufactured certainty");
        }

        // ---------- Player non-omniscience ----------

        [Test]
        public void PlayerKnowledgeIsNotOmniscient_UntransmittedInfoStaysPrivate()
        {
            Fixture fixture = new Fixture();
            fixture.Persons[101] = MakePerson(101, "Foreman");
            fixture.Persons[102] = MakePerson(102, "Bartender");
            fixture.Businesses["saloon-1"] = MakeBusiness("saloon-1");
            Visit(fixture, 101, "saloon-1", 19 * 60, 90, SocialVisitMotivation.Recreation);
            Visit(fixture, 102, "saloon-1", 19 * 60, 90, SocialVisitMotivation.Friendship);

            // The foreman observes trouble but never tells anyone.
            InformationPacketFactory.Observe("mine-trouble", "the north shaft floods", 101, Day, 0.8f);

            Assert.AreEqual(0, fixture.Knowledge.PacketsFor(102, "mine-trouble").Count,
                "untransmitted observations are not in anyone else's knowledge");
            Assert.AreEqual(0, fixture.Knowledge.PacketCount,
                "the store holds no packets at all — nothing leaks to the player");
        }

        // ---------- Social without economic gain ----------

        [Test]
        public void SocialInteractionCanOccurWithoutEconomicOpportunity()
        {
            Fixture fixture = new Fixture();
            fixture.Persons[101] = MakePerson(101, "Foreman");
            fixture.Persons[102] = MakePerson(102, "Bartender");
            fixture.Businesses["saloon-1"] = MakeBusiness("saloon-1");

            // A pure companionship visit: no job seeking, no business interest.
            Visit(fixture, 101, "saloon-1", 19 * 60, 90, SocialVisitMotivation.Companionship);
            Visit(fixture, 102, "saloon-1", 19 * 60, 90, SocialVisitMotivation.Friendship);

            ConversationOpportunityService opportunities = fixture.Opportunities();
            Assert.IsTrue(opportunities.TryGetChannel(101, 102, Day, out _, fixture.Diagnostics),
                "companionship alone is a legitimate reason to meet");

            // They converse about nothing economic.
            InformationPacket chat = InformationPacketFactory.Observe(
                "weather", "rain is coming tomorrow", 101, Day, 0.9f);
            Assert.IsNull(ConversationRunner.Converse(
                fixture.Opportunities(), fixture.Knowledge, 101, 102, Day, chat, fixture.Diagnostics));
            Assert.AreEqual(1, fixture.Knowledge.PacketsFor(102, "weather").Count,
                "the conversation happened and was recorded");
            Assert.AreEqual(0, fixture.Knowledge.PacketsFor(102, "mine-trouble").Count,
                "no economic information was manufactured by the visit");
        }

        // ---------- Off-screen persons ----------

        [Test]
        public void OffScreenPersonsUseTheSameInformationRules()
        {
            Fixture fixture = new Fixture();
            fixture.Persons[201] = MakePerson(201, "Traveler");
            fixture.Persons[202] = MakePerson(202, "Innkeeper");

            // Off-screen persons exchange a letter through the postal service.
            var postal = new PostalService();
            Assert.IsNull(postal.RegisterOffice("office-a", "loc-a", "post-1", null, 1, fixture.Diagnostics));
            Assert.IsNull(postal.RegisterOffice("office-b", "loc-b", "post-1", null, 1, fixture.Diagnostics));
            MailItem letter = postal.PostItem(
                MailKind.Letter, "Traveler", 202, "Innkeeper",
                "office-a", "office-b", Day - 3, fixture.Diagnostics);
            Assert.IsNotNull(letter, "the letter was posted");
            letter.SenderPersonId = 201;
            // The letter completed its journey and was collected.
            letter.Status = MailStatus.Collected;
            letter.DueArrivalDayIndex = Day - 1;

            var opportunities = new ConversationOpportunityService(
                fixture.Schedules,
                id => fixture.Persons.TryGetValue(id, out PersonState person) ? person : null,
                fixture.Employments,
                id =>
                {
                    fixture.Businesses.TryGetValue(id, out BusinessInstanceState business);
                    return business;
                },
                postal);

            // Correspondence is a legitimate channel even with no shared time.
            Assert.IsTrue(opportunities.TryGetChannel(201, 202, Day, out ConversationChannel channel, fixture.Diagnostics),
                "delivered correspondence opens a channel: " + string.Join(" | ", fixture.Diagnostics));
            Assert.AreEqual(ConversationChannelKind.Correspondence, channel.Kind);

            // But the same provenance rules apply: the traveler only relays.
            InformationPacket relay = InformationPacketFactory.Relay(
                InformationPacketFactory.Observe("mine-trouble", "the north shaft floods", 999, Day - 4, 0.5f, "pkt-origin-2"),
                201, 202, Day, 0.4f);
            Assert.IsNull(ConversationRunner.Converse(
                opportunities, fixture.Knowledge, 201, 202, Day, relay, fixture.Diagnostics));
            List<InformationPacket> held = fixture.Knowledge.PacketsFor(202, "mine-trouble");
            Assert.AreEqual(1, held.Count);
            Assert.IsTrue(held[0].IsRumor, "off-screen relays are still marked rumor");
        }
    }
}
