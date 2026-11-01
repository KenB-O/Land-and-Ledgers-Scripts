using System.Collections.Generic;
using LandLedgers.Economy;
using LandLedgers.Economy.Equipment;
using LandLedgers.Population;
using NUnit.Framework;

namespace LandLedgers.EditorTests.Economy
{
    /// <summary>
    /// G-03: the rental fee is real money. RentOut debits the renter's
    /// household ledger refusal-first (insufficient funds means no reservation
    /// is held) and credits the owning business runtime; Return settles
    /// assessed damage liability through the same legitimate cash legs.
    /// </summary>
    [TestFixture]
    public sealed class RentalFleetCashLegTests
    {
        private sealed class Fixture
        {
            public readonly RentalFleet Fleet = new RentalFleet { BusinessInstanceId = "livery-1" };
            public readonly EquipmentReservationService Reservations = new EquipmentReservationService();
            public readonly List<string> Diagnostics = new List<string>();
            public readonly HouseholdLedger RenterLedger;
            public readonly BusinessRuntimeState OwnerRuntime = new BusinessRuntimeState();

            public Fixture(int renterFundsCents)
            {
                Fleet.AddToFleet("buggy-1");
                RenterLedger = new HouseholdLedger(101);
                if (renterFundsCents > 0)
                {
                    Assert.IsNull(RenterLedger.RecordInflow(0, renterFundsCents,
                        HouseholdIncomeSource.Remittance, "test-setup", "test funding", "test"));
                }
            }

            public string RentOut(int feeCents = 500, float conditionAtRent = 1.0f)
            {
                return Fleet.RentOut("buggy-1", "person", "p1", "Traveler", 30, feeCents,
                    "day trip", Reservations, null, Diagnostics, RenterLedger, OwnerRuntime, 0, conditionAtRent);
            }
        }

        [Test]
        public void RentOut_CollectsFee_DebitsRenterCreditsBusiness()
        {
            var f = new Fixture(10000);

            Assert.IsNull(f.RentOut());

            Assert.AreEqual(9500, f.RenterLedger.GetBalanceCents(), "The renter paid the 500c fee.");
            Assert.AreEqual(500, f.OwnerRuntime.CurrentCashCents, "The owning business was credited the 500c fee.");
            Assert.IsTrue(f.Fleet.IsRentedOut("buggy-1"));
        }

        [Test]
        public void RentOut_InsufficientFunds_Refuses_NoReservationTaken()
        {
            var f = new Fixture(100); // cannot cover the 500c fee

            string refusal = f.RentOut();

            Assert.IsNotNull(refusal, "Insufficient funds: no rental.");
            Assert.IsFalse(f.Fleet.IsRentedOut("buggy-1"), "No reservation is held when the renter cannot pay.");
            Assert.IsFalse(f.Reservations.IsReserved(EquipmentReservationService.AssetKey("buggy-1")),
                "Any hold taken before the refusal is released.");
            Assert.AreEqual(100, f.RenterLedger.GetBalanceCents(), "Nothing moved.");
            Assert.AreEqual(0, f.OwnerRuntime.CurrentCashCents, "No manufactured revenue.");
        }

        [Test]
        public void RentOut_PositiveFee_WithoutCashAuthorities_Refuses()
        {
            var fleet = new RentalFleet { BusinessInstanceId = "livery-1" };
            fleet.AddToFleet("buggy-1");
            var reservations = new EquipmentReservationService();
            var diagnostics = new List<string>();

            string refusal = fleet.RentOut("buggy-1", "person", "p1", "Traveler", 30, 500,
                "day trip", reservations, null, diagnostics);

            Assert.IsNotNull(refusal, "No payer authority, no rental — revenue is never manufactured.");
            Assert.IsFalse(fleet.IsRentedOut("buggy-1"));
        }

        [Test]
        public void RentOut_ZeroFee_NeedsNoCashAuthorities()
        {
            var fleet = new RentalFleet { BusinessInstanceId = "livery-1" };
            fleet.AddToFleet("buggy-1");
            var reservations = new EquipmentReservationService();
            var diagnostics = new List<string>();

            Assert.IsNull(fleet.RentOut("buggy-1", "person", "p1", "Traveler", 30, 0,
                "comped trip", reservations, null, diagnostics),
                "A zero-fee (loaned/comped) rental needs no cash legs.");
            Assert.IsTrue(fleet.IsRentedOut("buggy-1"));
        }

        [Test]
        public void Return_Damage_SettlesLiabilityThroughCashLegs()
        {
            var f = new Fixture(10000);
            Assert.IsNull(f.RentOut());

            Assert.IsNull(f.Fleet.Return("buggy-1", 0.9f, f.Diagnostics, f.Reservations,
                f.RenterLedger, f.OwnerRuntime, damageValueBasisCents: 2000, dayIndex: 1));

            // Damage: (1.0 - 0.9) * 2000c basis = 200c.
            Assert.AreEqual(9300, f.RenterLedger.GetBalanceCents(), "Fee 500c + damage 200c debited.");
            Assert.AreEqual(700, f.OwnerRuntime.CurrentCashCents, "Fee 500c + damage 200c credited.");
            Assert.IsFalse(f.Fleet.IsRentedOut("buggy-1"));
        }

        [Test]
        public void Return_Damage_RenterCannotCover_RecordedUnpaid_ReturnCompletes()
        {
            var f = new Fixture(600); // covers the 500c fee, not the 200c damage
            Assert.IsNull(f.RentOut());

            Assert.IsNull(f.Fleet.Return("buggy-1", 0.9f, f.Diagnostics, f.Reservations,
                f.RenterLedger, f.OwnerRuntime, damageValueBasisCents: 2000, dayIndex: 1),
                "The physical return completes even when the renter cannot pay.");

            Assert.AreEqual(100, f.RenterLedger.GetBalanceCents(), "Only the fee moved.");
            Assert.AreEqual(500, f.OwnerRuntime.CurrentCashCents, "No damage credit without a real debit.");
            StringAssert.Contains("UNPAID", string.Join(" ", f.Diagnostics.ToArray()),
                "The unpaid liability is recorded loudly, not forgiven.");
            Assert.IsFalse(f.Fleet.IsRentedOut("buggy-1"));
        }

        [Test]
        public void Return_Damage_WithoutBasisOrAuthorities_LogsOnly_NeverInvented()
        {
            var f = new Fixture(10000);
            Assert.IsNull(f.RentOut());

            Assert.IsNull(f.Fleet.Return("buggy-1", 0.9f, f.Diagnostics, f.Reservations));

            Assert.AreEqual(9500, f.RenterLedger.GetBalanceCents(), "Only the fee moved.");
            Assert.AreEqual(500, f.OwnerRuntime.CurrentCashCents, "No invented damage revenue.");
            StringAssert.Contains("never invented", string.Join(" ", f.Diagnostics.ToArray()));
        }
    }
}
