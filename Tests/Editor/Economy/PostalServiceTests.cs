using System.Collections.Generic;
using LandLedgers.Economy.Postal;
using LandLedgers.Economy.Recruitment;
using LandLedgers.Population;
using LandLedgers.World.Journeys;
using NUnit.Framework;

namespace LandLedgers.EditorTests.Economy
{
    /// <summary>
    /// NX-2A: the postal service — real information network. Letters travel
    /// physically on real schedules with real handoff (Canon Part VII §7.2);
    /// the post office is a paying transportation customer (Canon §16.4).
    /// </summary>
    [TestFixture]
    public sealed class PostalServiceTests
    {
        private JourneyModel TwoTowns()
        {
            var model = new JourneyModel();
            var diag = new List<string>();
            model.RegisterLocation(new JourneyLocation("town-a", JourneyLocationKind.TownBuilding, "Town A", 0f, 0f));
            model.RegisterLocation(new JourneyLocation("town-b", JourneyLocationKind.TownBuilding, "Town B", 8f, 0f));
            model.AddEdge("town-a", "town-b", 8f, "stage road");
            Assert.IsEmpty(diag);
            return model;
        }

        private PostalService TwoOffices()
        {
            var postal = new PostalService();
            var diag = new List<string>();
            Assert.IsNull(postal.RegisterOffice("po-a", "town-a", "biz-po-a", new List<int> { 0 }, 7, diag)); // Monday
            Assert.IsNull(postal.RegisterOffice("po-b", "town-b", "biz-po-b", new List<int> { 0 }, 9, diag));
            return postal;
        }

        [Test]
        public void PostItem_ChargesHistoricalPostage()
        {
            var postal = TwoOffices();
            var diag = new List<string>();
            MailItem item = postal.PostItem(MailKind.Letter, "Kennedy", 1, "Emma",
                "po-a", "po-b", 0, diag);
            Assert.IsNotNull(item);
            // Historical: 3c per half-ounce in the 1870s (2c from Oct 1883).
            Assert.AreEqual(3, item.PostageCents);
            Assert.AreEqual(MailStatus.Posted, item.Status);
            Assert.AreEqual(3, postal.UncollectedPostageCents);
            Assert.AreEqual(3, postal.CollectPostageRevenue(diag));
            Assert.AreEqual(0, postal.UncollectedPostageCents, "Revenue collects once — no double counting.");
        }

        [Test]
        public void Dispatch_UsesRealSchedule_NotInstant()
        {
            var journeys = TwoTowns();
            var postal = TwoOffices(); // departs Mondays only (weekday 0)
            var diag = new List<string>();
            MailItem item = postal.PostItem(MailKind.Letter, "Kennedy", 1, "Emma",
                "po-a", "po-b", 0, diag);
            Assert.IsNotNull(item);

            postal.AdvanceDay(journeys, 0, diag); // Monday = departure day
            Assert.AreEqual(MailStatus.InTransit, item.Status);
            // 8 miles at wagon 4mph = 120 min → 1 transit day.
            Assert.AreEqual(1, item.DueArrivalDayIndex);

            postal.AdvanceDay(journeys, 1, diag);
            Assert.AreEqual(MailStatus.Arrived, item.Status);
            Assert.AreEqual("po-b", item.CurrentOfficeId);
        }

        [Test]
        public void Dispatch_WaitsForDepartureDay()
        {
            var journeys = TwoTowns();
            var postal = TwoOffices();
            var diag = new List<string>();
            MailItem item = postal.PostItem(MailKind.Letter, "Kennedy", 1, "Emma",
                "po-a", "po-b", 3, diag); // posted Wednesday
            postal.AdvanceDay(journeys, 3, diag); // Wednesday — not a departure day
            Assert.AreEqual(MailStatus.Posted, item.Status, "Mail waits for the real schedule — nothing teleports.");
        }

        [Test]
        public void Dispatch_NoRoute_NoDispatch()
        {
            var journeys = new JourneyModel(); // no edges at all
            var postal = new PostalService();
            var diag = new List<string>();
            Assert.IsNull(postal.RegisterOffice("po-a", "town-a", "biz-a", new List<int> { 0 }, 7, diag));
            // Offices need journey locations; register them without edges.
            journeys.RegisterLocation(new JourneyLocation("town-a", JourneyLocationKind.TownBuilding, "A", 0f, 0f));
            journeys.RegisterLocation(new JourneyLocation("town-b", JourneyLocationKind.TownBuilding, "B", 8f, 0f));
            Assert.IsNull(postal.RegisterOffice("po-b", "town-b", "biz-b", new List<int> { 0 }, 9, diag));

            MailItem item = postal.PostItem(MailKind.Letter, "Kennedy", 1, "Emma",
                "po-a", "po-b", 0, diag);
            postal.AdvanceDay(journeys, 0, diag);
            Assert.AreEqual(MailStatus.Posted, item.Status, "No route = no dispatch — mail waits honestly.");
        }

        [Test]
        public void Handoff_ThroughIntermediateOffice_IsRecorded()
        {
            var journeys = new JourneyModel();
            journeys.RegisterLocation(new JourneyLocation("town-a", JourneyLocationKind.TownBuilding, "A", 0f, 0f));
            journeys.RegisterLocation(new JourneyLocation("town-mid", JourneyLocationKind.Waypoint, "Mid", 4f, 0f));
            journeys.RegisterLocation(new JourneyLocation("town-b", JourneyLocationKind.TownBuilding, "B", 8f, 0f));
            journeys.AddEdge("town-a", "town-mid", 4f, "stage road");
            journeys.AddEdge("town-mid", "town-b", 4f, "stage road");

            var postal = new PostalService();
            var diag = new List<string>();
            Assert.IsNull(postal.RegisterOffice("po-a", "town-a", "biz-a", new List<int> { 0 }, 7, diag));
            Assert.IsNull(postal.RegisterOffice("po-mid", "town-mid", "biz-mid", new List<int> { 0 }, 11, diag));
            Assert.IsNull(postal.RegisterOffice("po-b", "town-b", "biz-b", new List<int> { 0 }, 9, diag));

            MailItem item = postal.PostItem(MailKind.Letter, "Kennedy", 1, "Emma",
                "po-a", "po-b", 0, diag);
            postal.AdvanceDay(journeys, 0, diag);
            bool handoffRecorded = false;
            foreach (string h in item.History)
                if (h.Contains("po-mid")) handoffRecorded = true;
            Assert.IsTrue(handoffRecorded, "Real handoff through the intermediate office must be recorded (Canon §16.4).");
        }

        [Test]
        public void CollectMail_InPersonOnly()
        {
            var journeys = TwoTowns();
            var postal = TwoOffices();
            var diag = new List<string>();
            postal.PostItem(MailKind.Letter, "Kennedy", 1, "Emma", "po-a", "po-b", 0, diag);
            postal.AdvanceDay(journeys, 0, diag);
            postal.AdvanceDay(journeys, 1, diag);

            var wrong = postal.CollectMail("po-b", 99, 1, diag);
            Assert.AreEqual(0, wrong.Count, "Only the recipient's own mail is released.");
            var right = postal.CollectMail("po-b", 1, 1, diag);
            Assert.AreEqual(1, right.Count);
            Assert.AreEqual(MailStatus.Collected, right[0].Status);
        }

        [Test]
        public void Contract_PaysOnlyOnCompletedTrip()
        {
            var postal = TwoOffices();
            var diag = new List<string>();
            PostalContract contract = postal.OfferContract("po-a", "town-a stage loop",
                "biz-freight-1", 150, 0, diag);
            Assert.IsNotNull(contract);

            Assert.AreEqual(0, postal.CompleteContractTrip(contract.ContractId, false, 1, diag),
                "Undelivered trips are not paid — no free subsidy (Canon §16.4).");
            Assert.AreEqual(150, postal.CompleteContractTrip(contract.ContractId, true, 1, diag));
            Assert.AreEqual(1, contract.TripsCompleted);
            Assert.AreEqual(150, contract.TotalPaidCents);
        }

        [Test]
        public void OffMapMail_LeavesHonestly()
        {
            var journeys = TwoTowns();
            var postal = TwoOffices();
            var diag = new List<string>();
            MailItem item = postal.PostItem(MailKind.Letter, "Kennedy", -1, "Arnold (Winnipeg)",
                "po-a", "offmap", 0, diag);
            Assert.IsNotNull(item);
            postal.AdvanceDay(journeys, 0, diag);
            postal.AdvanceDay(journeys, 1, diag);
            Assert.AreEqual(MailStatus.ForwardedOffMap, item.Status, "Off-map mail leaves the world — no fake return.");
        }

        [Test]
        public void RecruitmentLetter_ViaPost_WaitsOnRealArrival()
        {
            var journeys = TwoTowns();
            var postal = TwoOffices();
            var diag = new List<string>();

            var population = new PopulationState();
            var service = new RecruitmentService();
            RecruitmentEffort effort = service.OpenEffort("biz-store", "clerk",
                RecruitmentChannel.Correspondence, 0, 42, diag);
            Assert.IsNotNull(effort);

            // Recipient lives at town-b; the letter must travel by post.
            RecruitmentLetter letter = service.SendLetter(effort.EffortId, 7,
                "town-a", "town-b", journeys, 0, diag, postal);
            Assert.IsNotNull(letter);
            Assert.IsNotEmpty(letter.PostalMailId, "The letter must be a real postal item (NX-2A).");

            // Day 0: posted Monday, dispatched — but not yet arrived.
            postal.AdvanceDay(journeys, 0, diag);
            service.AdvanceDay(population, null, 0, diag, postal);
            Assert.IsFalse(letter.Resolved, "No instant information transfer — the reply cannot exist before arrival.");

            // Day 1: arrives at po-b; now it can resolve.
            postal.AdvanceDay(journeys, 1, diag);
            service.AdvanceDay(population, null, 1, diag, postal);
            Assert.IsTrue(letter.Resolved);
        }

        [Test]
        public void SaveRoundTrip_PreservesNetwork()
        {
            var journeys = TwoTowns();
            var postal = TwoOffices();
            var diag = new List<string>();
            postal.PostItem(MailKind.Letter, "Kennedy", 1, "Emma", "po-a", "po-b", 0, diag);
            postal.OfferContract("po-a", "loop", "biz-freight-1", 150, 0, diag);
            postal.AdvanceDay(journeys, 0, diag);

            PostalServiceSaveDto dto = postal.CaptureSaveDto();
            var restored = new PostalService();
            restored.LoadFromSaveDto(dto);

            Assert.AreEqual(2, new List<PostalOffice>(restored.Offices).Count);
            Assert.AreEqual(1, new List<MailItem>(restored.MailItems).Count);
            Assert.AreEqual(1, new List<PostalContract>(restored.Contracts).Count);
            Assert.AreEqual(postal.UncollectedPostageCents, restored.UncollectedPostageCents);
            MailItem item = restored.GetMailItem(new List<MailItem>(restored.MailItems)[0].MailId);
            Assert.AreEqual(MailStatus.InTransit, item.Status, "In-transit mail survives save/load mid-journey.");
        }
    }
}
