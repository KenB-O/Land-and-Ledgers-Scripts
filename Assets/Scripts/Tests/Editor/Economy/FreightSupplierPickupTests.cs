// Slice S11 (2026-11-06): supplier pickup on the beyond-gateway day
// (Objective 27) — the paid Millbrook lot moves into wagon custody at the
// supplier on D+1, recorded on the journey record as a purchase manifest,
// and the return-day tick dispatches the already-loaded shipment instead
// of running a second in-town loading task.
//
// SCOPE NOTE — read before citing these tests as coverage of the production
// wiring: SimulationDrivers.cs is harness-EXCLUDED, so the
// ProcessFreightSupplierPickups drive itself, its call site in
// DriveFreightCompanies, and the hooks wiring are source-reviewed only; no
// test in this file invokes them. What IS harness-tested here is every
// seam the drive relies on, through the harness-available pieces:
//   - the adapter's CommitSourceLot idempotency + no-stealing guard
//     (the D+2 re-commit safety and the custody the drive depends on),
//   - the purchase manifest's save round-trip through the S9 seam
//     (CaptureSaveDto / LoadFromSaveDto, deep snapshot),
//   - the timing inputs the drive's day rule reads
//     (DepartureDayIndex + GatewayTravelDays),
//   - the manifest's cancellation rule,
//   - the FreightProductionDriver Planned-case load-skip branch
//     (AdvanceTick dispatches an already-loaded Planned shipment with no
//     load task, and still loads an unloaded one),
//   - the whole return-day sequence after a supplier pickup, at the driver
//     level (the S10-style tick loop).
// The manifest's custody half under cancellation is adapter-side (the lot
// flags live on the supplier runtime, which CancelJourney never touches)
// and is pinned by the commit tests, not by the cancel test.
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
    public sealed class FreightSupplierPickupTests
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
        private JourneyModel journeyModel;
        private OffMapJourneyAuthority journeys;

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
                AnimalOwnerKind.Business, CarrierId, Day, "Freight supplier pickup fixture");
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

            journeyModel = new JourneyModel();
            Assert.IsNull(journeyModel.RegisterLocation(new JourneyLocation("town-center", JourneyLocationKind.Waypoint, "Town Centre", 0f, 0f)));
            RegionalTownRegistry.ApplyAll(journeyModel, new List<string>());
            journeys = new OffMapJourneyAuthority(journeyModel);
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

        // Millbrook-shaped vehicle departure, as in
        // OffMapJourneySaveSeamTests: day 10, leg days 0, due day 12.
        private OffMapJourneyRecord DepartMillbrookWagon(int dayIndex)
        {
            OffMapJourneyRecord record = journeys.DepartVehicle(
                WagonId, "town-center", "Millbrook Grain Co.", 0,
                "Millbrook flour pickup", null,
                new List<string> { AgreementId },
                dayIndex, null, null, diag);
            Assert.IsNotNull(record, "Millbrook departure should succeed: " + string.Join("; ", diag));
            return record;
        }

        // Exactly the writes the S11 supplier-pickup drive performs on D+1
        // (FreightShipmentService.WorkLoading's post-commit writes, no task).
        private void ApplyPickupWrites()
        {
            shipment.sourceLotDebitApplied = true;
            shipment.loadApplied = true;
            shipment.sourceCommittedAtSchedule = true;
            shipment.remainingQuantityUnits = shipment.PlannedQuantityUnits;
            shipment.physicalLocationId = "wagon:" + WagonId;
        }

        // The D+1 pickup's first half, as the drive performs it: commit the
        // lot through the adapter, then apply the loading writes.
        private void SimulateSupplierPickup()
        {
            Assert.IsTrue(adapter.CommitSourceLot(shipment, shipment.PlannedQuantityUnits, out string reason), reason);
            ApplyPickupWrites();
        }

        [Test]
        public void CommitSourceLot_TwiceSameShipment_IsIdempotent()
        {
            int stockBefore = supplier.GetInventoryUnits(MillbrookFlourFreightCustodyAdapter.FlourSkuId);

            Assert.IsTrue(adapter.CommitSourceLot(shipment, 1, out string firstReason), firstReason);
            Assert.IsTrue(lot.SourceCommitted, "first commit must flip the lot to committed");
            Assert.AreEqual(ShipmentId, lot.ShipmentId);
            Assert.AreEqual(stockBefore, supplier.GetInventoryUnits(MillbrookFlourFreightCustodyAdapter.FlourSkuId),
                "committing the reserved lot must not move supplier stock");

            Assert.IsTrue(adapter.CommitSourceLot(shipment, 1, out string secondReason), secondReason);
            Assert.IsTrue(lot.SourceCommitted, "second commit must keep exactly one commitment");
            Assert.AreEqual(ShipmentId, lot.ShipmentId);
            Assert.AreEqual(stockBefore, supplier.GetInventoryUnits(MillbrookFlourFreightCustodyAdapter.FlourSkuId),
                "the second commit must still move no supplier stock");
        }

        [Test]
        public void CommitSourceLot_OtherShipment_Refuses()
        {
            Assert.IsTrue(adapter.CommitSourceLot(shipment, 1, out string firstReason), firstReason);
            Assert.IsTrue(lot.SourceCommitted);

            var otherShipment = new LogisticsShipmentState
            {
                shipmentId = "freight-shipment:request-2",
                sourceBusinessInstanceId = "millbrook_grain_co",
                destinationBusinessInstanceId = BakeryId,
                cargoLotId = LotId,
                cargoSkuId = MillbrookFlourFreightCustodyAdapter.FlourSkuId,
                plannedQuantityUnits = 1,
                requiresSourceLotCustody = true,
            };
            Assert.IsTrue(!adapter.CommitSourceLot(otherShipment, 1, out string reason),
                "the lot must never be stolen by a second shipment");
            Assert.IsTrue(!string.IsNullOrEmpty(reason) && reason.Contains("already committed"),
                "refusal must name the existing commitment, but got '" + reason + "'");
            Assert.AreEqual(ShipmentId, lot.ShipmentId, "the lot must stay committed to the first shipment");
        }

        [Test]
        public void PickupManifest_RoundTripsThroughSaveSeam()
        {
            OffMapJourneyRecord record = DepartMillbrookWagon(10);
            record.PurchaseManifestUnits[MillbrookFlourFreightCustodyAdapter.FlourSkuId] = 1;

            OffMapJourneyAuthoritySaveDto dto = journeys.CaptureSaveDto();
            var reloaded = new OffMapJourneyAuthority(journeyModel);
            reloaded.LoadFromSaveDto(dto);

            OffMapJourneyRecord back = reloaded.FindActiveByVehicle(WagonId);
            Assert.IsNotNull(back, "the journey must survive the save seam");
            Assert.AreEqual(record.JourneyId, back.JourneyId);
            Assert.AreEqual(12, back.DueReturnDayIndex);
            Assert.AreEqual(OffMapJourneyState.Departed, back.State);
            Assert.AreEqual(1, back.PurchaseManifestUnits[MillbrookFlourFreightCustodyAdapter.FlourSkuId],
                "the manifest receipt must survive the save seam");

            // The S9 deep-snapshot guarantee, applied to the manifest:
            // mutating the live record must not dirty the captured DTO.
            record.PurchaseManifestUnits[MillbrookFlourFreightCustodyAdapter.FlourSkuId] = 7;
            record.PurchaseManifestUnits["wheat"] = 3;
            Assert.AreEqual(1, dto.journeys[0].PurchaseManifestUnits[MillbrookFlourFreightCustodyAdapter.FlourSkuId],
                "post-capture mutation must not leak into the saved DTO");
            Assert.IsTrue(!dto.journeys[0].PurchaseManifestUnits.ContainsKey("wheat"),
                "post-capture mutation must not leak into the saved DTO");
        }

        [Test]
        public void PickupDay_IsDeparturePlusGatewayDays()
        {
            OffMapJourneyRecord record = DepartMillbrookWagon(10);
            Assert.AreEqual(1, record.GatewayTravelDays, "Millbrook gateway leg is one day");
            Assert.AreEqual(0, record.OffMapLegDays);
            Assert.AreEqual(12, record.DueReturnDayIndex);
            Assert.AreEqual(11, record.DepartureDayIndex + record.GatewayTravelDays,
                "the pickup day (11) is the only whole day between departure and return, derived from the record, not stored anywhere");
        }

        [Test]
        public void Manifest_CancelledJourney_DropsManifest()
        {
            OffMapJourneyRecord record = DepartMillbrookWagon(10);
            record.PurchaseManifestUnits[MillbrookFlourFreightCustodyAdapter.FlourSkuId] = 1;

            Assert.IsNull(journeys.CancelJourney(record.JourneyId, "test cancel", null, diag));
            Assert.AreEqual(0, record.PurchaseManifestUnits.Count, "a cancel erases the receipt");
            Assert.AreEqual(OffMapJourneyState.Cancelled, record.State);
            // The custody half is adapter-side and is pinned by the commit
            // tests, not here: the lot flags live on the supplier runtime,
            // which CancelJourney never touches.
        }

        [Test]
        public void AdvanceTick_PlannedAlreadyLoaded_DispatchesWithoutLoadTask()
        {
            BindAndAuthorize();
            SimulateSupplierPickup();
            Assert.AreEqual(LogisticsShipmentStatus.Planned, shipment.Status,
                "the pickup drive leaves the shipment Planned until the tick dispatches it");
            Assert.IsFalse(lot.Received, "delivery is unload, not dispatch");

            int diagnosticsBefore = hooks.Diagnostics.Count;
            Tick();

            Assert.AreEqual(LogisticsShipmentStatus.InTransit, shipment.Status,
                "an already-loaded Planned shipment dispatches on the return day instead of loading in town");
            Assert.IsNull(FindLoadTask(), "no load task is created for a supplier-picked-up shipment");
            Assert.IsFalse(lot.Received, "the lot is still unreceived after dispatch");
            Assert.IsTrue(shipment.sourceLotDebitApplied, "no second commit: the debit flag stays as the pickup left it");
            for (int i = diagnosticsBefore; i < hooks.Diagnostics.Count; i++)
                Assert.IsTrue(!hooks.Diagnostics[i].Contains("debited once"),
                    "the adapter must not be called again; unexpected debit log: " + hooks.Diagnostics[i]);
        }

        [Test]
        public void AdvanceTick_PlannedNotLoaded_LoadsAsBefore()
        {
            BindAndAuthorize();
            Assert.IsFalse(shipment.loadApplied, "fixture shipment starts unloaded");

            Tick();

            Assert.AreEqual(LogisticsShipmentStatus.Loading, shipment.Status,
                "the new Planned branch must not break the normal (fallback / legacy) path");
            Assert.IsNotNull(FindLoadTask(), "loading starts as before when the shipment is not already loaded");
        }

        [Test]
        public void EndToEnd_SupplierPickedUpShipment_CompletesWithFlourAndSingleSettlement()
        {
            BindAndAuthorize();
            SimulateSupplierPickup();
            int payerBefore = payerRuntime.CurrentCashCents;
            int carrierBefore = carrierRuntime.CurrentCashCents;

            // The wagon is back (predicate false — the return drive completed
            // the journey at the start of the day in production). The
            // return-day sequence dispatches the already-loaded wagon,
            // transits, unloads the flour, and settles exactly once — with
            // no load task at any tick.
            hooks.IsVehicleOffMap = _ => false;
            var trace = new List<LogisticsShipmentStatus>();
            for (int tick = 0; tick < 8 && shipment.Status != LogisticsShipmentStatus.Completed; tick++)
            {
                Tick();
                trace.Add(shipment.Status);
                Assert.IsNull(FindLoadTask(), "no load task is created at any tick in the loop (tick " + tick + ")");
            }
            Assert.AreEqual(LogisticsShipmentStatus.Completed, shipment.Status,
                "shipment did not complete after the supplier pickup; trace: " + string.Join(", ", trace));

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
    }
}
