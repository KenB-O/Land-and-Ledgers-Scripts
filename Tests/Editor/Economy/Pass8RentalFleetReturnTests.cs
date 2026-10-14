using System.Collections.Generic;
using LandLedgers.Economy.Equipment;
using NUnit.Framework;

namespace LandLedgers.EditorTests.Economy
{
    /// <summary>
    /// Pass VIII Worker 7: a returned rig must be rentable again. RentOut
    /// takes a unified-authority hold on the asset; Return released the
    /// rental record but never released the hold — the rig stayed reserved
    /// forever and could never be rented or used again (a leaked hold on a
    /// shared asset). One physical resource serves multiple purposes over
    /// time, but a leaked reservation serves none.
    /// </summary>
    [TestFixture]
    public sealed class Pass8RentalFleetReturnTests
    {
        [Test]
        public void Return_ReleasesTheReservation_RigIsRentableAgain()
        {
            var fleet = new RentalFleet { BusinessInstanceId = "livery-1" };
            fleet.AddToFleet("buggy-1");
            var reservations = new EquipmentReservationService();
            var diagnostics = new List<string>();

            Assert.IsNull(fleet.RentOut("buggy-1", "person", "p1", "Traveler", 30, 500,
                "day trip", reservations, null, diagnostics));
            Assert.IsTrue(fleet.IsRentedOut("buggy-1"));
            Assert.IsTrue(reservations.IsReserved(EquipmentReservationService.AssetKey("buggy-1")));

            Assert.IsNull(fleet.Return("buggy-1", 0.95f, diagnostics, reservations));
            Assert.IsFalse(fleet.IsRentedOut("buggy-1"));
            Assert.IsFalse(reservations.IsReserved(EquipmentReservationService.AssetKey("buggy-1")),
                "the hold is released on return — the rig can serve its next purpose");

            // And the rig genuinely rents again.
            Assert.IsNull(fleet.RentOut("buggy-1", "person", "p2", "Other", 40, 500,
                "second trip", reservations, null, diagnostics));
            Assert.IsTrue(fleet.IsRentedOut("buggy-1"));
        }

        [Test]
        public void Return_WithoutReservationsService_WarnsLoudly()
        {
            var fleet = new RentalFleet { BusinessInstanceId = "livery-1" };
            fleet.AddToFleet("buggy-1");
            var reservations = new EquipmentReservationService();
            var diagnostics = new List<string>();

            Assert.IsNull(fleet.RentOut("buggy-1", "person", "p1", "Traveler", 30, 500,
                "day trip", reservations, null, diagnostics));
            // Legacy call shape (no service): still succeeds, but warns that
            // the hold was not released.
            Assert.IsNull(fleet.Return("buggy-1", 0.95f, diagnostics));
            Assert.IsTrue(reservations.IsReserved(EquipmentReservationService.AssetKey("buggy-1")),
                "without the service the hold leaks — and the diagnostics say so");
            StringAssert.Contains("NOT released", string.Join(" ", diagnostics.ToArray()));
        }
    }
}
