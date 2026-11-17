using System.Collections.Generic;
using LandLedgers.Population;
using LandLedgers.World.Property;
using NUnit.Framework;

namespace LandLedgers.EditorTests.World
{
    /// <summary>
    /// M1-R1: the daily housing-search drive, boarding only. The household
    /// housing loop was dead end to end (F-M1-1 through F-M1-3, F-M1-9). These
    /// tests prove the drive closes the boarding leg through the real
    /// planner, the real adapter, and the real executor, and that it never
    /// creates a rent-free tenancy (F-M1-5) and never double-houses a
    /// suitably housed household.
    /// </summary>
    [TestFixture]
    public sealed class HousingSearchDriveTests
    {
        private sealed class FakeBoarderPort : IBoarderCheckInPort
        {
            public readonly List<int> CheckedIn = new List<int>();

            public string CheckIn(int personId, int dayIndex, List<string> diagnostics)
            {
                CheckedIn.Add(personId);
                return null;
            }
        }

        private sealed class Fixture
        {
            public PopulationState State = new PopulationState();
            public HousingSearchNeedRegistry Needs = new HousingSearchNeedRegistry();
            public HousingAuthority Housing = new HousingAuthority();
            public LodgingRegistry Lodging = new LodgingRegistry();
            public HouseholdLedgerRegistry Ledgers = new HouseholdLedgerRegistry();
            public FakeBoarderPort Port = new FakeBoarderPort();
            public HashSet<int> AlreadyBoarded = new HashSet<int>();
            public List<string> Diag = new List<string>();

            public Fixture WithHousehold(int householdId, int cashCents, params int[] personAges)
            {
                var memberIds = new List<int>();
                for (int i = 0; i < personAges.Length; i++)
                {
                    int personId = householdId * 100 + i + 1;
                    memberIds.Add(personId);
                    State.people.Add(new PersonState
                    {
                        id = personId,
                        age = personAges[i],
                        householdId = householdId,
                        deathDayIndex = -1,
                    });
                }
                State.households.Add(new HouseholdState { id = householdId, memberIds = memberIds });
                if (cashCents > 0)
                {
                    Ledgers.GetOrCreate(householdId).RecordInflow(
                        10, cashCents, HouseholdIncomeSource.WageEmployment,
                        "test-wages", "test inflow", "test employer");
                }
                return this;
            }

            public Fixture WithBoardingRoom(string businessId, string roomId, int beds, int weeklyRateCents)
            {
                Housing.RegisterBuilding("b-board", "plot-7", "boarding-house", 0, "test fixture", Diag);
                AccommodationSpace space = Housing.DefineSpace("b-board", beds, Diag);
                Assert.IsNotNull(space, "test fixture: boarding space could not be defined.");
                string refusal = Lodging.RegisterLodging(
                    businessId, roomId, beds, space.SpaceId, 25, weeklyRateCents, 10, Housing, Diag);
                Assert.IsNull(refusal, "test fixture: lodging registration refused: " + refusal);
                return this;
            }

            public Fixture WithVacantSpace(int beds)
            {
                Housing.RegisterBuilding("b-1", "p-1", "house", 0, "test fixture", Diag);
                AccommodationSpace space = Housing.DefineSpace("b-1", beds, Diag);
                Assert.IsNotNull(space, "test fixture: vacant space could not be defined.");
                return this;
            }

            public HousingSearchDrive.DriveResult Drive(int dayIndex = 10)
            {
                return HousingSearchDrive.Drive(
                    State, Needs, Housing, Lodging,
                    businessId => businessId == "biz-board" ? (IBoarderCheckInPort)Port : null,
                    personId => AlreadyBoarded.Contains(personId),
                    Ledgers, dayIndex, Diag);
            }
        }

        [Test]
        public void UnshelteredHouseholdBoardsAndNeedResolves()
        {
            var f = new Fixture();
            f.WithHousehold(1, 2000, 34, 31);
            f.WithBoardingRoom("biz-board", "room-1", 2, 100);
            f.Needs.OpenNeed(1, 2, ResidentialConditionKind.Unsheltered, "test", 10);

            HousingSearchDrive.DriveResult result = f.Drive();

            Assert.AreEqual(1, result.Boarded);
            Assert.AreEqual(0, result.StillOpen);
            Assert.IsNull(f.Needs.GetOpenNeed(1), "need should resolve once the household is boarded.");
            Assert.AreEqual(2, f.Port.CheckedIn.Count);
            Assert.IsTrue(f.Port.CheckedIn.Contains(101));
            Assert.IsTrue(f.Port.CheckedIn.Contains(102));
        }

        [Test]
        public void NoBoardingVacancyKeepsNeedOpen()
        {
            var f = new Fixture();
            f.WithHousehold(1, 2000, 34, 31);
            f.Needs.OpenNeed(1, 2, ResidentialConditionKind.Unsheltered, "test", 10);

            HousingSearchDrive.DriveResult result = f.Drive();

            Assert.AreEqual(0, result.Boarded);
            Assert.AreEqual(1, result.StillOpen);
            Assert.IsNotNull(f.Needs.GetOpenNeed(1), "need stays open when no boarding vacancy exists.");
            Assert.AreEqual(0, f.Port.CheckedIn.Count);
        }

        [Test]
        public void UnaffordableBoardingKeepsNeedOpen()
        {
            var f = new Fixture();
            f.WithHousehold(1, 2000, 34, 31);
            f.WithBoardingRoom("biz-board", "room-1", 2, 1000);
            f.Needs.OpenNeed(1, 2, ResidentialConditionKind.Unsheltered, "test", 10);

            HousingSearchDrive.DriveResult result = f.Drive();

            Assert.AreEqual(0, result.Boarded);
            Assert.AreEqual(1, result.StillOpen);
            Assert.AreEqual(0, f.Port.CheckedIn.Count);
        }

        [Test]
        public void VacantRentalSpaceAloneNeverCreatesRentFreeTenancy()
        {
            var f = new Fixture();
            f.WithHousehold(1, 2000, 34, 31);
            f.WithVacantSpace(3);
            f.Needs.OpenNeed(1, 2, ResidentialConditionKind.Unsheltered, "test", 10);

            HousingSearchDrive.DriveResult result = f.Drive();

            Assert.AreEqual(0, result.Boarded, "drive executes boarding only, never a priceless rental.");
            Assert.AreEqual(1, result.StillOpen);
            Assert.IsNotNull(f.Needs.GetOpenNeed(1));
            foreach (AccommodationSpace space in f.Housing.VacantSpaces())
            {
                Assert.AreEqual(0, f.Housing.CurrentOccupants(space.SpaceId).Count,
                    "no occupancy may be created from a rental option that names no rent.");
            }
        }

        [Test]
        public void SuitablyHousedNeedWaitsForRentalLeg()
        {
            var f = new Fixture();
            f.WithHousehold(1, 2000, 34, 31);
            f.WithBoardingRoom("biz-board", "room-1", 2, 100);
            f.Needs.OpenNeed(1, 2, ResidentialConditionKind.SuitablyHoused, "relocate cheaper", 10);

            HousingSearchDrive.DriveResult result = f.Drive();

            Assert.AreEqual(0, result.Boarded, "boarding a suitably housed household would double-house it.");
            Assert.AreEqual(1, result.StillOpen);
            Assert.AreEqual(0, f.Port.CheckedIn.Count);
        }

        [Test]
        public void AlreadyBoardedHouseholdResolvesWithoutSecondCheckIn()
        {
            var f = new Fixture();
            f.WithHousehold(1, 2000, 34, 31);
            f.WithBoardingRoom("biz-board", "room-1", 2, 100);
            f.AlreadyBoarded.Add(101);
            f.AlreadyBoarded.Add(102);
            f.Needs.OpenNeed(1, 2, ResidentialConditionKind.Unsheltered, "test", 10);

            HousingSearchDrive.DriveResult result = f.Drive();

            Assert.AreEqual(1, result.Boarded);
            Assert.IsNull(f.Needs.GetOpenNeed(1));
            Assert.AreEqual(0, f.Port.CheckedIn.Count, "nobody is checked in twice.");
        }

        [Test]
        public void LodgingRegistrationFeedsAdapterVacanciesWithRealRateAndPayee()
        {
            var f = new Fixture();
            f.WithBoardingRoom("biz-board", "room-1", 2, 100);

            var adapter = new HousingOpportunityAdapter(f.Housing, f.Lodging);
            List<HousingSearchOption> vacancies = adapter.BoardingVacancies(2);

            Assert.AreEqual(1, vacancies.Count);
            Assert.AreEqual(HousingSearchPathKind.Boarding, vacancies[0].PathKind);
            Assert.AreEqual("biz-board", vacancies[0].ProviderName);
            Assert.AreEqual("business:biz-board", vacancies[0].PayeeHolderId);
            Assert.AreEqual(400, vacancies[0].RecurringMonthlyCents, "monthly board is the weekly rate times four.");
            Assert.AreEqual(2, vacancies[0].PlacesProvided);
        }
    }
}
