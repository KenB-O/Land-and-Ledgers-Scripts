// Plan 03 Slice 0 (2026-11-07): freight repeatability discriminator + probes.
// Tests only; no production file is touched.
//
// Fixture shape copied exactly from FreightSupplierPickupTests (same ids:
// carrier livery_freight_003, bakery bakery_001, wagon opening-freight-wagon,
// Ethan P7, horse A0, 125c = 25c base + 1c/mi x 100 chargeable miles, 1 sack
// = 10 lb, flour SKU bakery_flour_sack, Millbrook definition rows copied
// verbatim: 10 lb / 18 bakery units / 48c / 230 starting).
//
// Written in fixed-behaviour form (each test asserts the correct end state).
// Expected outcomes at HEAD (planner prediction): Test 1 FAILS (the
// discriminator — the second legitimate haul) or, if it passes, it proves
// the transition and Slice 2 is skipped. Tests 2-8 are controls/pins
// expected to PASS; a failure in any of them names a transition the planner
// believed proven and stops the slice before any repair is written.
//
// SCOPE NOTE — the harness cannot run SimulationDrivers.cs (excluded), so
// the live daily drives are NOT invoked here. What the probes do instead:
//   - Test 3 evaluates the pickup-drive gates (a)-(d) exactly as written in
//     ProcessFreightSupplierPickups against the live objects, proving gate
//     (d) blocks the commit while the physical destination trigger is
//     unconfirmed.
//   - Tests 1/5 replicate the one harness-excluded step the driver-level
//     haul needs — the terminal draft-team sweep
//     (SimulationDrivers.ReleaseTerminalFreightDraftTeams) — by calling the
//     identical WagonAuthority.ReleaseDraftTeam with the identical
//     AnimalAvailabilityAuthority, AFTER asserting its preconditions hold
//     (wagon.CurrentShipmentId and horse.CurrentAssignmentId still name the
//     terminal shipment). The sweep itself is source-review, never claimed
//     as harness-covered. Without that sweep call the rebind refuses
//     ("wagon already assigned"): completion releases the reservation holds
//     (ReleaseShipmentReservations) but the wagon/horse mirrors are only
//     cleared by the sweep. Test 5 pins both halves — the refusal before
//     the sweep and the successful rebind after it.
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
    public sealed class FreightRepeatabilityTests
    {
        private const int EthanId = 7;
        private const int Day = 12;
        private const int Day2 = 13;
        private const int Day3 = 14;
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
        private FakeBusinessCashPort buyerCash;
        private List<string> diag;
        private JourneyModel journeyModel;
        private OffMapJourneyAuthority journeys;
        private int commitCalls;

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
            commitCalls = 0;
            tasks = new TaskAuthority();
            budgets = new WorkTimeBudgetStore();
            budgets.EnsureDay(Day);
            budgets.EnsureDay(Day + 1);
            budgets.EnsureDay(Day + 2);
            budgets.EnsureDay(Day + 3);
            reservations = new EquipmentReservationService();

            var ids = new EntityIdRegistry();
            horse = new AnimalRegistry(ids).RegisterAnimal(AnimalSpecies.Horse, "Draft", AnimalSex.Male,
                AnimalOwnerKind.Business, CarrierId, Day, "Freight repeatability fixture");
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
            buyerCash = new FakeBusinessCashPort(20000);
            Assert.IsNull(supplier.TryPlaceOrder(
                "opening-flour-b1", BakeryId, "Glen Pierce", 1, buyerCash, 0, out lot),
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
                // Counted wrapper: the D+2 fallback path commits through
                // this hook (WorkLoading), while the supplier-pickup drive
                // commits through the adapter directly (SimulateSupplierPickup).
                CommitSourceLot = (LogisticsShipmentState s, int qty, out string reason) =>
                {
                    commitCalls++;
                    return adapter.CommitSourceLot(s, qty, out reason);
                },
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
                AgreementId, LotId, MillbrookFlourFreightCustodyAdapter.FlourSkuId, true);
            Assert.IsNotNull(shipment, shipmentRefusal);
            Assert.IsNull(book.LinkShipment(agreement.AgreementId, ShipmentId));
            Assert.IsNull(book.MarkPreparedForDispatch(agreement.AgreementId, ShipmentId, 50000f));
            freight.SetDispatchPlan(ShipmentId, EntityId.For(EntityKind.Person, EthanId),
                new List<EntityId> { horse.AnimalId });

            journeyModel = new JourneyModel();
            Assert.IsNull(journeyModel.RegisterLocation(new JourneyLocation("town-center", JourneyLocationKind.Waypoint, "Town Centre", 0f, 0f)));
            RegionalTownRegistry.ApplyAll(journeyModel, new List<string>());
            journeys = new OffMapJourneyAuthority(journeyModel);
        }

        private float NowGameSeconds() => Day * 86400f + 8 * 3600f;

        private EntityId Handler() => EntityId.For(EntityKind.Person, EthanId);

        private List<EntityId> TeamAnimals() => new List<EntityId> { horse.AnimalId };

        // The exact bind call HandleCompletedFreightDispatchTask performs
        // (S3): same authority (the shared reservation authority), same
        // float.MaxValue window, same shipment assignment id.
        private string BindTeam() => BindTeamFor(ShipmentId);

        private string BindTeamFor(string shipmentId) => WagonAuthority.TryAssignDraftTeam(wagon,
            new List<AnimalState> { horse },
            LandLedgers.Primitives.EntityId.For(LandLedgers.Primitives.EntityKind.Person, EthanId).ToString(), shipmentId,
            new AnimalAvailabilityAuthority(reservations), NowGameSeconds(), float.MaxValue);

        private bool Authorize() => freight.AuthorizeShipmentDispatch(ShipmentId, out _);

        private void BindAndAuthorize()
        {
            Assert.IsNull(BindTeam(), "fixture bind must succeed");
            Assert.IsTrue(Authorize(), "fixture authorization must succeed");
            Assert.IsFalse(shipment.requiresDispatchAuthorization);
        }

        private WorkTask FindLoadTask() => FindLoadTaskFor(ShipmentId);

        private WorkTask FindLoadTaskFor(string shipmentId) => tasks.FindOpenTask("freight:load:" + shipmentId,
            EntityId.For(EntityKind.Business, 3), EntityId.Invalid);

        private int Tick() => freight.AdvanceTick(86400f, hooks, Handler(), diag);

        // Millbrook-shaped vehicle departure, as in
        // OffMapJourneySaveSeamTests: day 10, leg days 0, due day 12.
        private OffMapJourneyRecord DepartMillbrookWagon(int dayIndex)
        {
            return DepartMillbrookWagon(dayIndex, AgreementId);
        }

        private OffMapJourneyRecord DepartMillbrookWagon(int dayIndex, string agreementId)
        {
            OffMapJourneyRecord record = journeys.DepartVehicle(
                WagonId, "town-center", "Millbrook Grain Co.", 0,
                "Millbrook flour pickup", null,
                new List<string> { agreementId },
                dayIndex, null, null, diag);
            Assert.IsNotNull(record, "Millbrook departure should succeed: " + string.Join("; ", diag));
            return record;
        }

        // Exactly the writes the S11 supplier-pickup drive performs on D+1
        // (FreightShipmentService.WorkLoading's post-commit writes, no task).
        private void ApplyPickupWrites() => ApplyPickupWritesFor(shipment);

        private void ApplyPickupWritesFor(LogisticsShipmentState target)
        {
            target.sourceLotDebitApplied = true;
            target.loadApplied = true;
            target.sourceCommittedAtSchedule = true;
            target.remainingQuantityUnits = target.PlannedQuantityUnits;
            target.physicalLocationId = "wagon:" + WagonId;
        }

        // The D+1 pickup's first half, as the drive performs it: commit the
        // lot through the adapter, then apply the loading writes.
        private void SimulateSupplierPickup() => SimulateSupplierPickupFor(shipment);

        private void SimulateSupplierPickupFor(LogisticsShipmentState target)
        {
            Assert.IsTrue(adapter.CommitSourceLot(target, target.PlannedQuantityUnits, out string reason), reason);
            ApplyPickupWritesFor(target);
        }

        // Drives one already-picked-up shipment to Completed through the
        // return-day tick loop (the wagon is back: IsVehicleOffMap false).
        // Asserts no load task is created at any tick.
        private List<LogisticsShipmentStatus> TickUntilCompleted(LogisticsShipmentState target, string label)
        {
            hooks.IsVehicleOffMap = _ => false;
            var trace = new List<LogisticsShipmentStatus>();
            for (int tick = 0; tick < 8 && target.Status != LogisticsShipmentStatus.Completed; tick++)
            {
                Tick();
                trace.Add(target.Status);
                Assert.IsNull(FindLoadTaskFor(target.ShipmentId),
                    "no load task is created at any tick in the loop for " + label + " (tick " + tick + ")");
            }
            Assert.AreEqual(LogisticsShipmentStatus.Completed, target.Status,
                label + " did not complete; trace: " + string.Join(", ", trace));
            return trace;
        }

        // Haul 1 end to end, exactly as
        // EndToEnd_SupplierPickedUpShipment_CompletesWithFlourAndSingleSettlement
        // does. Asserts completion, receipt, and the fulfilled agreement; the
        // caller owns the cash assertions.
        private void DriveHaul1ToCompletion()
        {
            BindAndAuthorize();
            SimulateSupplierPickup();
            TickUntilCompleted(shipment, "haul 1");
            Assert.IsTrue(lot.Received, "the paid Millbrook lot must be marked received");
            Assert.AreEqual(1, bakery.FlourStock.Lots.Count);
            Assert.AreEqual(10, bakery.FlourStock.Lots[0].Units,
                "the inventory transfer is pounds, not the separate 18 bakery-unit quotation");
            Assert.AreEqual(LotId, bakery.FlourStock.Lots[0].ImportOrderId);
            Assert.IsTrue(shipment.deliveryApplied);
            Assert.AreEqual(FreightServiceAgreementStatus.Fulfilled, agreement.Status,
                "haul 1 agreement must be fulfilled by the tick's MarkFulfilled path");
        }

        // The production terminal sweep, replicated call-for-call
        // (SimulationDrivers.ReleaseTerminalFreightDraftTeams, harness-
        // excluded): it releases ONLY when the wagon's CurrentShipmentId and
        // the horse's CurrentAssignmentId still name the terminal shipment.
        // Those preconditions are asserted first — they are the probe.
        private void ReleaseTerminalDraftTeam(string terminalShipmentId)
        {
            Assert.AreEqual(terminalShipmentId, wagon.CurrentShipmentId,
                "sweep precondition: the wagon must still name the terminal shipment");
            Assert.AreEqual(terminalShipmentId, horse.CurrentAssignmentId,
                "sweep precondition: the horse must still name the terminal shipment");
            Assert.IsNull(WagonAuthority.ReleaseDraftTeam(wagon, new List<AnimalState> { horse },
                new AnimalAvailabilityAuthority(reservations)),
                "the sweep release itself must succeed");
            Assert.IsTrue(string.IsNullOrEmpty(wagon.CurrentShipmentId), "sweep must clear the wagon's shipment name");
            Assert.IsTrue(string.IsNullOrEmpty(horse.CurrentAssignmentId), "sweep must clear the horse's assignment name");
        }

        // Places the second paid lot (day 13) and builds its request, quote,
        // acceptance, shipment, and link — up to, but not including,
        // MarkPreparedForDispatch, so Gate C probes can observe the
        // prepared-pending state. Returns the new lot, request id, agreement,
        // and shipment.
        private void BuildSecondHaul(out OffMapAidCargoLot lot2, out string request2Id,
            out FreightServiceAgreement agreement2, out LogisticsShipmentState shipment2)
        {
            int buyerBefore = buyerCash.BalanceCents;
            Assert.IsNull(MillbrookFlourReorder.PlaceReorder(
                supplier, buyerCash, BakeryId, "Glen Pierce", 1, Day2, diag, out lot2),
                "second reorder must place: " + string.Join("; ", diag));
            Assert.IsNotNull(lot2, "second reorder must mint a lot");
            Assert.AreEqual("millbrook_grain_co:flour-reorder-d13-n2", lot2.OrderId,
                "second reorder id is deterministic (day 13, second lot for the buyer; TryPlaceOrder prefixes the business id)");
            Assert.AreEqual(buyerBefore - lot2.TotalCostCents, buyerCash.BalanceCents,
                "second reorder debits buyer cash exactly once");
            Assert.AreEqual(48, lot2.TotalCostCents, "one sack costs the authored 48c");

            FreightServiceAgreementBook book = freight.ServiceAgreements;
            request2Id = "freight-request:" + lot2.OrderId;
            var request2 = new FreightServiceRequest
            {
                RequestId = request2Id, RequestingBusinessId = BakeryId, CarrierBusinessId = CarrierId,
                PickupBusinessId = "millbrook_grain_co", DestinationBusinessId = BakeryId,
                CargoLotId = lot2.LotId, Quantity = 1, QuantityUnit = "lb", RequestingPersonId = 2,
                CarrierOperatorPersonId = EthanId, PickupLocationId = "millbrook-pickup",
                DestinationLocationId = "bakery-door",
                CargoSkuId = MillbrookFlourFreightCustodyAdapter.FlourSkuId
            };
            Assert.IsNull(book.CreateRequest(request2, out _), "second request must record");
            Assert.IsNull(book.SetQuote(request2Id, new FreightServiceQuote
            {
                QuoteId = "quote-2", OneWayDistanceMiles = 50f, ChargeableMiles = 100,
                BaseServiceFeeCents = 25, RatePerMileCents = 1, MileageChargeCents = 100,
                TotalPriceCents = FreightChargeCents, PayerBusinessId = BakeryId, PaymentTiming = "After successful delivery"
            }), "second quote must record");
            Assert.IsNull(book.Accept(request2Id, EthanId, Day2, Day2 * 86400f + 40000f, out agreement2),
                "second request must accept");
            Assert.IsNotNull(agreement2, "second acceptance must produce an agreement");
            string shipment2Id = "freight-shipment:" + request2Id;
            shipment2 = freight.CreateShipment(CarrierId, shipment2Id, "millbrook_grain_co", BakeryId, 1,
                new FreightCargoSpec { MassKgPerUnit = 4.54f, VolumeM3PerUnit = 0.02f }, WagonId,
                hooks, EntityId.For(EntityKind.Person, EthanId), TeamAnimals(),
                FreightChargeCents, BakeryId, diag, out string shipmentRefusal,
                agreement2.AgreementId, lot2.LotId, MillbrookFlourFreightCustodyAdapter.FlourSkuId, true);
            Assert.IsNotNull(shipment2, "second shipment must create: " + shipmentRefusal);
            Assert.AreEqual(lot2.LotId, shipment2.CargoLotId, "the new lot must bind cleanly to the new shipment");
            Assert.IsNull(book.LinkShipment(agreement2.AgreementId, shipment2Id), "second shipment must link");
        }

        private void PrepareAndDispatchSecondHaul(FreightServiceAgreement agreement2, LogisticsShipmentState shipment2)
        {
            FreightServiceAgreementBook book = freight.ServiceAgreements;
            Assert.IsNull(book.MarkPreparedForDispatch(agreement2.AgreementId, shipment2.ShipmentId, Day2 * 86400f + 50000f),
                "second agreement must prepare for dispatch");
            freight.SetDispatchPlan(shipment2.ShipmentId, EntityId.For(EntityKind.Person, EthanId), TeamAnimals());
            ReleaseTerminalDraftTeam(ShipmentId);
            Assert.IsNull(BindTeamFor(shipment2.ShipmentId), "the draft team must rebind to the second haul");
            Assert.IsTrue(freight.AuthorizeShipmentDispatch(shipment2.ShipmentId, out _),
                "second shipment authorization must succeed");
            Assert.IsFalse(shipment2.requiresDispatchAuthorization);
        }

        [Test]
        public void SecondHaul_AfterFirstCompletes_SettlesOnceAndFreesCapacity()
        {
            int payerBefore = payerRuntime.CurrentCashCents;
            int carrierBefore = carrierRuntime.CurrentCashCents;

            DriveHaul1ToCompletion();
            Assert.AreEqual(payerBefore - FreightChargeCents, payerRuntime.CurrentCashCents,
                "haul 1 freight charge debited from the bakery exactly once");
            Assert.AreEqual(carrierBefore + FreightChargeCents, carrierRuntime.CurrentCashCents,
                "haul 1 freight charge credited to the carrier exactly once");

            BuildSecondHaul(out OffMapAidCargoLot lot2, out _, out FreightServiceAgreement agreement2,
                out LogisticsShipmentState shipment2);
            PrepareAndDispatchSecondHaul(agreement2, shipment2);
            SimulateSupplierPickupFor(shipment2);
            TickUntilCompleted(shipment2, "haul 2");

            Assert.IsTrue(lot2.Received, "the second paid Millbrook lot must be marked received");
            Assert.AreEqual(2, bakery.FlourStock.Lots.Count, "the bakery must hold exactly two flour lots");
            var importIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (BakeryFlourLot flourLot in bakery.FlourStock.Lots)
            {
                Assert.AreEqual(10, flourLot.Units, "each haul credits one 10 lb sack");
                Assert.IsTrue(importIds.Add(flourLot.ImportOrderId), "no bakery lot was minted twice for one Millbrook lot");
            }
            Assert.IsTrue(importIds.Contains(LotId), "haul 1 lot must be among the bakery lots");
            Assert.IsTrue(importIds.Contains(lot2.LotId), "haul 2 lot must be among the bakery lots");
            Assert.AreEqual(payerBefore - 2 * FreightChargeCents, payerRuntime.CurrentCashCents,
                "payer debited a second 125c (total -250c across both hauls)");
            Assert.AreEqual(carrierBefore + 2 * FreightChargeCents, carrierRuntime.CurrentCashCents,
                "carrier credited a second 125c (total +250c across both hauls)");
            Assert.AreEqual(0, freight.FindCompany(CarrierId).FindUncollectedFreightChargeShipmentIds().Count,
                "no uncollected charge remains after both hauls");
            Assert.AreEqual(FreightServiceAgreementStatus.Fulfilled, agreement2.Status,
                "second agreement must be fulfilled");
            Assert.IsTrue(!string.Equals(shipment.CargoLotId, shipment2.CargoLotId, StringComparison.Ordinal),
                "the two shipments must carry different cargo lots");

            // Capacity free afterwards: no live shipment holds the team.
            Assert.IsNull(freight.Service.FindExclusivityConflict(null, WagonId,
                EntityId.For(EntityKind.Person, EthanId), TeamAnimals()),
                "wagon/driver/horse must be free after the second haul completes");
            // Lot 1 stays bound to its Completed shipment: a hypothetical
            // third shipment cannot re-bind it (Completed-lot guard).
            var hypothetical = new LogisticsShipmentState { shipmentId = "freight-shipment:hypothetical-third" };
            Assert.IsTrue(!freight.Service.TryBindSourceLot(hypothetical, LotId,
                MillbrookFlourFreightCustodyAdapter.FlourSkuId, out string rebindReason),
                "lot 1 must refuse re-binding to a third shipment");
            Assert.IsTrue(rebindReason.Contains("already assigned"),
                "the refusal must name the existing assignment, but got '" + rebindReason + "'");
        }

        [Test]
        public void SecondHaul_SameDayDepartureRefused_NextDayDepartureSucceeds()
        {
            // Journey ids are day-granular (offmap-vehicle-<id>-<dayIndex>):
            // after the day-10 departure returns, a second departure for the
            // same vehicle on day 10 is refused with the existing journey id
            // named. This is by design; a same-day refusal is NOT a defect.
            OffMapJourneyRecord record = DepartMillbrookWagon(10);
            journeys.CompleteDueJourneys(12, null, diag);
            Assert.AreEqual(OffMapJourneyState.Returned, record.State, "haul 1 journey must have returned");

            var secondDepartDiag = new List<string>();
            OffMapJourneyRecord second = journeys.DepartVehicle(
                WagonId, "town-center", "Millbrook Grain Co.", 0,
                "Millbrook flour pickup", null,
                new List<string> { AgreementId },
                10, null, null, secondDepartDiag);
            Assert.IsNull(second, "a same-day second departure must be refused");
            Assert.IsTrue(string.Join("; ", secondDepartDiag).Contains(record.JourneyId),
                "the refusal must name the existing journey id, but got: " + string.Join("; ", secondDepartDiag));

            OffMapJourneyRecord nextDay = DepartMillbrookWagon(11, AgreementId);
            Assert.IsNotNull(nextDay, "the next-day departure must succeed");
            Assert.AreEqual("offmap-vehicle-" + WagonId + "-11", nextDay.JourneyId);
        }

        [Test]
        public void PickupGate_DestinationUnconfirmed_CommitsNothing()
        {
            BindAndAuthorize();
            OffMapJourneyRecord record = DepartMillbrookWagon(10);
            // The physical freight route needs its destination trigger.
            record.RequiresPhysicalArrivalConfirmation = true;
            Assert.IsTrue(!record.PhysicalDestinationArrivalConfirmed, "fixture: destination trigger has not fired");

            // Gates (a)-(d) exactly as written in
            // ProcessFreightSupplierPickups: (a) authorized, Planned,
            // unloaded, lot-custody, lot bound — all pass here.
            Assert.IsNotNull(agreement, "agreement must exist");
            Assert.AreEqual(FreightServiceAgreementStatus.Accepted, agreement.Status);
            Assert.IsNotNull(shipment, "shipment must exist");
            Assert.IsTrue(!shipment.requiresDispatchAuthorization, "gate (a): shipment is authorized");
            Assert.AreEqual(LogisticsShipmentStatus.Planned, shipment.Status, "gate (a): shipment is Planned");
            Assert.IsTrue(!shipment.loadApplied, "gate (a): shipment is unloaded");
            Assert.IsTrue(shipment.requiresSourceLotCustody, "gate (a): shipment needs lot custody");
            Assert.IsTrue(!string.IsNullOrWhiteSpace(shipment.CargoLotId), "gate (a): lot is bound");
            // (b) the wagon's active journey.
            Assert.AreSame(record, journeys.FindActiveByVehicle(WagonId), "gate (b): the wagon has an active journey");
            // (c) the agreement is on the journey.
            Assert.IsTrue(record.ObligationIds.Contains(AgreementId), "gate (c): the journey carries this agreement");
            // (d) the physical destination trigger has NOT fired: the pickup
            // drive must stop here and commit nothing.
            bool gateDBlocks = record.RequiresPhysicalArrivalConfirmation
                && !record.PhysicalDestinationArrivalConfirmed;
            Assert.IsTrue(gateDBlocks, "gate (d): pickup waits for the physical destination trigger");

            Assert.AreEqual(0, commitCalls, "the adapter must never be called while gate (d) blocks");
            Assert.IsTrue(!lot.SourceCommitted, "the lot stays uncommitted");
            Assert.AreEqual(LogisticsShipmentStatus.Planned, shipment.Status, "the shipment stays Planned");
            Assert.IsTrue(!shipment.loadApplied, "the shipment stays unloaded");
            Assert.IsTrue(!shipment.sourceLotDebitApplied, "no source debit is applied");
        }

        [Test]
        public void PickupGate_JourneyCompletedBeforePickup_FallbackLoadsInTown()
        {
            BindAndAuthorize();
            OffMapJourneyRecord record = DepartMillbrookWagon(10);
            // The journey returns with no pickup ever committed.
            journeys.CompleteDueJourneys(12, null, diag);
            Assert.AreEqual(OffMapJourneyState.Returned, record.State, "the journey must have returned");
            Assert.IsNull(journeys.FindActiveByVehicle(WagonId),
                "the pickup path has no active journey to act on after the return");
            Assert.IsTrue(!lot.SourceCommitted, "no pickup committed the lot");

            // The D+2 fallback: the Planned, unloaded shipment loads in town
            // via WorkLoading on the return-day tick.
            hooks.IsVehicleOffMap = _ => false;
            Assert.AreEqual(LogisticsShipmentStatus.Planned, shipment.Status);
            Tick();
            Assert.AreEqual(LogisticsShipmentStatus.Loading, shipment.Status,
                "the fallback must start loading in town");
            Assert.IsNotNull(FindLoadTask(), "a load task must be created by the fallback");
            Assert.AreEqual(0, commitCalls, "beginning the load commits nothing yet");
            Tick();
            Assert.AreEqual(1, commitCalls, "the fallback commits the lot through the adapter exactly once");
            Assert.IsTrue(shipment.loadApplied, "the fallback load applies");
            Assert.IsTrue(shipment.sourceLotDebitApplied, "the fallback debit applies");

            int payerBefore = payerRuntime.CurrentCashCents;
            int carrierBefore = carrierRuntime.CurrentCashCents;
            TickUntilCompleted(shipment, "fallback haul");
            Assert.IsTrue(lot.Received, "the fallback haul must still deliver the lot");
            Assert.AreEqual(1, bakery.FlourStock.Lots.Count);
            Assert.AreEqual(payerBefore - FreightChargeCents, payerRuntime.CurrentCashCents,
                "the fallback haul settles the 125c exactly once");
            Assert.AreEqual(carrierBefore + FreightChargeCents, carrierRuntime.CurrentCashCents,
                "the fallback haul credits the carrier exactly once");
            Assert.AreEqual(0, freight.FindCompany(CarrierId).FindUncollectedFreightChargeShipmentIds().Count,
                "no uncollected charge remains");
        }

        [Test]
        public void CapacityRelease_AfterCompletion_TeamRebindableAndReservationsReleased()
        {
            DriveHaul1ToCompletion();

            Assert.IsFalse(shipment.physicalTransportBound, "completion must unbind the shipment's transport");
            Assert.IsNull(freight.Service.FindExclusivityConflict(null, WagonId,
                EntityId.For(EntityKind.Person, EthanId), TeamAnimals()),
                "no live shipment may hold the wagon/driver/horse after completion");
            // ReleaseShipmentReservations is a no-op on re-call.
            freight.Service.ReleaseShipmentReservations(shipment);
            freight.Service.ReleaseShipmentReservations(shipment);
            Assert.IsNull(freight.Service.FindExclusivityConflict(null, WagonId,
                EntityId.For(EntityKind.Person, EthanId), TeamAnimals()),
                "re-releasing reservations must not re-bind anything");

            // The wagon/horse mirrors still name the terminal shipment until
            // the terminal sweep runs, so a rebind refuses first — that is
            // the sweep's precondition, not a defect in the driver.
            string preSweepRefusal = BindTeamFor("freight-shipment:probe");
            Assert.IsNotNull(preSweepRefusal, "the team must still be mirror-bound before the sweep runs");
            Assert.IsTrue(preSweepRefusal.Contains("already assigned"),
                "the pre-sweep refusal must name the assignment, but got '" + preSweepRefusal + "'");
            ReleaseTerminalDraftTeam(ShipmentId);
            Assert.IsNull(BindTeamFor("freight-shipment:probe"),
                "after the sweep the draft team must rebind to a new shipment id");
        }

        [Test]
        public void AgreementBook_SecondAgreementSelectableAfterFirstFulfilled()
        {
            DriveHaul1ToCompletion();
            Assert.AreEqual(FreightServiceAgreementStatus.Fulfilled, agreement.Status);

            BuildSecondHaul(out _, out _, out FreightServiceAgreement agreement2,
                out LogisticsShipmentState shipment2);
            Assert.AreEqual("Pending pickup", agreement2.FulfilmentStatus,
                "haul 2 must sit prepared-pending before preparation");

            FreightServiceAgreement found = freight.ServiceAgreements.FindNextAwaitingPreparation(freight.AllShipments());
            Assert.IsNotNull(found, "haul 2 must be selectable once it is registered and prepared-pending");
            Assert.AreEqual(agreement2.AgreementId, found.AgreementId,
                "the selectable agreement must be haul 2, not the fulfilled haul 1");

            Assert.IsTrue(!string.IsNullOrEmpty(freight.ServiceAgreements.MarkFulfilled(agreement.AgreementId, shipment)),
                "a second fulfilment of haul 1 must be refused");
            Assert.AreEqual(FreightServiceAgreementStatus.Fulfilled, agreement.Status,
                "haul 1 stays Fulfilled after the refused second transition");
        }

        [Test]
        public void MeetingRefusal_WhileWagonBusy_DefersNotRefuses()
        {
            // Haul 1 is live (loaded at the supplier, wagon bound) but not
            // yet complete, so a second lot can be ordered: lot 1 is already
            // committed and no longer covers a new order.
            BindAndAuthorize();
            SimulateSupplierPickup();
            BuildSecondHaulRequestOnly(out OffMapAidCargoLot lot2, out string request2Id);
            string conflict = freight.Service.FindExclusivityConflict(null, WagonId,
                EntityId.For(EntityKind.Person, EthanId), TeamAnimals());
            Assert.IsNotNull(conflict, "the live haul must conflict on the wagon");
            Assert.IsTrue(conflict.Contains(WagonId), "the conflict must name the wagon, but got '" + conflict + "'");

            FreightServiceAgreementBook book = freight.ServiceAgreements;
            Assert.IsNull(book.DeferTemporarily(request2Id, conflict),
                "a capacity conflict must defer the request, not refuse it");
            Assert.AreEqual(FreightServiceRequestStatus.Open, book.FindRequest(request2Id).Status,
                "the deferred request returns to Open with the same request and lot");

            // After haul 1 completes the same request proceeds to
            // meeting/acceptance with no reopen step.
            TickUntilCompleted(shipment, "haul 1");
            Assert.IsTrue(lot.Received, "haul 1 must complete first");
            Assert.IsNull(book.SetQuote(request2Id, new FreightServiceQuote
            {
                QuoteId = "quote-2", OneWayDistanceMiles = 50f, ChargeableMiles = 100,
                BaseServiceFeeCents = 25, RatePerMileCents = 1, MileageChargeCents = 100,
                TotalPriceCents = FreightChargeCents, PayerBusinessId = BakeryId, PaymentTiming = "After successful delivery"
            }), "the deferred request must still accept a quote after haul 1");
            Assert.IsNull(book.Accept(request2Id, EthanId, Day2, Day2 * 86400f + 40000f, out FreightServiceAgreement agreement2),
                "the deferred request must accept after haul 1 without any reopen");
            Assert.IsNotNull(agreement2, "acceptance must produce an agreement");

            // A genuinely Refused request whose lot is still shippable
            // reopens. Lot 2 is now bound to an agreement, so the reopen
            // case uses a third lot: lot 2 must be committed first (drive
            // haul 2 to its pickup), which frees the single-outstanding
            // rule for lot 3.
            string shipment2Id = "freight-shipment:" + request2Id;
            LogisticsShipmentState shipment2 = freight.CreateShipment(CarrierId, shipment2Id, "millbrook_grain_co", BakeryId, 1,
                new FreightCargoSpec { MassKgPerUnit = 4.54f, VolumeM3PerUnit = 0.02f }, WagonId,
                hooks, EntityId.For(EntityKind.Person, EthanId), TeamAnimals(),
                FreightChargeCents, BakeryId, diag, out string shipmentRefusal,
                agreement2.AgreementId, lot2.LotId, MillbrookFlourFreightCustodyAdapter.FlourSkuId, true);
            Assert.IsNotNull(shipment2, "haul 2 shipment must create: " + shipmentRefusal);
            Assert.IsNull(book.LinkShipment(agreement2.AgreementId, shipment2Id));
            ReleaseTerminalDraftTeam(ShipmentId);
            Assert.IsNull(BindTeamFor(shipment2Id), "the team must rebind to haul 2");
            Assert.IsTrue(freight.AuthorizeShipmentDispatch(shipment2Id, out _));
            SimulateSupplierPickupFor(shipment2);
            Assert.IsTrue(lot2.SourceCommitted, "lot 2 must be committed before a third lot can be ordered");

            Assert.IsNull(MillbrookFlourReorder.PlaceReorder(
                supplier, buyerCash, BakeryId, "Glen Pierce", 1, Day3, diag, out OffMapAidCargoLot lot3),
                "third reorder must place once lot 2 is committed: " + string.Join("; ", diag));
            string request3Id = "freight-request:" + lot3.OrderId;
            var request3 = new FreightServiceRequest
            {
                RequestId = request3Id, RequestingBusinessId = BakeryId, CarrierBusinessId = CarrierId,
                PickupBusinessId = "millbrook_grain_co", DestinationBusinessId = BakeryId,
                CargoLotId = lot3.LotId, Quantity = 1, QuantityUnit = "lb", RequestingPersonId = 2,
                CarrierOperatorPersonId = EthanId, PickupLocationId = "millbrook-pickup",
                DestinationLocationId = "bakery-door",
                CargoSkuId = MillbrookFlourFreightCustodyAdapter.FlourSkuId
            };
            Assert.IsNull(book.CreateRequest(request3, out _), "third request must record");
            Assert.IsNull(book.SetStatus(request3Id, FreightServiceRequestStatus.Refused, "test refusal"),
                "the third request must be refusable for the probe");
            // The production reopen gate (DriveMillbrookFreightNegotiation)
            // only reopens when FindShippableLot returns the request's lot:
            // lot 3 is uncommitted and unreceived, so it is the shippable lot.
            OffMapAidCargoLot shippable = MillbrookFlourReorder.FindShippableLot(supplier, BakeryId);
            Assert.IsNotNull(shippable, "a shippable lot must exist for the reopen gate");
            Assert.AreEqual(lot3.LotId, shippable.LotId, "the shippable lot must be lot 3");
            Assert.IsNull(book.ReopenRefusedRequest(request3Id, "Lot '" + lot3.LotId + "' is still shippable; reopening."),
                "a refused request whose lot is still shippable must reopen");
            Assert.AreEqual(FreightServiceRequestStatus.Open, book.FindRequest(request3Id).Status,
                "the reopened request returns to Open");

            // The received-lot half, at the production gate: lot 1 is
            // Received and FindShippableLot never returns it, so the daily
            // drive can never reopen a refused request for lot 1 — the lot
            // branch stays terminal by construction, not by the book method
            // (which does not itself inspect lots).
            Assert.IsTrue(lot.Received, "lot 1 must be received for this half");
            Assert.IsTrue(!string.Equals(lot.LotId, MillbrookFlourReorder.FindShippableLot(supplier, BakeryId).LotId, StringComparison.Ordinal),
                "the shippable lot must never be the received lot 1");
        }

        // Places lot 2 and records its request only (no quote/acceptance):
        // the Gate B probe needs the request to exist while haul 1 is live.
        // Caller must have committed lot 1 first (SimulateSupplierPickup),
        // or the single-outstanding-order rule refuses the reorder.
        private void BuildSecondHaulRequestOnly(out OffMapAidCargoLot lot2, out string request2Id)
        {
            Assert.IsNull(MillbrookFlourReorder.PlaceReorder(
                supplier, buyerCash, BakeryId, "Glen Pierce", 1, Day2, diag, out lot2),
                "second reorder must place: " + string.Join("; ", diag));
            request2Id = "freight-request:" + lot2.OrderId;
            var request2 = new FreightServiceRequest
            {
                RequestId = request2Id, RequestingBusinessId = BakeryId, CarrierBusinessId = CarrierId,
                PickupBusinessId = "millbrook_grain_co", DestinationBusinessId = BakeryId,
                CargoLotId = lot2.LotId, Quantity = 1, QuantityUnit = "lb", RequestingPersonId = 2,
                CarrierOperatorPersonId = EthanId, PickupLocationId = "millbrook-pickup",
                DestinationLocationId = "bakery-door",
                CargoSkuId = MillbrookFlourFreightCustodyAdapter.FlourSkuId
            };
            Assert.IsNull(freight.ServiceAgreements.CreateRequest(request2, out _), "second request must record");
        }

        [Test]
        public void R1Scope_WeeklyPlannerIgnoresMillbrookLots_BakeryDriverCountsThem()
        {
            // (i) Store weekly path: with no StoreProcurementOrder and no
            // StoreDelivery for flour, the outstanding quantity is 0 even
            // though a Millbrook lot is outstanding for the bakery. The
            // weekly planner counts only the store daily chain, by
            // construction — a Millbrook OffMapAidCargoLot is a bakery lot
            // in a different authority.
            var restock = new StoreRestockService();
            Assert.AreEqual(0, StoreWeeklyReorderPlanner.OutstandingOrderedUnits(restock, "flour"),
                "the weekly planner must see 0 for flour: the Millbrook lot is not a store order or delivery");
            Assert.IsTrue(!lot.SourceCommitted && !lot.Received,
                "fixture: lot 1 is outstanding (uncommitted, unreceived) while the planner sees 0");

            // (ii) Bakery daily path: the same outstanding lot blocks a
            // duplicate reorder, and the block lifts once the lot is
            // Received — the bakery side already counts its Millbrook lots.
            Assert.IsTrue(!string.IsNullOrEmpty(MillbrookFlourReorder.PlaceReorder(
                    supplier, buyerCash, BakeryId, "Glen Pierce", 1, Day, diag, out OffMapAidCargoLot duplicate)),
                "a duplicate reorder must be refused while lot 1 is uncommitted");
            Assert.IsNull(duplicate, "no duplicate lot is minted");
            Assert.IsTrue(string.Join("; ", diag).Contains("already covers"),
                "the refusal must name the outstanding lot, but got: " + string.Join("; ", diag));

            DriveHaul1ToCompletion();
            Assert.IsTrue(lot.Received, "lot 1 must be received before the reorder is retried");
            int buyerBefore = buyerCash.BalanceCents;
            Assert.IsNull(MillbrookFlourReorder.PlaceReorder(
                supplier, buyerCash, BakeryId, "Glen Pierce", 1, Day2, diag, out OffMapAidCargoLot lot2),
                "the reorder must succeed after lot 1 is Received: " + string.Join("; ", diag));
            Assert.IsNotNull(lot2, "a second lot must be minted after receipt");
            Assert.AreEqual(buyerBefore - lot2.TotalCostCents, buyerCash.BalanceCents,
                "the second reorder debits buyer cash exactly once");
            // R1 disposition (recorded in the slice log): the store weekly
            // path cannot see Millbrook lots and the bakery daily path
            // already counts them, so R1 is a store-path question needing an
            // owner decision naming the demand path before any fix — no
            // allocation-path production change in this plan.
        }
    }
}
