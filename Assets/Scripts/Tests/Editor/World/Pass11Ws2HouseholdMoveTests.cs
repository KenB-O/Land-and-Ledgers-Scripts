using System.Collections.Generic;
using LandLedgers.World.Property;
using NUnit.Framework;

namespace LandLedgers.EditorTests.World
{
    /// <summary>
    /// Pass XI WS2 (audit finding A-10): HouseholdMoveService proofs for
    /// the routing decision. The service is caller-free in production, and
    /// these tests establish exactly what its all-or-nothing guarantee
    /// covers, so the decision to route (or not route) a live path through
    /// it rests on evidence: validation before any mutation, old
    /// occupancies ended so no household holds incompatible simultaneous
    /// housing rights after a move, commercial capacity respected, and
    /// refusals that leave every prior occupancy intact.
    /// </summary>
    [TestFixture]
    public sealed class Pass11Ws2HouseholdMoveTests
    {
        private sealed class Fixture
        {
            public HousingAuthority Housing = new HousingAuthority();
            public List<string> Diag = new List<string>();

            public string BuildingWithSpace(string buildingId, string parcelId, int capacity)
            {
                Housing.RegisterBuilding(buildingId, parcelId, "house", 0, "test", Diag);
                AccommodationSpace space = Housing.DefineSpace(buildingId, capacity, Diag);
                Assert.IsNotNull(space);
                return space.SpaceId;
            }
        }

        [Test]
        public void WholeHouseholdMove_EndsOldOccupancies_NoDualHousingRights()
        {
            var f = new Fixture();
            string oldSpace = f.BuildingWithSpace("b-old", "p-old", 4);
            string newSpace = f.BuildingWithSpace("b-new", "p-new", 4);
            Assert.IsNotNull(f.Housing.Occupy(1, 10, oldSpace, AccommodationArrangement.OwnerOccupied, 0, f.Diag));
            Assert.IsNotNull(f.Housing.Occupy(2, 10, oldSpace, AccommodationArrangement.OwnerOccupied, 0, f.Diag));
            Assert.AreEqual(2, f.Housing.OccupantCount(oldSpace));

            var mover = new HouseholdMoveService();
            HouseholdMoveResult result = mover.MoveHousehold(10, new List<int> { 1, 2 }, newSpace,
                AccommodationArrangement.OwnerOccupied, "relocation", "Seller", 5, f.Housing, f.Diag);
            Assert.IsTrue(result.Moved, result.Refusal);
            Assert.IsFalse(string.IsNullOrWhiteSpace(result.AgreementId));
            Assert.AreEqual(2, result.OccupancyIds.Count);
            Assert.AreEqual(0, f.Housing.OccupantCount(oldSpace), "the old space is freed by the move");
            Assert.AreEqual(2, f.Housing.OccupantCount(newSpace));
            Assert.IsTrue(result.VacatedSpaceIds.Contains(oldSpace));
            foreach (int personId in new[] { 1, 2 })
            {
                int inNew = 0;
                foreach (ResidentialOccupancy occ in f.Housing.CurrentOccupanciesForPerson(personId))
                    if (occ != null && occ.SpaceId == newSpace) inNew++;
                Assert.AreEqual(1, inNew, $"P{personId} holds exactly one current occupancy, in the new space");
            }
        }

        [Test]
        public void OverCapacityCommercialMove_Refused_BeforeAnyMutation()
        {
            var f = new Fixture();
            string homeSpace = f.BuildingWithSpace("b-home", "p-home", 4);
            string lodgingSpace = f.BuildingWithSpace("b-lodge", "p-lodge", 1);
            Assert.IsNotNull(f.Housing.Occupy(1, 10, homeSpace, AccommodationArrangement.OwnerOccupied, 0, f.Diag));
            Assert.IsNotNull(f.Housing.Occupy(2, 10, homeSpace, AccommodationArrangement.OwnerOccupied, 0, f.Diag));
            Assert.IsNotNull(f.Housing.Occupy(9, 99, lodgingSpace, AccommodationArrangement.BoardingOrLodging, 0, f.Diag));

            var mover = new HouseholdMoveService();
            HouseholdMoveResult result = mover.MoveHousehold(10, new List<int> { 1, 2 }, lodgingSpace,
                AccommodationArrangement.BoardingOrLodging, "boarding", "Innkeeper", 5, f.Housing, f.Diag);
            Assert.IsFalse(result.Moved, "one bed cannot hold the staying boarder plus two movers");
            Assert.IsFalse(string.IsNullOrWhiteSpace(result.Refusal));
            Assert.AreEqual(2, f.Housing.OccupantCount(homeSpace), "a refused move leaves prior occupancies intact");
            Assert.AreEqual(1, f.Housing.OccupantCount(lodgingSpace));
            Assert.AreEqual(0, result.OccupancyIds.Count);
        }

        [Test]
        public void UnknownSpaceAndUnstatedArrangement_Refused_NoMutation()
        {
            var f = new Fixture();
            string homeSpace = f.BuildingWithSpace("b-home", "p-home", 4);
            Assert.IsNotNull(f.Housing.Occupy(1, 10, homeSpace, AccommodationArrangement.OwnerOccupied, 0, f.Diag));

            var mover = new HouseholdMoveService();
            HouseholdMoveResult unknown = mover.MoveHousehold(10, new List<int> { 1 }, "space-nope",
                AccommodationArrangement.OwnerOccupied, "relocation", "Seller", 5, f.Housing, f.Diag);
            Assert.IsFalse(unknown.Moved);
            string newSpace = f.BuildingWithSpace("b-new", "p-new", 4);
            HouseholdMoveResult unstated = mover.MoveHousehold(10, new List<int> { 1 }, newSpace,
                AccommodationArrangement.Unspecified, "relocation", "Seller", 5, f.Housing, f.Diag);
            Assert.IsFalse(unstated.Moved, "arrangements are stated, never defaulted");
            Assert.AreEqual(1, f.Housing.OccupantCount(homeSpace));
            Assert.AreEqual(0, f.Housing.OccupantCount(newSpace));
        }
    }
}
