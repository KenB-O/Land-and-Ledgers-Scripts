// Slice S4 (2026-11-05): honest transit duration for detailed shipments —
// route-derived from JourneyModel (one-way return leg, Canon 4 mph wagon),
// passed into FreightProductionDriver.CreateShipment through the new seam.
//
// SCOPE NOTE — read before citing these tests as coverage of the production
// wiring: SimulationDrivers.cs is harness-EXCLUDED, so the
// TryRegisterAcceptedFreightShipment route resolution, the CreateShipment
// call with the resolved seconds, and the extended registration log line
// are source-reviewed only; no test in this file invokes them. What IS
// harness-tested here is the exact resolver seam that wiring calls
// (FreightProductionDriver.TryResolveTransitGameSeconds), the CreateShipment
// duration parameter (set / default / floor behaviour), the duration being
// consumed by AdvanceTransit, the same-day end-to-end completion with
// exactly-once settlement, and the save round-trip. The route math itself
// (50.32 mi -> 755 min -> 45,300 s) is pinned with zero production code
// under test.
using System;
using System.Collections.Generic;
using LandLedgers.Animals;
using LandLedgers.Economy;
using LandLedgers.Economy.Blacksmith;
using LandLedgers.Economy.Businesses.Bakery;
using LandLedgers.Economy.Businesses.GeneralStore;
using LandLedgers.Economy.Equipment;
using LandLedgers.Economy.Freight;
using LandLedgers.Economy.Logistics;
using LandLedgers.Economy.Transport;
using LandLedgers.Persistence;
using LandLedgers.Primitives;
using LandLedgers.Tasks;
using LandLedgers.Time;
using LandLedgers.World.Journeys;
using LandLedgers.World.Regional;
using NUnit.Framework;
using UnityEngine;
using EntityId = LandLedgers.Primitives.EntityId;

namespace LandLedgers.EditorTests.Economy
{
    [TestFixture]
    public sealed class FreightTransitDurationTests
    {
        private const int EthanId = 7;
        private const int Day = 12;
        private const string CarrierId = "livery_freight_003";
        private const string BakeryId = "bakery_001";
        private const string WagonId = "opening-freight-wagon";
        private const string ShipmentId = "freight-shipment:request-1";
        private const string AgreementId = "freight-agreement:request-1";
        private const string LotId = "aidlot-millbrook_grain_co:opening-flour-b1";
        private const int FreightChargeCents = 125;
        private const float MillbrookTransitSeconds = 45300f;

        private TaskAuthority tasks;
        private WorkTimeBudgetStore budgets;
        private EquipmentReservationService reservations;
        private AnimalAvailabilityAuthority availability;
        private FreightProductionDriver freight;
        private FreightShipmentHooks hooks;
        private AnimalState horse;
        private EquipmentAsset wagon;
        private LogisticsShipmentState shipment;
        private FreightServiceAgreement agreement;
        private OffMapAidBusinessRuntime supplier;
        private BakeryShopRuntime bakery;
        private MillbrookFlourFreightCustodyAdapter adapter;
        private OffMapAidCargoLot lot;
        private BusinessRuntimeState carrierRuntime;
        private BusinessRuntimeState payerRuntime;
        private List<string> diag;

        private sealed class FakeBusinessCashPort : IBusinessCashPort
        {
            public int BalanceCents { get; private set; }

            public FakeBusinessCashPort(int balanceCents) => BalanceCents = balanceCents;

            public string TryDebit(int amountCents, string reason)
            {
                if (amountCents <= 0 || amountCents > BalanceCents) return "test cash port refused debit";
                BalanceCents -= amountCents;
                return null;
            }

            public string Credit(int amountCents, string reason)
            {
                if (amountCents <= 0) return "test cash port refused credit";
                BalanceCents += amountCents;
                return null;
            }
        }

        private static BusinessRuntimeState Runtime(string id, int cash)
        {
            var runtime = new BusinessRuntimeState();
            runtime.BindBusinessIdentity(id);
            runtime.SetCurrentCashCents(cash);
            return runtime;
        }

        private static OffMapAidBusinessDefinition MillbrookDefinition()
        {
            // Row values copied verbatim from the authored Millbrook asset
            // (10 lb / 18 bakery units / 48c / 230 starting), as in
            // MillbrookFlourFreightCustodyTests.
            var definition = ScriptableObject.CreateInstance<OffMapAidBusinessDefinition>();
            definition.BusinessId = "millbrook_grain_co";
            definition.DisplayName = "Millbrook Grain Co.";
            definition.PickupRouteMilesFromTown = 50f;
            definition.Inventory = new List<OffMapAidInventoryRow>
            {
                new OffMapAidInventoryRow
                {
                    ItemId = "bakery_flour_sack", DisplayName = "Flour",
                    InventoryUnit = "lb", StartingUnits = 230,
                    OrderUnit = "10 lb sack", InventoryUnitsPerOrderUnit = 10,
                    BakeryUnitsPerOrderUnit = 18, OrderPriceCents = 48,
                },
            };
            return definition;
        }

        private static JourneyModel ModelWithHomeCentre()
        {
            var model = new JourneyModel();
            string problem = model.RegisterLocation(new JourneyLocation("town-center", JourneyLocationKind.Waypoint, "Town Centre", 0f, 0f));
            Assert.IsNull(problem);
            return model;
        }

        [SetUp]
        public void SetUp()
        {
            BuildFixture(0f);
        }

        // Mirrors FreightDispatchTeamBindingTests.SetUp exactly (same ids,
        // same paid lot, same agreement), except the shipment is created
        // with the given transit duration: 0 means unset (the 120 s field
        // default), > 0 applies the LRM 60 s floor. Tests that need the
        // honest duration re-run this with 45300f; every object is fresh,
        // so the re-run cannot collide with the SetUp fixture.
        private void BuildFixture(float transitDurationGameSeconds)
        {
            diag = new List<string>();
            tasks = new TaskAuthority();
            budgets = new WorkTimeBudgetStore();
            budgets.EnsureDay(Day);
            budgets.EnsureDay(Day + 1);
            reservations = new EquipmentReservationService();
            availability = new AnimalAvailabilityAuthority(reservations);

            var ids = new EntityIdRegistry();
            horse = new AnimalRegistry(ids).RegisterAnimal(AnimalSpecies.Horse, "Draft", AnimalSex.Male,
                AnimalOwnerKind.Business, CarrierId, Day, "Freight transit duration fixture");
            Assert.IsNotNull(horse);
            Assert.AreEqual("A0", horse.AnimalId.ToString());
            Assert.IsNull(HorseAuthority.Configure(horse, HorseUseCapabilities.GeneralPurpose,
                "ethan-staging", "freight-company-stable", Day));
            // Hitch completion state: attached to the wagon, but the hitch's
            // own Draft reservation (keyed by the hitch task id) has been
            // released, so the horse reads idle for a new binding.
            horse.TransportState = HorseTransportState.AttachedToWagon;
            horse.AttachedWagonAssetId = WagonId;

            wagon = new EquipmentAsset
            {
                AssetId = WagonId, Kind = "freight-wagon", DisplayName = "Opening Freight Wagon",
                Condition01 = 1f, OwnerKind = "business", OwnerId = CarrierId,
                LocationId = "ethan-staging", HitchPreparationShipmentId = ShipmentId,
                HitchingAnimalIds = new List<string> { horse.AnimalId.ToString() }
            };
            Assert.IsNull(WagonAuthority.Configure(wagon, WagonSubtype.GeneralFarmFreight.ToString(),
                450f, 4f, "ethan-staging"));

            supplier = new OffMapAidBusinessRuntime(MillbrookDefinition());
            var bakeryCash = new FakeBusinessCashPort(20000);
            Assert.IsNull(supplier.TryPlaceOrder(
                "opening-flour-b1", BakeryId, "Glen Pierce", 1, bakeryCash, 0, out lot),
                "fixture order must place");
            Assert.AreEqual(LotId, lot.LotId);
            bakery = new BakeryShopRuntime(BakeryId);
            adapter = new MillbrookFlourFreightCustodyAdapter(supplier, bakery, new EntityIdRegistry());

            carrierRuntime = Runtime(CarrierId, 0);
            payerRuntime = Runtime(BakeryId, 20000);
            freight = new FreightProductionDriver();
            freight.EnsureCompany(CarrierId, carrierRuntime);
            freight.RegisterBusinessRuntime(BakeryId, payerRuntime);
            freight.Service.RegisterWagon(wagon);

            hooks = new FreightShipmentHooks
            {
                Tasks = tasks,
                Budgets = budgets,
                DayIndex = Day,
                Diagnostics = new List<string>(),
                Reservations = reservations,
                NowGameSeconds = NowGameSeconds(),
                CommitSourceLot = (LogisticsShipmentState s, int qty, out string reason) =>
                    adapter.CommitSourceLot(s, qty, out reason),
                ReceiveCargoLot = (LogisticsShipmentState s, int qty, out string reason) =>
                    adapter.ReceiveCargoLot(s, qty, Day, out reason),
            };

            FreightServiceAgreementBook book = freight.ServiceAgreements;
            var request = new FreightServiceRequest
            {
                RequestId = "request-1", RequestingBusinessId = BakeryId, CarrierBusinessId = CarrierId,
                PickupBusinessId = "millbrook_grain_co", DestinationBusinessId = BakeryId,
                CargoLotId = LotId, Quantity = 1, QuantityUnit = "lb", RequestingPersonId = 2,
                CarrierOperatorPersonId = EthanId, PickupLocationId = "millbrook-pickup",
                DestinationLocationId = "bakery-door",
                CargoSkuId = MillbrookFlourFreightCustodyAdapter.FlourSkuId
            };
            Assert.IsNull(book.CreateRequest(request, out _));
            Assert.IsNull(book.SetQuote(request.RequestId, new FreightServiceQuote
            {
                QuoteId = "quote-1", OneWayDistanceMiles = 50f, ChargeableMiles = 100,
                BaseServiceFeeCents = 25, RatePerMileCents = 1, MileageChargeCents = 100,
                TotalPriceCents = FreightChargeCents, PayerBusinessId = BakeryId, PaymentTiming = "After successful delivery"
            }));
            Assert.IsNull(book.Accept(request.RequestId, EthanId, Day, 40000f, out agreement));
            shipment = freight.CreateShipment(CarrierId, ShipmentId, "millbrook_grain_co", BakeryId, 1,
                new FreightCargoSpec { MassKgPerUnit = 4.54f, VolumeM3PerUnit = 0.02f }, WagonId,
                hooks, EntityId.For(EntityKind.Person, EthanId), new List<EntityId> { horse.AnimalId },
                FreightChargeCents, BakeryId, diag, out string shipmentRefusal,
                AgreementId, LotId, MillbrookFlourFreightCustodyAdapter.FlourSkuId, true,
                transitDurationGameSeconds: transitDurationGameSeconds);
            Assert.IsNotNull(shipment, shipmentRefusal);
            Assert.IsNull(book.LinkShipment(agreement.AgreementId, ShipmentId));
            Assert.IsNull(book.MarkPreparedForDispatch(agreement.AgreementId, ShipmentId, 50000f));
            freight.SetDispatchPlan(ShipmentId, EntityId.For(EntityKind.Person, EthanId),
                new List<EntityId> { horse.AnimalId });
        }

        private float NowGameSeconds() => Day * 86400f + 8 * 3600f;

        // The exact bind call HandleCompletedFreightDispatchTask performs
        // (S3): same authority (the shared reservation authority), same
        // float.MaxValue window, same shipment assignment id.
        private string BindTeam() => WagonAuthority.TryAssignDraftTeam(wagon,
            new List<AnimalState> { horse }, EthanId.ToString(), ShipmentId,
            new AnimalAvailabilityAuthority(reservations), NowGameSeconds(), float.MaxValue);

        private bool Authorize() => freight.AuthorizeShipmentDispatch(ShipmentId, out _);

        private void BindAndAuthorize()
        {
            Assert.IsNull(BindTeam(), "fixture bind must succeed");
            Assert.IsTrue(Authorize(), "fixture authorization must succeed");
            Assert.IsFalse(shipment.requiresDispatchAuthorization);
        }

        private string DriveDiagnostics(List<LogisticsShipmentStatus> trace)
        {
            WorkTask loadTask = tasks.FindOpenTask("freight:load:" + ShipmentId,
                EntityId.For(EntityKind.Business, 3), EntityId.Invalid);
            return "trace: " + string.Join(", ", trace)
                + "; loadApplied: " + shipment.loadApplied
                + "; sourceLotDebitApplied: " + shipment.sourceLotDebitApplied
                + "; load task: " + (loadTask == null ? "none"
                    : loadTask.Status + ", minutesRemaining=" + loadTask.MinutesRemaining)
                + "; hook diagnostics: " + string.Join(" | ", hooks.Diagnostics)
                + "; driver diagnostics: " + string.Join(" | ", diag);
        }

        private List<LogisticsShipmentStatus> DriveToCompleted()
        {
            var trace = new List<LogisticsShipmentStatus>();
            EntityId handler = EntityId.For(EntityKind.Person, EthanId);
            for (int tick = 0; tick < 8 && shipment.Status != LogisticsShipmentStatus.Completed; tick++)
            {
                freight.AdvanceTick(86400f, hooks, handler, diag);
                trace.Add(shipment.Status);
            }
            return trace;
        }

        private static void AssertSubsequenceInOrder(List<LogisticsShipmentStatus> trace,
            params LogisticsShipmentStatus[] expected)
        {
            int at = 0;
            foreach (LogisticsShipmentStatus status in trace)
            {
                if (at < expected.Length && status == expected[at]) at++;
            }
            Assert.AreEqual(expected.Length, at,
                "expected states in order: " + string.Join(" -> ", expected)
                + "; observed trace: " + string.Join(", ", trace));
        }

        [Test]
        public void RouteDerivedDuration_MillbrookHaul_Is755Minutes()
        {
            JourneyModel model = ModelWithHomeCentre();
            Assert.AreEqual(20, RegionalTownRegistry.ApplyAll(model, new List<string>()));
            JourneyRoute route = model.FindRoute("ashford-millbrook_grain_co", "town-center", TravelMode.Wagon);
            Assert.IsTrue(route.Found, route.Diagnostic);
            // 49.5 (centre->gateway) + 0.5 (gateway->centre) + 0.316 (centre->site).
            Assert.AreEqual(50.32f, route.TotalMiles, 0.01f);
            // 50.316 mi at Canon 4 mph = 755 min.
            Assert.AreEqual(755, route.TotalMinutes);
            Assert.AreEqual(MillbrookTransitSeconds, route.TotalMinutes * 60f);
        }

        [Test]
        public void ResolveTransitGameSeconds_UsesRouteMinutes()
        {
            JourneyModel model = ModelWithHomeCentre();
            RegionalTownRegistry.ApplyAll(model, new List<string>());
            Assert.IsTrue(FreightProductionDriver.TryResolveTransitGameSeconds(
                model, "ashford-millbrook_grain_co", "town-center", out float seconds, out string reason), reason);
            Assert.AreEqual(MillbrookTransitSeconds, seconds);
            Assert.AreEqual(string.Empty, reason);
        }

        [Test]
        public void ResolveTransitGameSeconds_UnknownRoute_Refuses()
        {
            // Empty model: no Ashford, no route — refuse, never invent a duration.
            var emptyModel = new JourneyModel();
            Assert.IsTrue(!FreightProductionDriver.TryResolveTransitGameSeconds(
                emptyModel, "ashford-millbrook_grain_co", "town-center", out float seconds, out string reason));
            Assert.AreEqual(0f, seconds);
            Assert.IsTrue(!string.IsNullOrWhiteSpace(reason), "refusal must name the missing route");
            Assert.IsTrue(reason.Contains("ashford-millbrook_grain_co"), reason);

            // Missing inputs refuse with a reason naming the missing input.
            Assert.IsTrue(!FreightProductionDriver.TryResolveTransitGameSeconds(
                null, "ashford-millbrook_grain_co", "town-center", out _, out string nullReason));
            Assert.IsTrue(!string.IsNullOrWhiteSpace(nullReason), "null-model refusal must name the missing input");
            Assert.IsTrue(!FreightProductionDriver.TryResolveTransitGameSeconds(
                emptyModel, "", "town-center", out _, out string emptyReason));
            Assert.IsTrue(!string.IsNullOrWhiteSpace(emptyReason), "empty-origin refusal must name the missing input");
        }

        [Test]
        public void CreateShipment_WithRouteDerivedDuration_SetsTransitDuration()
        {
            BuildFixture(MillbrookTransitSeconds);
            Assert.AreEqual(MillbrookTransitSeconds, shipment.transitDurationGameSeconds);
            // S4 sets the duration only: no grid route plan is fabricated for
            // a JourneyModel route (Note-3).
            Assert.AreEqual(0f, shipment.RoutePlan.TotalTravelGameSeconds);
        }

        [Test]
        public void CreateShipment_WithoutDuration_Keeps120sDefault()
        {
            // The SetUp fixture calls CreateShipment without the new argument
            // (BuildFixture(0f) = unset): legacy callers keep the 120 s field
            // default, so Series2Carrier / ConstructionMaterialFreight are
            // behaviour-identical under S4.
            Assert.AreEqual(120f, shipment.transitDurationGameSeconds);
        }

        [Test]
        public void AdvanceTransit_HonestDuration_HoldsThenArrives()
        {
            BuildFixture(MillbrookTransitSeconds);
            BindAndAuthorize();
            EntityId handler = EntityId.For(EntityKind.Person, EthanId);

            // Drive in 1-hour ticks until InTransit (loading takes 5 min of
            // work, so the first tick dispatches; the tick that dispatches
            // does not also advance transit).
            int guard = 0;
            while (shipment.Status != LogisticsShipmentStatus.InTransit && guard < 10)
            {
                freight.AdvanceTick(3600f, hooks, handler, diag);
                guard++;
            }
            Assert.AreEqual(LogisticsShipmentStatus.InTransit, shipment.Status,
                "shipment did not reach InTransit; diagnostics: " + string.Join(" | ", diag));
            Assert.AreEqual(0f, shipment.stageElapsedGameSeconds);

            // 20,000 of 45,300 s: still in transit, ~44% of the way.
            freight.Service.AdvanceTransit(shipment, 20000f, hooks);
            Assert.AreEqual(LogisticsShipmentStatus.InTransit, shipment.Status,
                "a 120 s default would have arrived on the first 20,000 s step");
            Assert.AreEqual(20000f / MillbrookTransitSeconds, shipment.physicalProgress01, 0.01f);

            // The remaining 25,300 s completes the leg exactly.
            freight.Service.AdvanceTransit(shipment, 25300f, hooks);
            Assert.AreEqual(LogisticsShipmentStatus.Arrived, shipment.Status);
            Assert.AreEqual(1f, shipment.physicalProgress01);
            Assert.AreEqual(BakeryId, shipment.physicalLocationId);
        }

        [Test]
        public void EndToEnd_HonestDuration_StillCompletesSameDay()
        {
            BuildFixture(MillbrookTransitSeconds);
            BindAndAuthorize();
            int payerBefore = payerRuntime.CurrentCashCents;
            int carrierBefore = carrierRuntime.CurrentCashCents;

            List<LogisticsShipmentStatus> trace = DriveToCompleted();
            Assert.AreEqual(LogisticsShipmentStatus.Completed, shipment.Status,
                "shipment did not complete; " + DriveDiagnostics(trace));
            AssertSubsequenceInOrder(trace,
                LogisticsShipmentStatus.Loading,
                LogisticsShipmentStatus.InTransit,
                LogisticsShipmentStatus.Arrived,
                LogisticsShipmentStatus.Unloading,
                LogisticsShipmentStatus.Completed);

            // The honest duration (45,300 s) is still inside one daily
            // 86,400 s tick: S4 corrects the duration, not the day count.
            Assert.AreEqual(MillbrookTransitSeconds, shipment.transitDurationGameSeconds);
            Assert.IsTrue(lot.Received, "the paid Millbrook lot must be marked received");
            Assert.AreEqual(1, bakery.FlourStock.Lots.Count);
            Assert.AreEqual(10, bakery.FlourStock.Lots[0].Units,
                "the inventory transfer is pounds, not the separate 18 bakery-unit quotation");
            Assert.AreEqual(LotId, bakery.FlourStock.Lots[0].ImportOrderId);
            Assert.IsTrue(shipment.deliveryApplied);
            Assert.AreEqual(payerBefore - FreightChargeCents, payerRuntime.CurrentCashCents,
                "freight charge debited from the bakery exactly once");
            Assert.AreEqual(carrierBefore + FreightChargeCents, carrierRuntime.CurrentCashCents,
                "freight charge credited to the carrier exactly once");
            Assert.AreEqual(0, freight.FindCompany(CarrierId).FindUncollectedFreightChargeShipmentIds().Count,
                "no uncollected charge remains");

            freight.AdvanceTick(86400f, hooks, EntityId.For(EntityKind.Person, EthanId), diag);
            Assert.AreEqual(payerBefore - FreightChargeCents, payerRuntime.CurrentCashCents,
                "no second debit on a later tick");
            Assert.AreEqual(carrierBefore + FreightChargeCents, carrierRuntime.CurrentCashCents,
                "no second credit on a later tick");
        }

        [Test]
        public void SaveRoundTrip_PreservesRouteDerivedDuration()
        {
            BuildFixture(MillbrookTransitSeconds);
            BindAndAuthorize();

            FreightShipmentService.FreightServiceSaveDto dto = freight.CaptureSaveDto();
            var restoredDriver = new FreightProductionDriver();
            List<LogisticsShipmentState> restoredShipments =
                restoredDriver.RestoreSaveDto(dto, hooks, diag);
            LogisticsShipmentState restoredShipment = restoredShipments.Find(s => s != null
                && string.Equals(s.ShipmentId, ShipmentId, StringComparison.Ordinal));
            Assert.IsNotNull(restoredShipment, "restored driver must carry the shipment");
            Assert.AreEqual(MillbrookTransitSeconds, restoredShipment.transitDurationGameSeconds,
                "the route-derived duration must survive save/load");
            Assert.IsFalse(restoredShipment.requiresDispatchAuthorization,
                "authorization must survive save/load, not re-hold the shipment");
            Assert.AreEqual(LogisticsShipmentStatus.Planned, restoredShipment.Status);
            Assert.AreEqual(LotId, restoredShipment.CargoLotId);
        }
    }
}
