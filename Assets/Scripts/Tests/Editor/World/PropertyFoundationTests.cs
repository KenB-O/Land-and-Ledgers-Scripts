using System.Collections.Generic;
using LandLedgers.Primitives;
using LandLedgers.World.Property;
using NUnit.Framework;

namespace LandLedgers.EditorTests.World
{
    /// <summary>
    /// T2F: property & housing foundation. Title follows an unbroken chain of
    /// real events; occupancy is distinct from membership; one building can
    /// hold two independent accommodation units; business use is assigned per
    /// space without double-booking.
    /// </summary>
    public sealed class PropertyFoundationTests
    {
        private TitleAuthority titles;
        private HousingAuthority housing;
        private EntityIdRegistry ids;
        private List<string> diag;

        [SetUp]
        public void SetUp()
        {
            titles = new TitleAuthority();
            housing = new HousingAuthority();
            ids = new EntityIdRegistry();
            diag = new List<string>();
        }

        [Test]
        public void TitleFollowsAnUnbrokenChainOfRealEvents()
        {
            titles.RegisterParcel("p-1", "lot 4", 2.5f, "Founder", TitleBasis.Grant, 0, diag);
            Assert.AreEqual("Founder", titles.CurrentHolder("p-1"));

            string problem = titles.TransferTitle(ids, "p-1", "Buyer", TitleBasis.Purchase, "deed-77", 100, "cash sale", diag);
            Assert.IsNull(problem);
            Assert.AreEqual("Buyer", titles.CurrentHolder("p-1"));
            Assert.AreEqual(2, titles.ChainOf("p-1").Count);
        }

        [Test]
        public void PurchaseWithoutAConveyanceIsNotATransfer()
        {
            titles.RegisterParcel("p-1", "lot 4", 2.5f, "Founder", TitleBasis.Grant, 0, diag);
            string problem = titles.TransferTitle(ids, "p-1", "Buyer", TitleBasis.Purchase, "", 100, "handshake deal", diag);
            Assert.IsNotNull(problem, "A purchase without a conveyance instrument is not a transfer.");
            Assert.AreEqual("Founder", titles.CurrentHolder("p-1"));
        }

        [Test]
        public void DoubleHouseIsOneBuildingWithTwoIndependentUnits()
        {
            housing.RegisterBuilding("b-1", "p-1", "double house", 50, "built by carpenter crew", diag);
            AccommodationSpace unitA = housing.DefineSpace("b-1", 4, diag);
            AccommodationSpace unitB = housing.DefineSpace("b-1", 3, diag);
            Assert.AreNotEqual(unitA.SpaceId, unitB.SpaceId);

            housing.Occupy(1, 10, unitA.SpaceId, AccommodationArrangement.OwnerOccupied, 60, diag);
            housing.Occupy(2, 20, unitB.SpaceId, AccommodationArrangement.Rental, 61, diag);

            Assert.AreEqual(1, housing.CurrentOccupants(unitA.SpaceId).Count);
            Assert.AreEqual(1, housing.CurrentOccupants(unitB.SpaceId).Count);
        }

        [Test]
        public void OccupancyIsDistinctFromHouseholdMembership()
        {
            housing.RegisterBuilding("b-1", "p-1", "bunkhouse", 50, "built", diag);
            AccommodationSpace bunk = housing.DefineSpace("b-1", 8, diag);

            // P7 is a member of H10 but sleeps in the employer's bunkhouse.
            ResidentialOccupancy occ = housing.Occupy(7, 10, bunk.SpaceId, AccommodationArrangement.EmployerLodging, 70, diag);

            Assert.IsNotNull(occ);
            Assert.AreEqual(7, occ.PersonId);
            Assert.AreEqual(10, occ.HouseholdId, "Membership (H10) recorded separately from where they sleep.");
            Assert.AreEqual(AccommodationArrangement.EmployerLodging, occ.Arrangement);
        }

        [Test]
        public void MovingEndsThePriorOccupancy()
        {
            housing.RegisterBuilding("b-1", "p-1", "house", 50, "built", diag);
            AccommodationSpace a = housing.DefineSpace("b-1", 4, diag);
            AccommodationSpace b = housing.DefineSpace("b-1", 4, diag);

            housing.Occupy(1, 10, a.SpaceId, AccommodationArrangement.Rental, 70, diag);
            housing.Occupy(1, 10, b.SpaceId, AccommodationArrangement.Rental, 80, diag);

            Assert.AreEqual(0, housing.CurrentOccupants(a.SpaceId).Count, "One bed at a time — the old occupancy ended.");
            Assert.AreEqual(1, housing.CurrentOccupants(b.SpaceId).Count);
        }

        [Test]
        public void FunctionalSpaceHasOneActiveAssignment()
        {
            housing.RegisterBuilding("b-2", "p-1", "store", 50, "built", diag);
            AccommodationSpace shop = housing.DefineSpace("b-2", 0, diag);

            Assert.IsNull(housing.AssignFunctionalSpace(shop.SpaceId, "biz-store", "retail counter", 90, diag));
            string problem = housing.AssignFunctionalSpace(shop.SpaceId, "biz-tailor", "cutting table", 91, diag);
            Assert.IsNotNull(problem, "One active assignment per space — no double-booking.");
        }

        [Test]
        public void AgreementsNameBothParties()
        {
            var agreement = housing.RecordAgreement("rental", "Landlord", "Tenant", "space-x",
                "$5/month, due the 1st", 100, -1, "", diag);
            Assert.IsNotNull(agreement);
            Assert.AreEqual("Landlord", agreement.GrantorName);
            Assert.AreEqual("Tenant", agreement.GranteeName);

            Assert.IsNull(housing.RecordAgreement("rental", "", "Tenant", "space-x", "terms", 100, -1, "", diag),
                "Anonymous grantors are refused.");
        }

        [Test]
        public void SaveLoadRoundTripsTitleAndHousing()
        {
            titles.RegisterParcel("p-1", "lot 4", 2.5f, "Founder", TitleBasis.Grant, 0, diag);
            titles.TransferTitle(ids, "p-1", "Buyer", TitleBasis.Purchase, "deed-77", 100, "", diag);
            housing.RegisterBuilding("b-1", "p-1", "house", 50, "built", diag);

            var titleDto = titles.CaptureSaveDto();
            var housingDto = housing.CaptureSaveDto();
            var titles2 = new TitleAuthority();
            var housing2 = new HousingAuthority();
            titles2.LoadFromSaveDto(titleDto);
            housing2.LoadFromSaveDto(housingDto);

            Assert.AreEqual("Buyer", titles2.CurrentHolder("p-1"));
            Assert.AreEqual(2, titles2.ChainOf("p-1").Count);
            Assert.AreEqual(1, housing2.CaptureSaveDto().Buildings.Count);
        }
    }
}
