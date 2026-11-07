// Slice S10 (2026-11-06): the off-map hold — AdvanceTick skips a shipment
// whose wagon is off-map, and the hold lifts when the journey returns.
//
// SCOPE NOTE — read before citing these tests as coverage of the production
// wiring: SimulationDrivers.cs is harness-EXCLUDED, so the
// TryDepartMillbrookShipment helper, its handler call site in
// HandleCompletedFreightDispatchTask, its daily retry scan in
// DriveFreightCompanies, and the hooks wiring (IsVehicleOffMap =
// OffMapJourneys.IsOffMapVehicle) are source-reviewed only; no test in this
// file invokes them. What IS harness-tested here is the exact hold behaviour
// those edits rely on, through the harness-available seams:
// FreightShipmentHooks.IsVehicleOffMap + FreightProductionDriver.AdvanceTick
// (the skip beside the authorization skip), driven across days with the
// predicate true (away) then false (returned) to pin the day-level story —
// depart D, absent D and D+1, flour on D+2 (plan 09 §1.5). The fixture
// mirrors FreightDispatchTeamBindingTests (authorized Planned shipment, real
// adapter, paid lot). DepartureQuery_AgreesWithDepartRecord is already
// specified in S9 §4.3 and implemented in OffMapJourneyAuthorityTests —
// listed in the plan so S10's verification runs it; it is not duplicated
// here.
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
using LandLedgers.World;
using LandLedgers.World.Journeys;
using LandLedgers.World.Regional;
using NUnit.Framework;
using UnityEngine;
using EntityId = LandLedgers.Primitives.EntityId;

namespace LandLedgers.EditorTests.Economy
{
    [TestFixture]
    public sealed class FreightOffMapDepartureTests
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

        private TaskAuthority tasks;
        private WorkTimeBudgetStore budgets;
        private EquipmentReservationService reservations;
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

        [SetUp]
        public void SetUp()
        {
            diag = new List<string>();
            tasks = new TaskAuthority();
            budgets = new WorkTimeBudgetStore();
            budgets.EnsureDay(Day);
            budgets.EnsureDay(Day + 1);
            budgets.EnsureDay(Day + 2);
            reservations = new EquipmentReservationService();

            var ids = new EntityIdRegistry();
            horse = new AnimalRegistry(ids).RegisterAnimal(AnimalSpecies.Horse, "Draft", AnimalSex.Male,
                AnimalOwnerKind.Business, CarrierId, Day, "Freight off-map departure fixture");
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
                // Production sets the request SKU from the paid lot itself
                // (SimulationDrivers: CargoSkuId = lot.SkuId); the lot's SKU
                // is the adapter's flour SKU, not the integration fixture's
                // test-only "millbrook_flour_sack" id.
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
                AgreementId, LotId, MillbrookFlourFreightCustodyAdapter.FlourSkuId, true);
            Assert.IsNotNull(shipment, shipmentRefusal);
            Assert.IsNull(book.LinkShipment(agreement.AgreementId, ShipmentId));
            Assert.IsNull(book.MarkPreparedForDispatch(agreement.AgreementId, ShipmentId, 50000f));
            freight.SetDispatchPlan(ShipmentId, EntityId.For(EntityKind.Person, EthanId),
                new List<EntityId> { horse.AnimalId });
        }

        private float NowGameSeconds() => Day * 86400f + 8 * 3600f;

        private EntityId Handler() => EntityId.For(EntityKind.Person, EthanId);

        // The exact bind call HandleCompletedFreightDispatchTask performs
        // (S3): same authority (the shared reservation authority), same
        // float.MaxValue window, same shipment assignment id.
        private string BindTeam() => WagonAuthority.TryAssignDraftTeam(wagon,
            new List<AnimalState> { horse },
            LandLedgers.Primitives.EntityId.For(LandLedgers.Primitives.EntityKind.Person, EthanId).ToString(), ShipmentId,
            new AnimalAvailabilityAuthority(reservations), NowGameSeconds(), float.MaxValue);

        private bool Authorize() => freight.AuthorizeShipmentDispatch(ShipmentId, out _);

        private void BindAndAuthorize()
        {
            Assert.IsNull(BindTeam(), "fixture bind must succeed");
            Assert.IsTrue(Authorize(), "fixture authorization must succeed");
            Assert.IsFalse(shipment.requiresDispatchAuthorization);
        }

        private WorkTask FindLoadTask() => tasks.FindOpenTask("freight:load:" + ShipmentId,
            EntityId.For(EntityKind.Business, 3), EntityId.Invalid);

        private int Tick() => freight.AdvanceTick(86400f, hooks, Handler(), diag);

        [Test]
        public void AdvanceTick_HoldsShipmentWhileWagonOffMap()
        {
            BindAndAuthorize();
            hooks.IsVehicleOffMap = _ => true;

            int advanced = Tick();

            Assert.AreEqual(0, advanced, "a held shipment advances nothing");
            Assert.AreEqual(LogisticsShipmentStatus.Planned, shipment.Status);
            Assert.IsFalse(shipment.loadApplied, "no load while the wagon is off-map");
            Assert.IsFalse(shipment.sourceLotDebitApplied, "no lot debit while the wagon is off-map");
            Assert.IsFalse(lot.Received, "the paid lot must not be marked received while held");
            Assert.IsNull(FindLoadTask(), "no load task is created while the wagon is off-map");
        }

        [Test]
        public void AdvanceTick_NoPredicate_AdvancesAsBefore()
        {
            BindAndAuthorize();
            Assert.IsNull(hooks.IsVehicleOffMap, "fixture hooks carry no predicate — the pre-S10 behaviour");

            Tick();

            Assert.AreEqual(LogisticsShipmentStatus.Loading, shipment.Status,
                "without a predicate the authorized shipment loads exactly as before S10");
            Assert.IsNotNull(FindLoadTask(), "loading starts without a predicate");
        }

        [Test]
        public void AdvanceTick_HoldLiftsOnReturn()
        {
            BindAndAuthorize();

            // Day D: wagon away — held.
            hooks.IsVehicleOffMap = _ => true;
            Assert.AreEqual(0, Tick());
            Assert.AreEqual(LogisticsShipmentStatus.Planned, shipment.Status);
            Assert.IsFalse(shipment.loadApplied);
            Assert.IsNull(FindLoadTask());

            // Day D+2: wagon returned — the hold is derived from the
            // predicate, not latched on the shipment, so it lifts.
            hooks.IsVehicleOffMap = _ => false;
            Tick();
            Assert.AreEqual(LogisticsShipmentStatus.Loading, shipment.Status,
                "the hold must lift when the predicate answers false");
            Assert.IsNotNull(FindLoadTask(), "loading starts on the return day");
        }

        [Test]
        public void EndToEnd_DepartedShipment_FlourArrivesOnDueDay()
        {
            BindAndAuthorize();
            int payerBefore = payerRuntime.CurrentCashCents;
            int carrierBefore = carrierRuntime.CurrentCashCents;

            // Days D and D+1: the wagon is off-map. The shipment waits —
            // still Planned, nothing loaded, no lot debit, no charge.
            hooks.IsVehicleOffMap = _ => true;
            for (int awayDay = 0; awayDay < 2; awayDay++)
            {
                Assert.AreEqual(0, Tick(), "held tick advances nothing");
                Assert.AreEqual(LogisticsShipmentStatus.Planned, shipment.Status,
                    "shipment must stay Planned while the wagon is away (day " + awayDay + ")");
                Assert.IsFalse(shipment.loadApplied);
                Assert.IsFalse(shipment.sourceLotDebitApplied);
                Assert.IsFalse(lot.Received);
                Assert.IsNull(FindLoadTask());
                Assert.AreEqual(payerBefore, payerRuntime.CurrentCashCents, "no charge while held");
                Assert.AreEqual(carrierBefore, carrierRuntime.CurrentCashCents, "no credit while held");
            }

            // Day D+2: the wagon is back (predicate false — the return drive
            // completed the journey at the start of the day in production).
            // The same day's tick loads the lot, transits, unloads the flour
            // into the bakery bin, and settles the charge exactly once.
            hooks.IsVehicleOffMap = _ => false;
            var trace = new List<LogisticsShipmentStatus>();
            for (int tick = 0; tick < 8 && shipment.Status != LogisticsShipmentStatus.Completed; tick++)
            {
                Tick();
                trace.Add(shipment.Status);
            }
            Assert.AreEqual(LogisticsShipmentStatus.Completed, shipment.Status,
                "shipment did not complete on the return day; trace: " + string.Join(", ", trace));

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

            Tick();
            Assert.AreEqual(payerBefore - FreightChargeCents, payerRuntime.CurrentCashCents,
                "no second debit on a later tick");
            Assert.AreEqual(carrierBefore + FreightChargeCents, carrierRuntime.CurrentCashCents,
                "no second credit on a later tick");
        }

        // ---------- Slice F5 (2026-11-06, Obj 25): visible departure/return gates ----------
        //
        // SCOPE NOTE (harness adaptation, F2/F3 precedent): the controller
        // (CoupledTransportMovementController) and the Begin methods on
        // OpeningTransportPresentationController are harness-excluded
        // MonoBehaviours, and the SimulationDrivers call sites
        // (TryDepartMillbrookShipment, ProcessFreightJourneyReturns) are
        // source-reviewed only. What IS harness-tested here are the exact
        // gating predicates those call sites and Begin methods read:
        // the outbound leg starts only when IsOffMapVehicle is true for a
        // live vehicle journey record — never on dispatch-task start or
        // authorization alone (no record exists then, so the gate is
        // closed) — and the return leg starts only once the journey
        // record is Returned.

        private static (JourneyModel model, OffMapJourneyAuthority authority, List<string> diag) NewJourneyAuthority()
        {
            var model = new JourneyModel();
            model.RegisterLocation(new JourneyLocation("farmstead-f1", JourneyLocationKind.Farmstead, "Home Farm", 0f, 6f));
            model.RegisterLocation(new JourneyLocation("town-depot", JourneyLocationKind.OffMapGateway, "Rail Depot", 0f, 0f));
            model.RegisterLocation(new JourneyLocation("mill", JourneyLocationKind.Mill, "Grist Mill", 2f, 3f));
            model.AddEdge("farmstead-f1", "mill", 4f, "river road");
            model.AddEdge("mill", "town-depot", 3f, "depot road");
            return (model, new OffMapJourneyAuthority(model), new List<string>());
        }

        [Test]
        public void Departure_DoesNotStartMovementWithoutJourneyRecord()
        {
            (_, OffMapJourneyAuthority authority, List<string> diag) = NewJourneyAuthority();

            Assert.IsFalse(authority.IsOffMapVehicle(WagonId),
                "with no journey record, the outbound gate is closed — a dispatch start or authorization alone must not start movement");

            // Ethan departing on foot does not move the wagon.
            OffMapJourneyRecord personRecord = authority.DepartPerson(
                EthanId, "farmstead-f1", "Millbrook", 1, 2, "drive the flour wagon",
                new Dictionary<string, int>(), new List<string>(), Day,
                _ => 0, (_, _) => null, diag);
            Assert.IsNotNull(personRecord, "person departure must succeed: " + string.Join("; ", diag));
            Assert.IsTrue(authority.IsOffMapPerson(EthanId));
            Assert.IsFalse(authority.IsOffMapVehicle(WagonId),
                "a person journey must not open the wagon's outbound gate");

            // Only the vehicle journey record opens it — the S10 departure.
            OffMapJourneyRecord wagonRecord = authority.DepartVehicle(
                WagonId, "farmstead-f1", "Millbrook", 1, "Millbrook flour pickup",
                new Dictionary<string, int>(), new List<string>(), Day,
                _ => 0, (_, _) => null, diag);
            Assert.IsNotNull(wagonRecord, "vehicle departure must succeed: " + string.Join("; ", diag));
            Assert.IsTrue(authority.IsOffMapVehicle(WagonId),
                "with a live vehicle journey record, the outbound leg may start");
        }

        [Test]
        public void Return_StartsReturnLegOnlyForReturnedJourney()
        {
            (_, OffMapJourneyAuthority authority, List<string> diag) = NewJourneyAuthority();

            OffMapJourneyRecord wagonRecord = authority.DepartVehicle(
                WagonId, "farmstead-f1", "Millbrook", 1, "Millbrook flour pickup",
                new Dictionary<string, int>(), new List<string>(), Day,
                _ => 0, (_, _) => null, diag);
            Assert.IsNotNull(wagonRecord, "vehicle departure must succeed: " + string.Join("; ", diag));
            int dueDay = wagonRecord.DueReturnDayIndex;
            Assert.IsTrue(dueDay > Day, "a vehicle journey is a round trip with a future due day");

            // Before the due day the journey is still Departed: no return leg.
            List<OffMapJourneyRecord> early = authority.CompleteDueJourneys(dueDay - 1, null, diag);
            Assert.AreEqual(0, early.Count, "nothing completes before the due day");
            Assert.AreEqual(OffMapJourneyState.Departed, wagonRecord.State,
                "while Departed, the return-leg gate is closed");
            Assert.IsTrue(authority.IsOffMapVehicle(WagonId));

            // On the due day the journey returns: only now may the return leg start.
            List<OffMapJourneyRecord> done = authority.CompleteDueJourneys(dueDay, null, diag);
            Assert.AreEqual(1, done.Count, string.Join("; ", diag));
            Assert.AreEqual(OffMapJourneyState.Returned, wagonRecord.State,
                "only a Returned journey opens the return leg");
            Assert.IsFalse(authority.IsOffMapVehicle(WagonId),
                "after the return, the wagon is home — the return leg has run, and no second leg starts");
        }

        [Test]
        public void Return_BoardingRefusedWhenRiderFarFromExit()
        {
            // Slice R3 (2026-11-06, Objective 22): the §8 load case at
            // the seam BeginCoupledReturn reads. A Departed journey
            // restored from its DTO still gates the return leg (it
            // completes on its due day and only then is Returned), and
            // the pure board gate refuses a rider whose position is not
            // within tolerance of the leg-start cell — the fresh scene
            // after a mid-trip load has Ethan standing at staging/home,
            // not at the exit, so the return leg must proceed riderless
            // rather than teleporting or duplicating him.
            //
            // SCOPE NOTE: the director/controller parts (the board call
            // in BeginCoupledReturn, the riderless-leg log) are
            // source-reviewed only — SimulationDrivers, the director,
            // and the presentation controller are harness-excluded.
            // What IS harness-tested here is the exact pair of
            // predicates the return leg reads: the restored journey
            // gating (OffMapJourneyAuthority) and the board refusal
            // (WagonRiderPlacement.TryAllowBoarding).
            (JourneyModel model, OffMapJourneyAuthority authority, List<string> diag) = NewJourneyAuthority();

            OffMapJourneyRecord wagonRecord = authority.DepartVehicle(
                WagonId, "farmstead-f1", "Millbrook", 1, "Millbrook flour pickup",
                new Dictionary<string, int>(), new List<string>(), Day,
                _ => 0, (_, _) => null, diag);
            Assert.IsNotNull(wagonRecord, "vehicle departure must succeed: " + string.Join("; ", diag));
            Assert.AreEqual(OffMapJourneyState.Departed, wagonRecord.State);
            int dueDay = wagonRecord.DueReturnDayIndex;

            // Save mid-trip (journey Departed) and load into a fresh
            // authority: the record survives the DTO round-trip.
            OffMapJourneyAuthoritySaveDto dto = authority.CaptureSaveDto();
            var reloaded = new OffMapJourneyAuthority(model);
            reloaded.LoadFromSaveDto(dto);
            OffMapJourneyRecord restored = reloaded.FindActiveByVehicle(WagonId);
            Assert.IsNotNull(restored, "the Departed journey must survive the save/load round-trip");
            Assert.AreEqual(wagonRecord.JourneyId, restored.JourneyId);
            Assert.AreEqual(OffMapJourneyState.Departed, restored.State,
                "a mid-trip load restores the journey as Departed — the return leg is still closed");
            Assert.IsTrue(reloaded.IsOffMapVehicle(WagonId));

            // On the due day the restored journey returns: only now
            // does the return leg open.
            List<OffMapJourneyRecord> done = reloaded.CompleteDueJourneys(dueDay, null, diag);
            Assert.AreEqual(1, done.Count, string.Join("; ", diag));
            Assert.AreEqual(OffMapJourneyState.Returned, restored.State,
                "only a Returned journey opens the return leg, after a DTO restore exactly as without one");

            // The leg-start cell is the exit cell; the rider in the
            // fresh scene stands at staging, far from it.
            Vector3 exitSeat = WagonRiderPlacement.CellCenter(new GridCoord(8, 2), 2f, Vector3.zero);
            Vector3 riderAtStaging = new Vector3(5f, 0f, 5f);
            Assert.IsTrue(Vector2.Distance(
                    new Vector2(exitSeat.x, exitSeat.z), new Vector2(riderAtStaging.x, riderAtStaging.z))
                > WagonRiderPlacement.BoardToleranceMetres,
                "the fixture must actually place the rider beyond the boarding tolerance");
            Assert.IsTrue(!WagonRiderPlacement.TryAllowBoarding(
                isRouteActive: false, isHidden: false,
                seatWorldPosition: exitSeat,
                riderWorldPosition: riderAtStaging,
                boardToleranceMetres: WagonRiderPlacement.BoardToleranceMetres,
                out string refusalReason));
            Assert.IsTrue(!string.IsNullOrWhiteSpace(refusalReason),
                "the riderless return must name its refusal reason");

            // And the same gate allows a rider who IS at the exit —
            // the refusal is about position, not about the load.
            Assert.IsTrue(WagonRiderPlacement.TryAllowBoarding(
                isRouteActive: false, isHidden: false,
                seatWorldPosition: exitSeat,
                riderWorldPosition: exitSeat,
                boardToleranceMetres: WagonRiderPlacement.BoardToleranceMetres,
                out _));
        }
    }
}
