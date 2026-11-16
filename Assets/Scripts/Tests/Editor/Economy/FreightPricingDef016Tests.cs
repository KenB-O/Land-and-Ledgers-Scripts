// DEF-016 (Decision 10): cargo-sensitive freight pricing + honest multi-trip loads.
// The hired-freight quote prices cargo (trips from real mass/volume against
// the real wagon), road conditions (the journey route's own multiplier),
// and discloses its terms; the construction coordinator executes the plan's
// trips, each priced and settled separately, with per-trip receipt into
// the site stockpile.
// Copyright (c) Land & Ledgers contributors. Licensed under the MIT License.

using System;
using System.Collections.Generic;
using LandLedgers.Economy;
using LandLedgers.Economy.Blacksmith;
using LandLedgers.Economy.Businesses.LumberYard;
using LandLedgers.Economy.Financing;
using LandLedgers.Economy.Freight;
using LandLedgers.Economy.Logistics;
using LandLedgers.Economy.Transport;
using LandLedgers.Population;
using LandLedgers.Primitives;
using LandLedgers.Tasks;
using LandLedgers.Time;
using NUnit.Framework;

namespace LandLedgers.EditorTests.Economy
{
    [TestFixture]
    public sealed class FreightPricingDef016Tests
    {
        private const float WagonMassKg = 450f;
        private const float WagonVolumeM3 = 4f;

        private static FreightCargoSpec LumberSpec() => LumberCargoSpec.Spec;

        private static bool TryCargo(float miles, int quantity, FreightCargoSpec spec,
            float wagonMassKg, float wagonVolumeM3, float conditionMultiplier,
            out FreightServiceQuote quote, out string reason)
        {
            return FreightServiceQuoteCalculator.TryCalculateCargo(miles, 25, 1, true,
                quantity, spec, wagonMassKg, wagonVolumeM3, conditionMultiplier,
                out quote, out reason);
        }

        [Test]
        public void CargoQuote_SingleTrip_MatchesOldOverload()
        {
            Assert.IsTrue(TryCargo(3.5f, 20, LumberSpec(), WagonMassKg, WagonVolumeM3, 1f,
                out FreightServiceQuote quote, out string reason), reason);
            Assert.AreEqual(1, quote.TripCount);
            Assert.AreEqual(28, quote.UnitsPerTrip);
            Assert.AreEqual(20, quote.TotalQuantityUnits);
            Assert.AreEqual(320f, quote.CargoMassKg, 0.001f);
            Assert.AreEqual(0.8f, quote.CargoVolumeM3, 0.001f);
            Assert.AreEqual(32, quote.PerTripTotalCents);
            Assert.AreEqual(32, quote.TotalPriceCents);
            Assert.AreEqual(25, quote.BaseServiceFeeCents);
            Assert.AreEqual(7, quote.ChargeableMiles);
            Assert.AreEqual(7, quote.MileageChargeCents);
            Assert.AreEqual(1f, quote.ConditionMultiplier);

            // The old overload prices the same miles identically.
            Assert.IsTrue(FreightServiceQuoteCalculator.TryCalculate(3.5f, 25, 1, true,
                out FreightServiceQuote old, out reason), reason);
            Assert.AreEqual(quote.TotalPriceCents, old.TotalPriceCents);
            Assert.AreEqual(1, old.TripCount);
            Assert.AreEqual(old.TotalPriceCents, old.PerTripTotalCents);
        }

        [Test]
        public void CargoQuote_MultiTrip_300Units_ElevenTrips()
        {
            Assert.IsTrue(TryCargo(3.5f, 300, LumberSpec(), WagonMassKg, WagonVolumeM3, 1f,
                out FreightServiceQuote quote, out string reason), reason);
            Assert.AreEqual(11, quote.TripCount);
            Assert.AreEqual(28, quote.UnitsPerTrip);
            Assert.AreEqual(300, quote.TotalQuantityUnits);
            Assert.AreEqual(4800f, quote.CargoMassKg, 0.001f);
            Assert.AreEqual(12f, quote.CargoVolumeM3, 0.001f);
            Assert.AreEqual(32, quote.PerTripTotalCents);
            Assert.AreEqual(352, quote.TotalPriceCents);
        }

        [Test]
        public void CargoQuote_MultiTrip_600Units_TwentyTwoTrips()
        {
            Assert.IsTrue(TryCargo(3.5f, 600, LumberSpec(), WagonMassKg, WagonVolumeM3, 1f,
                out FreightServiceQuote quote, out string reason), reason);
            Assert.AreEqual(22, quote.TripCount);
            Assert.AreEqual(32, quote.PerTripTotalCents);
            Assert.AreEqual(704, quote.TotalPriceCents);
        }

        [Test]
        public void CargoQuote_ConditionMultiplier_ScalesMileageOnly()
        {
            // Mud 1.6x: 7 chargeable miles billed as ceil(7 x 1.6) = 12.
            // The dispatch fee is unchanged — mustering does not get muddier.
            Assert.IsTrue(TryCargo(3.5f, 20, LumberSpec(), WagonMassKg, WagonVolumeM3, 1.6f,
                out FreightServiceQuote quote, out string reason), reason);
            Assert.AreEqual(1, quote.TripCount);
            Assert.AreEqual(12, quote.ChargeableMiles);
            Assert.AreEqual(12, quote.MileageChargeCents);
            Assert.AreEqual(37, quote.PerTripTotalCents);
            Assert.AreEqual(37, quote.TotalPriceCents);
            Assert.AreEqual(1.6f, quote.ConditionMultiplier, 0.001f);

            // 0 (unset estimate) and 1 both mean unconditioned: 32c.
            Assert.IsTrue(TryCargo(3.5f, 20, LumberSpec(), WagonMassKg, WagonVolumeM3, 0f,
                out FreightServiceQuote unset, out reason), reason);
            Assert.AreEqual(32, unset.TotalPriceCents);
            Assert.IsTrue(TryCargo(3.5f, 20, LumberSpec(), WagonMassKg, WagonVolumeM3, 1f,
                out FreightServiceQuote plain, out reason), reason);
            Assert.AreEqual(32, plain.TotalPriceCents);
        }

        [Test]
        public void CargoQuote_InvalidInputs_Refuse()
        {
            Assert.IsFalse(TryCargo(3.5f, 0, LumberSpec(), WagonMassKg, WagonVolumeM3, 1f,
                out _, out string reason));
            Assert.IsFalse(string.IsNullOrWhiteSpace(reason));
            Assert.IsFalse(TryCargo(3.5f, 20, null, WagonMassKg, WagonVolumeM3, 1f,
                out _, out reason));
            Assert.IsFalse(TryCargo(0f, 20, LumberSpec(), WagonMassKg, WagonVolumeM3, 1f,
                out _, out _));
            Assert.IsFalse(TryCargo(3.5f, 20, LumberSpec(), 0f, 0f, 1f, out _, out reason),
                "a wagon that carries nothing cannot be quoted");
            Assert.IsFalse(TryCargo(3.5f, 20, LumberSpec(), WagonMassKg, WagonVolumeM3, -1f,
                out _, out _));
            Assert.IsFalse(TryCargo(3.5f, 20, LumberSpec(), -5f, WagonVolumeM3, 1f,
                out _, out _));
        }

        [Test]
        public void CargoQuote_WeightlessCargo_OneTrip()
        {
            var weightless = new FreightCargoSpec { MassKgPerUnit = 0f, VolumeM3PerUnit = 0f };
            Assert.IsTrue(TryCargo(3.5f, 600, weightless, WagonMassKg, WagonVolumeM3, 1f,
                out FreightServiceQuote quote, out string reason), reason);
            Assert.AreEqual(1, quote.TripCount);
            Assert.AreEqual(32, quote.TotalPriceCents);
        }

        [Test]
        public void CargoQuote_VolumeBinds()
        {
            var bulky = new FreightCargoSpec { MassKgPerUnit = 1f, VolumeM3PerUnit = 0.5f };
            // Mass allows 450, volume allows 8 — volume binds: 20u is 3 trips.
            Assert.IsTrue(TryCargo(3.5f, 20, bulky, WagonMassKg, WagonVolumeM3, 1f,
                out FreightServiceQuote quote, out string reason), reason);
            Assert.AreEqual(8, quote.UnitsPerTrip);
            Assert.AreEqual(3, quote.TripCount);
            Assert.AreEqual(96, quote.TotalPriceCents);
        }

        [Test]
        public void CargoQuote_TermsDisclosed()
        {
            Assert.IsTrue(TryCargo(3.5f, 300, LumberSpec(), WagonMassKg, WagonVolumeM3, 1f,
                out FreightServiceQuote quote, out string reason), reason);
            Assert.IsFalse(string.IsNullOrWhiteSpace(quote.TermsSummary));
            StringAssert.Contains("11 trips", quote.TermsSummary);
            StringAssert.Contains("300u", quote.TermsSummary);
            StringAssert.Contains("352c", quote.TermsSummary);
        }

        [Test]
        public void AgreementBook_AcceptsMultiTripQuote_RejectsInconsistentTotal()
        {
            var book = new FreightServiceAgreementBook();
            var request = new FreightServiceRequest
            {
                RequestId = "request-def016", RequestingBusinessId = "bakery-1",
                CarrierBusinessId = "freight-1", PickupBusinessId = "millbrook",
                DestinationBusinessId = "bakery-1", CargoLotId = "lot-1", Quantity = 56,
                RequestingPersonId = 4, CarrierOperatorPersonId = 7,
                PickupLocationId = "millbrook-pickup", DestinationLocationId = "bakery-door"
            };
            Assert.IsNull(book.CreateRequest(request, out _));
            var twoTrip = new FreightServiceQuote
            {
                QuoteId = "quote-def016", OneWayDistanceMiles = 3.5f, ChargeableMiles = 7,
                BaseServiceFeeCents = 25, RatePerMileCents = 1, MileageChargeCents = 7,
                TripCount = 2, PerTripTotalCents = 32, TotalPriceCents = 64,
                PayerBusinessId = "bakery-1"
            };
            Assert.IsNull(book.SetQuote(request.RequestId, twoTrip));

            // Same quote with a total that does not reconcile across trips is refused.
            var broken = new FreightServiceQuote
            {
                QuoteId = "quote-def016-broken", OneWayDistanceMiles = 3.5f, ChargeableMiles = 7,
                BaseServiceFeeCents = 25, RatePerMileCents = 1, MileageChargeCents = 7,
                TripCount = 2, PerTripTotalCents = 32, TotalPriceCents = 63,
                PayerBusinessId = "bakery-1"
            };
            Assert.IsNotNull(book.SetQuote(request.RequestId, broken));

            // A legacy quote with TripCount 0 still validates as one trip.
            var legacy = new FreightServiceQuote
            {
                QuoteId = "quote-legacy", OneWayDistanceMiles = 50f, ChargeableMiles = 100,
                BaseServiceFeeCents = 25, RatePerMileCents = 1, MileageChargeCents = 100,
                TotalPriceCents = 125, PayerBusinessId = "bakery-1"
            };
            Assert.AreEqual(0, legacy.TripCount);
            var book2 = new FreightServiceAgreementBook();
            Assert.IsNull(book2.CreateRequest(new FreightServiceRequest
            {
                RequestId = "request-legacy", RequestingBusinessId = "bakery-1",
                CarrierBusinessId = "freight-1", PickupBusinessId = "millbrook",
                DestinationBusinessId = "bakery-1", CargoLotId = "lot-2", Quantity = 1,
                RequestingPersonId = 4, CarrierOperatorPersonId = 7,
                PickupLocationId = "millbrook-pickup", DestinationLocationId = "bakery-door"
            }, out _));
            Assert.IsNull(book2.SetQuote("request-legacy", legacy));
        }

        [Test]
        public void Capacity_MaxUnitsPerTrip_MassBinds()
        {
            var wagon = new EquipmentAsset
            {
                AssetId = "wagon-1", Kind = "wagon",
                PayloadMassCapacityKg = 450f, CargoVolumeCapacityM3 = 4f,
            };
            Assert.AreEqual(28, FreightCargoCapacity.MaxUnitsPerTrip(LumberSpec(), wagon));
            Assert.AreEqual(11, FreightCargoCapacity.TripsNeeded(300, LumberSpec(), wagon));
            Assert.AreEqual(22, FreightCargoCapacity.TripsNeeded(600, LumberSpec(), wagon));
            Assert.AreEqual(1, FreightCargoCapacity.TripsNeeded(28, LumberSpec(), wagon));
        }

        [Test]
        public void Capacity_MaxUnitsPerTrip_VolumeBinds()
        {
            var wagon = new EquipmentAsset
            {
                AssetId = "wagon-1", Kind = "wagon",
                PayloadMassCapacityKg = 450f, CargoVolumeCapacityM3 = 4f,
            };
            var bulky = new FreightCargoSpec { MassKgPerUnit = 1f, VolumeM3PerUnit = 0.5f };
            Assert.AreEqual(8, FreightCargoCapacity.MaxUnitsPerTrip(bulky, wagon));
        }

        [Test]
        public void Capacity_MaxUnitsPerTrip_ZeroCapacity_ReturnsZero()
        {
            var wagon = new EquipmentAsset
            {
                AssetId = "wagon-0", Kind = "wagon",
                PayloadMassCapacityKg = 0f, CargoVolumeCapacityM3 = 0f,
            };
            Assert.AreEqual(0, FreightCargoCapacity.MaxUnitsPerTrip(LumberSpec(), wagon));
            Assert.AreEqual(-1, FreightCargoCapacity.TripsNeeded(10, LumberSpec(), wagon));
        }

        [Test]
        public void Capacity_MaxUnitsPerTrip_Weightless_ReturnsMaxValue()
        {
            var wagon = new EquipmentAsset
            {
                AssetId = "wagon-1", Kind = "wagon",
                PayloadMassCapacityKg = 450f, CargoVolumeCapacityM3 = 4f,
            };
            var weightless = new FreightCargoSpec { MassKgPerUnit = 0f, VolumeM3PerUnit = 0f };
            Assert.AreEqual(int.MaxValue, FreightCargoCapacity.MaxUnitsPerTrip(weightless, wagon));
            Assert.AreEqual(1, FreightCargoCapacity.TripsNeeded(600, weightless, wagon));
        }

        #region Coordinator multi-trip

        private sealed class TripChain
        {
            public List<string> Diag = new List<string>();
            public FreightShipmentService Freight = new FreightShipmentService();
            public TaskAuthority Tasks = new TaskAuthority();
            public WorkTimeBudgetStore Budgets = new WorkTimeBudgetStore();
            public PersonScheduleTracker Tracker = new PersonScheduleTracker();
            public CreditCashBridge Cash = new CreditCashBridge();
            public HouseholdLedger HouseholdLedger;
            public PurseCashStore CarrierPurse;

            public TripChain(int householdCashCents)
            {
                HouseholdLedger = new HouseholdLedger(7);
                Assert.IsNull(HouseholdLedger.RecordInflow(0, householdCashCents,
                    HouseholdIncomeSource.OwnerContribution, "test-seed", "test seed capital", "test"));
                Assert.IsNull(Cash.Register("household:7",
                    new HouseholdCashStore(7, HouseholdLedger)));
                CarrierPurse = new PurseCashStore("business:freight-1", 0, "test seed capital");
                Assert.IsNull(Cash.Register("business:freight-1", CarrierPurse));
            }

            public EquipmentAsset Wagon()
            {
                return new EquipmentAsset
                {
                    AssetId = "opening-freight-wagon", Kind = "wagon",
                    DisplayName = "freight wagon", Condition01 = 1f,
                    OwnerKind = "business", OwnerId = "freight-1",
                    PayloadMassCapacityKg = WagonAuthority.DefaultMassCapacityKg,
                    CargoVolumeCapacityM3 = WagonAuthority.DefaultVolumeCapacityM3,
                };
            }

            public ConstructionMaterialSale Sale(int units)
            {
                var sale = new ConstructionMaterialSale
                {
                    SaleId = "sale-0", SupplierBusinessId = "lumber-yard",
                    ProjectId = "proj-1", PackageId = "pkg-1", DayIndex = 11,
                };
                sale.Lines.Add(new ConstructionMaterialSaleLine
                {
                    DisplayName = "lumber", Units = units, UnitLabel = "board feet",
                    UnitPriceCents = 40, SupplierBusinessId = "lumber-yard",
                    Provenance = "yard lot 'lumber' x" + units + " (sale sale-0, day 11)",
                });
                return sale;
            }

            public ConstructionFreightPlan Plan(int trips, int perTripCents, int totalCents)
            {
                return new ConstructionFreightPlan
                {
                    Freight = Freight,
                    Wagon = Wagon(),
                    DriverId = EntityId.For(EntityKind.Person, 41),
                    DraftAnimalIds = new List<EntityId> { EntityId.For(EntityKind.Animal, 1) },
                    LoaderId = EntityId.For(EntityKind.Person, 42),
                    UnloaderId = EntityId.For(EntityKind.Person, 43),
                    Tasks = Tasks,
                    Budgets = Budgets,
                    Tracker = Tracker,
                    SiteLabel = "proj-1",
                    FreightChargeCents = totalCents,
                    FreightChargeCentsPerTrip = perTripCents,
                    TripCount = trips,
                    CarrierPayeeLabel = "business:freight-1",
                    BuyerOwnerName = "household:7",
                    Cash = Cash,
                    CargoSpec = LumberSpec(),
                    TransitGameSeconds = 3200f,
                };
            }
        }

        [Test]
        public void Coordinator_SplitsAcrossTrips_ExactMoney()
        {
            var chain = new TripChain(100000);
            var coordinator = new ConstructionMaterialFreightCoordinator();
            var stockpile = new ConstructionSiteStockpile { ProjectId = "proj-1" };

            // 56 lumber units at 28/trip is 2 trips of 32c: 64c total.
            string problem = coordinator.FreightSaleToSite(chain.Sale(56),
                chain.Plan(2, 32, 64), stockpile, 11, chain.Diag);
            Assert.IsNull(problem, string.Join("; ", chain.Diag));
            Assert.AreEqual(2, chain.Freight.TrackedShipments.Count);
            Assert.AreEqual("cship-0", chain.Freight.TrackedShipments[0].ShipmentId);
            Assert.AreEqual("cship-1", chain.Freight.TrackedShipments[1].ShipmentId);
            Assert.AreEqual(28, chain.Freight.TrackedShipments[0].PlannedQuantityUnits);
            Assert.AreEqual(28, chain.Freight.TrackedShipments[1].PlannedQuantityUnits);
            Assert.AreEqual(56, stockpile.AvailableUnits("lumber"));
            Assert.AreEqual(64, chain.CarrierPurse.ReadBalanceCents());
            Assert.AreEqual(100000 - 64, chain.HouseholdLedger.GetBalanceCents());
            Assert.AreEqual(64, coordinator.LastFreightPaidCents);
        }

        [Test]
        public void Coordinator_PlanTripsTooFew_RefusesBlockedPreLoad()
        {
            var chain = new TripChain(100000);
            var coordinator = new ConstructionMaterialFreightCoordinator();
            var stockpile = new ConstructionSiteStockpile { ProjectId = "proj-1" };

            // 56 units need 2 trips at 28/trip; the plan quotes 1.
            string problem = coordinator.FreightSaleToSite(chain.Sale(56),
                chain.Plan(1, 32, 32), stockpile, 11, chain.Diag);
            Assert.IsNotNull(problem);
            StringAssert.Contains("blocked pre-load", problem);
            Assert.AreEqual(0, stockpile.AvailableUnits("lumber"));
            Assert.AreEqual(0, chain.CarrierPurse.ReadBalanceCents());
            Assert.AreEqual(100000, chain.HouseholdLedger.GetBalanceCents());
            Assert.AreEqual(0, coordinator.LastFreightPaidCents);
            Assert.AreEqual(0, chain.Freight.TrackedShipments.Count);
        }

        [Test]
        public void Coordinator_ChargeMismatch_Refuses()
        {
            var chain = new TripChain(100000);
            var coordinator = new ConstructionMaterialFreightCoordinator();
            var stockpile = new ConstructionSiteStockpile { ProjectId = "proj-1" };

            // 2 trips x 32c is 64c, not 63c: the plan does not reconcile.
            string problem = coordinator.FreightSaleToSite(chain.Sale(56),
                chain.Plan(2, 32, 63), stockpile, 11, chain.Diag);
            Assert.IsNotNull(problem);
            StringAssert.Contains("does not reconcile", problem);
            Assert.AreEqual(0, stockpile.AvailableUnits("lumber"));
            Assert.AreEqual(0, chain.CarrierPurse.ReadBalanceCents());
            Assert.AreEqual(0, coordinator.LastFreightPaidCents);
        }

        [Test]
        public void Coordinator_PartialTripFailure_KeepsDeliveredGoods()
        {
            // The buyer holds exactly one trip's freight: trip 1 pays and
            // delivers 28u, trip 2's payment refuses for insufficient cash.
            var chain = new TripChain(32);
            var coordinator = new ConstructionMaterialFreightCoordinator();
            var stockpile = new ConstructionSiteStockpile { ProjectId = "proj-1" };

            string problem = coordinator.FreightSaleToSite(chain.Sale(56),
                chain.Plan(2, 32, 64), stockpile, 11, chain.Diag);
            Assert.IsNotNull(problem);
            StringAssert.Contains("trip 2", problem);
            Assert.AreEqual(28, stockpile.AvailableUnits("lumber"),
                "trip 1's goods are already at the site; they are not rolled back.");
            Assert.AreEqual(32, chain.CarrierPurse.ReadBalanceCents());
            Assert.AreEqual(0, chain.HouseholdLedger.GetBalanceCents());
            Assert.AreEqual(32, coordinator.LastFreightPaidCents);
        }

        #endregion
    }
}
