using System.Collections.Generic;
using System.Linq;
using LandLedgers.Economy;
using LandLedgers.Economy.Recruitment;
using LandLedgers.Population;
using LandLedgers.World.Journeys;
using NUnit.Framework;

namespace LandLedgers.EditorTests.Economy
{
    /// <summary>
    /// T2B: recruitment is a workflow over time. Inquiries come from ACTUAL
    /// persons — never a spawned candidate list. Canon Part VI §6.2/6.3,
    /// GHOST-DES-021/034/039/041/051.
    /// </summary>
    public sealed class RecruitmentTests
    {
        private PopulationState population;
        private EmploymentRelationshipRegistry employment;
        private RecruitmentService service;
        private List<string> diag;

        private static PersonState Person(int id, string first, string last, int householdId, string professionId = null)
        {
            return new PersonState
            {
                id = id,
                firstName = first,
                lastName = last,
                age = 30,
                ageBand = AgeBand.Adult18Plus,
                laborAccessLevel = LaborAccessLevel.FullLaborMarket,
                householdId = householdId,
                professionId = professionId,
            };
        }

        [SetUp]
        public void SetUp()
        {
            population = new PopulationState();
            // Household 1: the employer (id 1) + family member (id 2, a plausible referral lead).
            population.people.Add(Person(1, "Employer", "Ems", 1, "merchant"));
            population.people.Add(Person(2, "Kin", "Ems", 1, "clerk"));
            // Household 2: an unemployed stranger (id 3).
            population.people.Add(Person(3, "Stranger", "Sue", 2, "laborer"));
            // A child — never a candidate.
            population.people.Add(new PersonState
            {
                id = 4, firstName = "Kid", lastName = "Ems", age = 8,
                ageBand = AgeBand.Child0To9, laborAccessLevel = LaborAccessLevel.None, householdId = 1,
            });
            employment = new EmploymentRelationshipRegistry();
            service = new RecruitmentService();
            diag = new List<string>();
        }

        /// <summary>
        /// NX-3A test fake: a real newspaper ad market. The recruitment
        /// service buys through this — GHOST-DES-076.
        /// </summary>
        private sealed class FakeAdMarket : INewspaperAdMarket
        {
            public int PlacedCount;
            public string MarketName => "Test Gazette";
            public int RateForSizeCents(string sizeClass) => 500;
            public string PlaceAd(string advertiserBusinessId, string adText, string sizeClass,
                int insertions, int dayIndex, List<string> diag)
            {
                PlacedCount++;
                if (diag != null) diag.Add($"FakeAdMarket: placed test-ad-{PlacedCount} for '{advertiserBusinessId}'.");
                return $"test-ad-{PlacedCount}";
            }
        }

        private static INewspaperAdMarket TestMarket() => new FakeAdMarket();

        private static JourneyModel TwoTownJourney()
        {
            var journeys = new JourneyModel();
            journeys.RegisterLocation(new JourneyLocation("home", JourneyLocationKind.Farmstead, "Home", 0f, 0f));
            journeys.RegisterLocation(new JourneyLocation("town", JourneyLocationKind.TownBuilding, "Town", 2f, 0f));
            journeys.AddEdge("home", "town", 2.0f, "river road");
            return journeys;
        }

        [Test]
        public void OpenEffortRequiresARealChannelAndPricesPaidOnes()
        {
            var bad = service.OpenEffort("biz-1", "clerk", RecruitmentChannel.Unspecified, 0, 42, diag);
            Assert.IsNull(bad);

            // NX-3A: with no newspaper, the ad channel is refused — GHOST-DES-076.
            var noPaper = service.OpenEffort("biz-1", "clerk", RecruitmentChannel.NewspaperAd, 0, 42, diag);
            Assert.IsNull(noPaper, "No newspaper exists — no ad can be bought.");

            var ad = service.OpenEffort("biz-1", "clerk", RecruitmentChannel.NewspaperAd, 0, 42, diag, TestMarket());
            Assert.IsNotNull(ad);
            Assert.Greater(ad.CostCents, 0, "Newspaper ads cost real money — book through expenses.");

            var referral = service.OpenEffort("biz-1", "clerk", RecruitmentChannel.EmployeeReferral, 0, 43, diag);
            Assert.AreEqual(0, referral.CostCents, "Asking around is free but slow.");
        }

        [Test]
        public void InquiriesArriveOverTimeFromActualPersonsOnly()
        {
            var effort = service.OpenEffort("biz-1", "clerk", RecruitmentChannel.NewspaperAd, 0, 7, diag, TestMarket());

            for (int day = 0; day < 60; day++)
                service.AdvanceDay(population, employment, day, diag);

            Assert.Greater(service.Inquiries.Count, 0, "Ads produce inquiries over time.");
            foreach (CandidateInquiry inquiry in service.Inquiries)
            {
                Assert.IsNotNull(population.GetPerson(inquiry.PersonId),
                    $"Inquiry {inquiry.InquiryId} references P{inquiry.PersonId} — must be a real person.");
                Assert.AreNotEqual(4, inquiry.PersonId, "Children are never candidates.");
            }
        }

        [Test]
        public void NoCandidatePoolMeansNoFakedInquiry()
        {
            // Employ everyone employable: only the child remains, who is not a candidate.
            foreach (PersonState p in population.people.Where(p => p.ageBand == AgeBand.Adult18Plus))
            {
                employment.Register(new EmploymentRelationship
                {
                    Id = $"emp-{p.id}",
                    EmployeePersonId = p.id,
                    EmployerBusinessId = "biz-other",
                    RoleDisplayName = "hand",
                    LifecycleState = EmploymentLifecycleState.Active,
                    Compensation = CompensationTerms.FromWeeklyWage(500, "test"),
                    Source = EmploymentSource.Manual,
                });
            }
            var effort = service.OpenEffort("biz-1", "clerk", RecruitmentChannel.NewspaperAd, 0, 7, diag, TestMarket());
            for (int day = 0; day < 30; day++)
                service.AdvanceDay(population, employment, day, diag);

            Assert.AreEqual(0, service.Inquiries.Count, "Nobody available → no inquiry faked.");
        }

        [Test]
        public void ReferralCanProduceALeadOrHonestlyNone()
        {
            var effort = service.OpenEffort("biz-1", "clerk", RecruitmentChannel.EmployeeReferral, 0, 99, diag);

            // Run several times across seeds implicitly via day variation; at least
            // the call never invents a person.
            for (int day = 0; day < 10; day++)
            {
                CandidateInquiry lead = service.AskForReferral(effort.EffortId, 1, population, employment, day, diag);
                if (lead != null)
                {
                    Assert.IsNotNull(population.GetPerson(lead.PersonId), "Referral leads are real persons.");
                    return; // a lead is a valid outcome
                }
            }
            // No lead across all attempts is also a valid outcome (honest).
            Assert.Pass("No referral lead — an honest outcome per Canon §6.3.");
        }

        [Test]
        public void LetterTakesRealTravelTimeAndRecipientMayIgnore()
        {
            var journeys = TwoTownJourney();
            var effort = service.OpenEffort("biz-1", "clerk", RecruitmentChannel.Correspondence, 0, 5, diag);

            var letter = service.SendLetter(effort.EffortId, 3, "town", "home", journeys, 0, diag);
            Assert.IsNotNull(letter);
            Assert.Greater(letter.ArrivalDayIndex, letter.SentDayIndex, "Letters take real travel time (Canon §6.3).");

            // Before arrival: nothing happens.
            service.AdvanceDay(population, employment, 0, diag);
            Assert.AreEqual(0, service.Inquiries.Count);

            // After arrival: recipient replies, negotiates, inspects, or ignores — all honest.
            for (int day = 1; day <= letter.ArrivalDayIndex + 2; day++)
                service.AdvanceDay(population, employment, day, diag);
            // No assertion on outcome — any of the four is valid. The letter resolved.
            Assert.Pass();
        }

        [Test]
        public void LetterWithNoRouteIsNotSent()
        {
            var journeys = TwoTownJourney();
            var effort = service.OpenEffort("biz-1", "clerk", RecruitmentChannel.Correspondence, 0, 5, diag);
            var letter = service.SendLetter(effort.EffortId, 3, "town", "nowhere", journeys, 0, diag);
            Assert.IsNull(letter, "No route → no letter. The recipient is not contacted by magic.");
        }

        [Test]
        public void DirectApproachReachesAKnownPersonOnly()
        {
            var effort = service.OpenEffort("biz-1", "clerk", RecruitmentChannel.DirectContact, 0, 11, diag);

            var nobody = service.DirectApproach(effort.EffortId, 999, population, employment, 0, diag);
            Assert.IsNull(nobody, "Unknown persons cannot be approached.");

            var child = service.DirectApproach(effort.EffortId, 4, population, employment, 0, diag);
            Assert.IsNull(child, "Children cannot take the role.");
        }

        [Test]
        public void HireFromInquiryCreatesARealEmploymentRelationship()
        {
            var effort = service.OpenEffort("biz-1", "clerk", RecruitmentChannel.NewspaperAd, 0, 7, diag, TestMarket());
            for (int day = 0; day < 60 && service.Inquiries.Count == 0; day++)
                service.AdvanceDay(population, employment, day, diag);
            Assert.Greater(service.Inquiries.Count, 0);

            CandidateInquiry inquiry = service.Inquiries.First();
            string problem = service.HireFromInquiry(inquiry.InquiryId, 600, EmploymentKind.Permanent, 61, employment, diag);
            Assert.IsNull(problem);

            var rels = employment.GetActiveByEmployee(inquiry.PersonId);
            Assert.AreEqual(1, rels.Count);
            Assert.AreEqual("biz-1", rels[0].EmployerBusinessId);
            Assert.AreEqual(600, rels[0].Compensation.AgreedWeeklyWageCents);
            Assert.AreEqual(InquiryStatus.Hired, inquiry.Status);
        }

        [Test]
        public void UnpaidHireIsRefused()
        {
            var effort = service.OpenEffort("biz-1", "clerk", RecruitmentChannel.NewspaperAd, 0, 7, diag, TestMarket());
            for (int day = 0; day < 60 && service.Inquiries.Count == 0; day++)
                service.AdvanceDay(population, employment, day, diag);
            CandidateInquiry inquiry = service.Inquiries.First();

            string problem = service.HireFromInquiry(inquiry.InquiryId, 0, EmploymentKind.Permanent, 61, employment, diag);
            Assert.IsNotNull(problem, "Unpaid employment is not employment.");
            Assert.AreEqual(0, employment.GetActiveByEmployee(inquiry.PersonId).Count);
        }
    }
}
