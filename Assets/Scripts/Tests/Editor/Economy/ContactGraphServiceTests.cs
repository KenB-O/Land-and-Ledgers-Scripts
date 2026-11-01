using EntityId = LandLedgers.Primitives.EntityId;

using System.Collections.Generic;
using LandLedgers.Economy.Labor;
using LandLedgers.Economy.Recruitment;
using LandLedgers.Population;
using LandLedgers.Primitives;
using LandLedgers.World.Journeys;
using NUnit.Framework;

namespace LandLedgers.Editor.Economy
{
    /// <summary>
    /// Worker B: known-contacts authority (Autonomous Labor, Phase B).
    /// A candidate appears only through a DOCUMENTED link — never because the
    /// employer opens a hiring screen. RecruitmentService is used read-only
    /// (no edits) for the correspondence/letter behaviors.
    /// </summary>
    [TestFixture]
    public sealed class ContactGraphServiceTests
    {
        private static PersonState Adult(int id, int householdId, string professionId = "")
        {
            return new PersonState
            {
                id = id,
                firstName = "Test" + id,
                lastName = "Person",
                ageBand = AgeBand.Adult18Plus,
                laborAccessLevel = LaborAccessLevel.FullLaborMarket,
                householdId = householdId,
                professionId = professionId,
            };
        }

        private static JourneyModel TwoTownModel()
        {
            var journeys = new JourneyModel();
            journeys.RegisterLocation(new JourneyLocation("store", JourneyLocationKind.Store, "General store", 0f, 0f));
            journeys.RegisterLocation(new JourneyLocation("farm-remote", JourneyLocationKind.Farmstead, "Remote farm", 90f, 0f));
            journeys.AddEdge("store", "farm-remote", 90f, "county road");
            return journeys;
        }

        [Test]
        public void ReachableCandidates_HouseholdMemberReachable_StrangerUnreachable()
        {
            var graph = new ContactGraphService();
            // Household 10: P1, P2, P3. Household 11: P7, P8. P9: no links at all.
            graph.SeedHouseholdLinks(1, 10, new[] { 1, 2, 3 });
            graph.SeedHouseholdLinks(7, 11, new[] { 7, 8 });

            List<int> reachable = graph.ReachableCandidates("store-1", new[] { 1 });

            Assert.Contains(2, reachable, "household member reachable");
            Assert.Contains(3, reachable, "household member reachable");
            Assert.IsFalse(reachable.Contains(1), "the via-person is not their own candidate");
            Assert.IsFalse(reachable.Contains(7), "other household not reachable");
            Assert.IsFalse(reachable.Contains(8), "other household not reachable");
            Assert.IsFalse(reachable.Contains(9), "stranger with no link is unreachable");
        }

        [Test]
        public void ReachableCandidates_FormerCoworkerAndCorrespondentReachable()
        {
            var graph = new ContactGraphService();
            graph.SeedFormerCoworkerLinks(1, "store-1", new[] { 4 });
            graph.SeedCorrespondenceLink(1, 5, "inq-abc");

            List<int> reachable = graph.ReachableCandidates("store-1", new[] { 1 });

            Assert.Contains(4, reachable, "former coworker reachable via shared employment history");
            Assert.Contains(5, reachable, "correspondent reachable via inquiry");
            Assert.IsFalse(reachable.Contains(6), "no link, no reach");
        }

        [Test]
        public void ReachableCandidates_OneHopOnly_SortedDistinct()
        {
            var graph = new ContactGraphService();
            // P2 knows P6, but the employer only knows P1: P6 is friend-of-a-friend.
            graph.SeedHouseholdLinks(1, 10, new[] { 1, 2 });
            graph.SeedHouseholdLinks(2, 10, new[] { 1, 2, 6 });
            graph.SeedHouseholdLinks(2, 10, new[] { 1, 2, 6 }); // re-seed: no duplicates

            List<int> reachable = graph.ReachableCandidates("store-1", new[] { 1 });

            Assert.AreEqual(1, reachable.Count, "exactly one candidate");
            Assert.AreEqual(2, reachable[0], "one hop only, sorted, distinct — friend-of-a-friend excluded");
        }

        [Test]
        public void AddLink_RequiresDocumentedProvenance_AndRealPersons()
        {
            var graph = new ContactGraphService();

            Assert.IsNotNull(graph.AddLink(1, 2, ContactLinkKind.HouseholdMember, ""), "blank provenance refused");
            Assert.IsNotNull(graph.AddLink(1, 1, ContactLinkKind.HouseholdMember, "self"), "self-link refused");
            Assert.IsNotNull(graph.AddLink(-1, 2, ContactLinkKind.HouseholdMember, "x"), "negative id refused");
            Assert.IsNull(graph.AddLink(1, 2, ContactLinkKind.HouseholdMember, "shared household 10"), "valid link accepted");
            Assert.AreEqual(1, graph.LinkCount);
        }

        [Test]
        public void AddLink_DuplicateKeepsFirstProvenance()
        {
            var graph = new ContactGraphService();
            graph.AddLink(1, 2, ContactLinkKind.HouseholdMember, "shared household 10");
            Assert.IsNull(graph.AddLink(1, 2, ContactLinkKind.Correspondent, "inquiry xyz"), "re-add is idempotent");

            string provenance = graph.DescribeProvenance(1, 2);
            Assert.IsTrue(provenance.Contains("shared household 10"), "first provenance wins: " + provenance);
        }

        [Test]
        public void ReferralProvenance_HowHeardChainPreserved()
        {
            var graph = new ContactGraphService();
            graph.SeedEmployeeReferralLink(5, 7, "recruit-400-0");

            List<int> reachable = graph.ReachableCandidates("store-1", new[] { 5 });
            Assert.Contains(7, reachable, "referred person reachable through the referrer");

            string provenance = graph.DescribeProvenance(7, 5);
            Assert.IsNotNull(provenance, "provenance documented");
            Assert.IsTrue(provenance.Contains("EmployeeReferral"), "kind recorded: " + provenance);
            Assert.IsTrue(provenance.Contains("recruit-400-0"), "effort recorded: " + provenance);

            Assert.IsNull(graph.DescribeProvenance(7, 9), "no link, no invented provenance");
        }

        [Test]
        public void DelayedCandidateResponse_LetterTravelTimeHonored()
        {
            var population = new PopulationState();
            population.people.Add(Adult(1, 20));
            var recruitment = new RecruitmentService();
            var journeys = TwoTownModel();
            var diag = new List<string>();

            RecruitmentEffort effort = recruitment.OpenEffort(
                "store-1", "Clerk", RecruitmentChannel.Correspondence, 400, 7, diag);
            Assert.IsNotNull(effort);

            // 90 miles on foot = 1800 min = 2 travel days: arrives day 402.
            RecruitmentLetter letter = recruitment.SendLetter(
                effort.EffortId, 1, "store", "farm-remote", journeys, 400, diag);
            Assert.IsNotNull(letter, "letter sent: " + string.Join(" | ", diag));
            Assert.AreEqual(402, letter.ArrivalDayIndex);

            recruitment.AdvanceDay(population, null, 400, diag);
            Assert.AreEqual(0, recruitment.Inquiries.Count, "no instant reply on send day");

            recruitment.AdvanceDay(population, null, 401, diag);
            Assert.AreEqual(0, recruitment.Inquiries.Count, "letter still traveling — no reply faked");

            recruitment.AdvanceDay(population, null, 402, diag);
            string joined = string.Join(" | ", diag);
            bool resolved = recruitment.Inquiries.Count > 0
                || joined.Contains("travel to inspect")
                || joined.Contains("ignored the letter");
            Assert.IsTrue(resolved, "letter resolved on arrival day (reply, inspect, or honest ignore): " + joined);
        }

        [Test]
        public void UnknownCandidate_NoReplyFaked()
        {
            var population = new PopulationState();
            population.people.Add(Adult(1, 20));
            var recruitment = new RecruitmentService();
            var journeys = TwoTownModel();
            var diag = new List<string>();

            RecruitmentEffort effort = recruitment.OpenEffort(
                "store-1", "Clerk", RecruitmentChannel.Correspondence, 400, 7, diag);
            RecruitmentLetter letter = recruitment.SendLetter(
                effort.EffortId, 999, "store", "farm-remote", journeys, 400, diag);
            Assert.IsNotNull(letter);

            recruitment.AdvanceDay(population, null, 402, diag);
            Assert.AreEqual(0, recruitment.Inquiries.Count, "unknown recipient — no inquiry faked");
            Assert.IsTrue(string.Join(" | ", diag).Contains("no reply faked"), "honest diagnostic recorded");
        }

        [Test]
        public void AskForReferral_NoLeads_HonestNull()
        {
            var population = new PopulationState();
            population.people.Add(Adult(2, 20)); // the contact, alone: no family, no coworkers
            var recruitment = new RecruitmentService();
            var diag = new List<string>();

            RecruitmentEffort effort = recruitment.OpenEffort(
                "store-1", "Clerk", RecruitmentChannel.EmployeeReferral, 400, 7, diag);
            CandidateInquiry inquiry = recruitment.AskForReferral(effort.EffortId, 2, population, null, 400, diag);

            Assert.IsNull(inquiry, "no lead is an honest outcome");
            Assert.AreEqual(0, recruitment.Inquiries.Count, "no inquiry faked");
        }

        [Test]
        public void SaveLoad_RoundTrip_PreservesLinks()
        {
            var graph = new ContactGraphService();
            graph.SeedHouseholdLinks(1, 10, new[] { 1, 2, 3 });
            graph.SeedFormerCoworkerLinks(1, "store-1", new[] { 4 });

            ContactGraphServiceSaveDto dto = graph.CaptureSaveDto();
            var restored = new ContactGraphService();
            restored.LoadFromSaveDto(dto);

            Assert.AreEqual(
                graph.ReachableCandidates("store-1", new[] { 1 }).Count,
                restored.ReachableCandidates("store-1", new[] { 1 }).Count,
                "same candidate count after reload");
            CollectionAssert.AreEquivalent(
                graph.ReachableCandidates("store-1", new[] { 1 }),
                restored.ReachableCandidates("store-1", new[] { 1 }),
                "reachability identical after reload");
            Assert.AreEqual(
                graph.DescribeProvenance(1, 4),
                restored.DescribeProvenance(1, 4),
                "provenance identical after reload");

            var empty = new ContactGraphService();
            empty.LoadFromSaveDto(null);
            Assert.AreEqual(0, empty.LinkCount, "null dto loads clean");
        }
    }
}
