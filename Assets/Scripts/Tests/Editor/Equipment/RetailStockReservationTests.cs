using System.Collections.Generic;
using NUnit.Framework;

namespace LandLedgers.Economy.Equipment.Tests
{
    /// <summary>
    /// Lane 9e: retail stock reservations on the EXISTING unified authority —
    /// available-to-promise net-out, first-wins refusal, idempotent release,
    /// and per-lot quote holds. No second reservation system: every hold
    /// here is an ordinary authority reservation on a retail-stock or lot key.
    /// </summary>
    [TestFixture]
    public sealed class RetailStockReservationTests
    {
        private EquipmentReservationService authority;

        [SetUp]
        public void SetUp()
        {
            authority = new EquipmentReservationService();
        }

        [Test]
        public void KeyConvention_NamesSupplierAndItem()
        {
            Assert.AreEqual("retail-stock:store-1:flour",
                ReservationKeyConventions.RetailStock("store-1", "flour"));
            Assert.AreEqual("lot:yard-lot-9",
                ReservationKeyConventions.Lot("yard-lot-9"));
        }

        [Test]
        public void GameSeconds_MatchesDayAndMinute()
        {
            Assert.AreEqual(100 * 86400f + 600 * 60f, RetailStockReservation.GameSeconds(100, 600));
            Assert.AreEqual(0f, RetailStockReservation.GameSeconds(0, 0));
        }

        [Test]
        public void AvailableUnits_NetsOutLiveHolds()
        {
            float at = RetailStockReservation.GameSeconds(100, 600);
            Assert.AreEqual(5, RetailStockReservation.AvailableUnits(authority, "store-1", "flour", 5, at),
                "no holds: all stock is promiseable");

            string holdA = RetailStockReservation.TryReserveUnits(
                authority, "store-1", "flour", 3, 5, "trip-a", "shopping trip for H7",
                at, at + 3 * 3600f, null);
            Assert.IsFalse(string.IsNullOrWhiteSpace(holdA), "first hold must succeed");
            Assert.AreEqual(2, RetailStockReservation.AvailableUnits(authority, "store-1", "flour", 5, at),
                "three held of five leaves two promiseable");

            // Outside the hold window the stock is free again.
            Assert.AreEqual(5, RetailStockReservation.AvailableUnits(authority, "store-1", "flour", 5, at + 4 * 3600f),
                "a hold binds only inside its window");
        }

        [Test]
        public void SecondHoldOverCapacity_IsRefusedAndHoldsNothing()
        {
            float at = RetailStockReservation.GameSeconds(100, 600);
            string holdA = RetailStockReservation.TryReserveUnits(
                authority, "store-1", "flour", 3, 5, "trip-a", "shopping trip for H7",
                at, at + 3 * 3600f, null);
            Assert.IsNotNull(holdA);

            var diag = new List<string>();
            string holdB = RetailStockReservation.TryReserveUnits(
                authority, "store-1", "flour", 3, 5, "trip-b", "shopping trip for H8",
                at, at + 3 * 3600f, diag);
            Assert.IsNull(holdB, "three more cannot be promised when only two are free");
            Assert.AreEqual(1, diag.Count, "the refusal must name the conflict");
            Assert.IsTrue(diag[0].Contains("trip-a"), "the refusal must name the holding trip: " + diag[0]);

            // The refused trip holds nothing: a smaller hold still fits.
            string holdB2 = RetailStockReservation.TryReserveUnits(
                authority, "store-1", "flour", 2, 5, "trip-b", "shopping trip for H8",
                at, at + 3 * 3600f, null);
            Assert.IsNotNull(holdB2, "two units still fit after the refused three");
            Assert.AreEqual(0, RetailStockReservation.AvailableUnits(authority, "store-1", "flour", 5, at));
            Assert.AreEqual(2, authority.LiveReservationCount, "exactly the two granted holds exist");
        }

        [Test]
        public void ReleaseFreesStock_AndIsIdempotent()
        {
            float at = RetailStockReservation.GameSeconds(100, 600);
            string holdA = RetailStockReservation.TryReserveUnits(
                authority, "store-1", "flour", 3, 5, "trip-a", "shopping trip for H7",
                at, at + 3 * 3600f, null);
            Assert.AreEqual(2, RetailStockReservation.AvailableUnits(authority, "store-1", "flour", 5, at));

            RetailStockReservation.Release(authority, holdA);
            Assert.AreEqual(5, RetailStockReservation.AvailableUnits(authority, "store-1", "flour", 5, at),
                "releasing the hold frees the stock");
            Assert.AreEqual(0, authority.LiveReservationCount);

            RetailStockReservation.Release(authority, holdA);
            RetailStockReservation.Release(authority, null);
            RetailStockReservation.Release(authority, string.Empty);
            Assert.AreEqual(0, authority.LiveReservationCount, "releasing an unknown id is a no-op");
        }

        [Test]
        public void StockDropBelowHeldTotals_FailsClosed()
        {
            float at = RetailStockReservation.GameSeconds(100, 600);
            string holdA = RetailStockReservation.TryReserveUnits(
                authority, "store-1", "flour", 3, 5, "trip-a", "shopping trip for H7",
                at, at + 3 * 3600f, null);
            Assert.IsNotNull(holdA);

            // A direct sale consumes two units outside the reservation path;
            // the re-registered capacity (3) still carries the hold of 3.
            Assert.AreEqual(0, RetailStockReservation.AvailableUnits(authority, "store-1", "flour", 3, at),
                "available floors at 0, never negative, when stock drops below held totals");
        }

        [Test]
        public void ZeroStock_NothingToHold()
        {
            float at = RetailStockReservation.GameSeconds(100, 600);
            Assert.AreEqual(0, RetailStockReservation.AvailableUnits(authority, "store-1", "flour", 0, at));
            Assert.IsNull(RetailStockReservation.TryReserveUnits(
                authority, "store-1", "flour", 1, 0, "trip-a", "shopping trip for H7",
                at, at + 3600f, null), "zero stock holds nothing");
            Assert.AreEqual(0, authority.LiveReservationCount);
        }

        [Test]
        public void NullAuthority_DegradesToLiveStock()
        {
            float at = RetailStockReservation.GameSeconds(100, 600);
            Assert.AreEqual(5, RetailStockReservation.AvailableUnits(null, "store-1", "flour", 5, at),
                "unwired: evaluation plans against live stock");
            Assert.IsNull(RetailStockReservation.TryReserveUnits(
                null, "store-1", "flour", 3, 5, "trip-a", "shopping trip for H7",
                at, at + 3600f, null), "unwired: no hold is recorded");
            RetailStockReservation.Release(null, "res-1");
        }

        [Test]
        public void LotHolds_CountPerLot_AndExcludeOwnSale()
        {
            float at = RetailStockReservation.GameSeconds(100, 720);
            // Production registers the lot's capacity (= lot units) before
            // reserving, as TryReserveQuotedUnits does; an unregistered key
            // reads as the exclusive default capacity of 1.
            Assert.IsNull(authority.RegisterResourceCapacity(
                ReservationKeyConventions.Lot("yard-lot-9"), 10f));
            var request = new ReservationRequest("carpentry-procurement:shop-1", "yard quote for 'shop-1'");
            request.Items.Add(new ReservationItem(ReservationKeyConventions.Lot("yard-lot-9"), 4, at - 3600f, at + 3600f));
            Assert.IsNull(authority.TryReserveAll(request, out var handle), "the quote hold must succeed");
            Assert.IsNotNull(handle);

            Assert.AreEqual(4, RetailStockReservation.ReservedUnitsForLot(authority, "yard-lot-9", 10, at),
                "four of ten lot units are held");
            Assert.AreEqual(0, RetailStockReservation.ReservedUnitsForLotExcludingOwner(
                    authority, "yard-lot-9", 10, at, "carpentry-procurement:shop-1"),
                "the buyer's own quote hold never blocks its own sale");
            Assert.AreEqual(4, RetailStockReservation.ReservedUnitsForLotExcludingOwner(
                    authority, "yard-lot-9", 10, at, "carpentry-procurement:shop-2"),
                "another buyer's sale still sees the hold");
            Assert.AreEqual(0, RetailStockReservation.ReservedUnitsForLot(authority, "yard-lot-9", 10, at + 7200f),
                "outside the hold window the lot is free");
        }
    }
}
