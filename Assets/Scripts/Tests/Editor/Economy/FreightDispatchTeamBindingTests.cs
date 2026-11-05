// Slice S3 (2026-11-05): bind the draft team at dispatch completion and
// release it at terminal state.
//
// SCOPE NOTE — read before citing these tests as coverage of the production
// wiring: SimulationDrivers.cs is harness-EXCLUDED, so the
// HandleCompletedFreightDispatchTask bind step and the DriveFreightCompanies
// terminal-release sweep are source-reviewed only; no test in this file
// invokes them. What IS harness-tested here is the exact bind+authorize
// behaviour those two edits perform, through the harness-available seams:
// WagonAuthority.TryAssignDraftTeam + FreightProductionDriver
// .AuthorizeShipmentDispatch (the task text names FreightShipmentService for
// the second call; the method actually lives on FreightProductionDriver,
// FreightProductionDriver.cs:243), the end-to-end AdvanceTick drive, the
// WagonAuthority.ReleaseDraftTeam behaviour the sweep relies on, and the
// save round-trips that pin Verify-1 (whether the unified-reservation hold
// behind the bind survives save/load). The fixture mirrors the production
// opening order (agreement accepted, shipment Planned+held, hitch completed
// so the horse is attached but its hitch reservation is released, dispatch
// task created) with NO manual pre-binding — unlike
// FreightDispatchActivationIntegrationTests, whose SetUp pre-binds the team.
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
using NUnit.Framework;
using UnityEngine;
using EntityId = LandLedgers.Primitives.EntityId;

namespace LandLedgers.EditorTests.Economy
{
    [TestFixture]
    public sealed class FreightDispatchTeamBindingTests
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

        [SetUp]
        public void SetUp()
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
                AnimalOwnerKind.Business, CarrierId, Day, "Freight dispatch binding fixture");
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

        // The exact bind call HandleCompletedFreightDispatchTask performs
        // (S3): same authority (the shared reservation authority), same
        // float.MaxValue window, same shipment assignment id.
        private string BindTeam() => WagonAuthority.TryAssignDraftTeam(wagon,
            new List<AnimalState> { horse }, EthanId.ToString(), ShipmentId,
            new AnimalAvailabilityAuthority(reservations), NowGameSeconds(), float.MaxValue);

        // The exact already-bound predicate the handler checks before
        // calling TryAssignDraftTeam (a second bind would refuse: the wagon
        // reads IsTransportAssigned once bound).
        private bool IsAlreadyBoundToShipment() => horse.CurrentUse == HorseUseKind.Draft
            && string.Equals(horse.CurrentAssignmentId, ShipmentId, StringComparison.Ordinal)
            && string.Equals(wagon.CurrentShipmentId, ShipmentId, StringComparison.Ordinal)
            && string.Equals(wagon.CurrentDriverPersonId, EthanId.ToString(), StringComparison.Ordinal);

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
        public void BindThenAuthorize_AuthorizesHeldShipment_WithoutManualSetup()
        {
            Assert.IsTrue(shipment.requiresDispatchAuthorization);
            Assert.AreEqual(LogisticsShipmentStatus.Planned, shipment.Status);
            Assert.AreEqual(HorseUseKind.None, horse.CurrentUse);
            Assert.IsTrue(!IsAlreadyBoundToShipment());

            Assert.IsNull(BindTeam());
            Assert.IsTrue(IsAlreadyBoundToShipment());
            Assert.AreEqual(HorseUseKind.Draft, horse.CurrentUse);
            Assert.AreEqual(ShipmentId, horse.CurrentAssignmentId);
            Assert.AreEqual(ShipmentId, wagon.CurrentShipmentId);
            Assert.AreEqual(EthanId.ToString(), wagon.CurrentDriverPersonId);
            Assert.IsTrue(wagon.DraftAnimalIds.Contains(horse.AnimalId.ToString()));
            Assert.AreEqual(1, reservations.ReservationsForTask(ShipmentId).Count,
                "one unified-reservation record holds the horse for this shipment");

            Assert.IsTrue(Authorize());
            Assert.IsFalse(shipment.requiresDispatchAuthorization);
            Assert.AreEqual(LogisticsShipmentStatus.Planned, shipment.Status,
                "dispatch authorization must not imply travel, pickup, or loading");
            Assert.IsFalse(shipment.loadApplied);
            Assert.AreEqual(LotId, shipment.CargoLotId);
        }

        [Test]
        public void ReconcileTwice_IsIdempotent_NoSecondHold_NoSecondAuthorization()
        {
            BindAndAuthorize();

            // A reconcile re-run after authorization must change nothing: the
            // handler returns early once the hold is cleared, and the
            // already-bound predicate (not a second bind) is what keeps the
            // team held exactly once.
            Assert.IsTrue(Authorize(), "authorization is idempotent after success");
            Assert.IsTrue(IsAlreadyBoundToShipment());
            Assert.AreEqual(1, reservations.ReservationsForTask(ShipmentId).Count,
                "no second reservation record for the horse");
            Assert.IsTrue(!string.IsNullOrEmpty(BindTeam()),
                "a second bind attempt must refuse — the handler must take the already-bound branch, never re-bind");
            Assert.AreEqual(1, reservations.ReservationsForTask(ShipmentId).Count,
                "the refused second bind must not add a hold");
            Assert.IsFalse(shipment.requiresDispatchAuthorization);
        }

        [Test]
        public void BindRefused_StaysHeld_ThenAuthorizesAfterHorseFrees()
        {
            Assert.IsNull(HorseAuthority.TryReserveUse(horse, HorseUseKind.Draft, "other-assignment",
                availability, NowGameSeconds(), NowGameSeconds() + 86400f, out _),
                "fixture pre-assignment must succeed");
            Assert.AreEqual("other-assignment", horse.CurrentAssignmentId);

            string refusal = BindTeam();
            Assert.IsTrue(!string.IsNullOrEmpty(refusal), "bind must refuse while the horse is held elsewhere");
            Assert.IsTrue(refusal.Contains("already assigned"), refusal);

            // The shipment stays held: authorization is never attempted on a
            // refused bind, so nothing is authorized and nothing is bound.
            Assert.IsTrue(shipment.requiresDispatchAuthorization);
            Assert.AreEqual(LogisticsShipmentStatus.Planned, shipment.Status);
            Assert.AreEqual(string.Empty, wagon.CurrentShipmentId);
            Assert.AreEqual(0, wagon.DraftAnimalIds.Count);

            // Retry path: once the horse frees, the same bind authorizes.
            HorseAuthority.ReleaseUse(horse, availability);
            Assert.AreEqual(HorseUseKind.None, horse.CurrentUse);
            BindAndAuthorize();
            Assert.IsTrue(IsAlreadyBoundToShipment());
        }

        [Test]
        public void AuthorizedShipment_AdvancesToCompleted_WithFlourInBakery()
        {
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
        public void TerminalShipment_ReleasesDraftTeam()
        {
            BindAndAuthorize();
            List<LogisticsShipmentStatus> trace = DriveToCompleted();
            Assert.AreEqual(LogisticsShipmentStatus.Completed, shipment.Status,
                DriveDiagnostics(trace));
            Assert.IsTrue(IsAlreadyBoundToShipment(), "team must still be bound at completion");
            Assert.AreEqual(0, reservations.ReservationsForTask(ShipmentId).Count,
                "completion already released the unified-reservation records (bind + dispatch share one owner, "
                + "so ReleaseShipmentReservations frees both); only the mirror fields still name the shipment");

            // The exact release call the DriveFreightCompanies terminal sweep
            // makes (the sweep's iteration/guards are source-reviewed only).
            // It clears the mirror fields; the reservation release inside it
            // is a no-op for the already-released hold.
            Assert.IsNull(WagonAuthority.ReleaseDraftTeam(wagon,
                new List<AnimalState> { horse }, availability));
            Assert.AreEqual(HorseUseKind.None, horse.CurrentUse);
            Assert.AreEqual(string.Empty, horse.CurrentAssignmentId);
            Assert.AreEqual(string.Empty, wagon.CurrentShipmentId);
            Assert.AreEqual(string.Empty, wagon.CurrentDriverPersonId);
            Assert.AreEqual(0, wagon.DraftAnimalIds.Count);
            Assert.AreEqual(0, reservations.ReservationsForTask(ShipmentId).Count,
                "the bind's unified-reservation record must be gone");

            Assert.IsNull(WagonAuthority.ReleaseDraftTeam(wagon,
                new List<AnimalState> { horse }, availability),
                "release is idempotent");
            Assert.AreEqual(HorseUseKind.None, horse.CurrentUse);
        }

        [Test]
        public void SaveRoundTrip_PreservesBindingAndAuthorization()
        {
            BindAndAuthorize();

            // Freight driver + agreement seams: the restored shipment must
            // still be authorized (not re-held) and the restored agreement
            // still accepted and linked to it.
            FreightShipmentService.FreightServiceSaveDto dto = freight.CaptureSaveDto();
            var restoredDriver = new FreightProductionDriver();
            List<LogisticsShipmentState> restoredShipments =
                restoredDriver.RestoreSaveDto(dto, hooks, diag);
            LogisticsShipmentState restoredShipment = restoredShipments.Find(s => s != null
                && string.Equals(s.ShipmentId, ShipmentId, StringComparison.Ordinal));
            Assert.IsNotNull(restoredShipment, "restored driver must carry the shipment");
            Assert.IsFalse(restoredShipment.requiresDispatchAuthorization,
                "authorization must survive save/load, not re-hold the shipment");
            Assert.AreEqual(LogisticsShipmentStatus.Planned, restoredShipment.Status);
            Assert.AreEqual(LotId, restoredShipment.CargoLotId);
            FreightServiceAgreement restoredAgreement =
                restoredDriver.ServiceAgreements.FindAgreement(AgreementId);
            Assert.IsNotNull(restoredAgreement, "restored driver must carry the agreement");
            Assert.AreEqual(FreightServiceAgreementStatus.Accepted, restoredAgreement.Status);
            Assert.AreEqual(ShipmentId, restoredAgreement.ShipmentId);

            // Verify-1: the unified-reservation hold behind the bind must
            // survive save/load as a live hold, not just as mirror fields.
            // If this fails, a post-load horse reads Draft while the
            // authority holds nothing — report it, do not assume it.
            EquipmentReservationAuthoritySaveDto reservationDto = reservations.CaptureSaveDto();
            var restoredReservations = new EquipmentReservationService();
            restoredReservations.LoadFromSaveDto(reservationDto);
            Assert.IsTrue(restoredReservations.IsReserved(
                ReservationKeyConventions.Animal(horse.AnimalId.ToString()), NowGameSeconds()),
                "restored authority must still hold the horse at the bind time");
            Assert.AreEqual(1, restoredReservations.ReservationsForTask(ShipmentId).Count,
                "restored authority must hold exactly one record for this shipment");
            var rival = new ReservationRequest("rival-task", "Rival hold");
            rival.Items.Add(new ReservationItem(
                ReservationKeyConventions.Animal(horse.AnimalId.ToString()),
                1f, NowGameSeconds(), NowGameSeconds() + 3600f));
            Assert.IsTrue(!string.IsNullOrEmpty(restoredReservations.TryReserveAll(rival, out _)),
                "a restored hold must still refuse double-booking");

            // Horse/wagon mirror fields: these are the plain serialized
            // fields the production save seams persist (horse as a whole
            // object in PopulationSaveDto.animals; wagon binding fields in
            // the Pass 9 equipment DTO). Pinned here through a Unity
            // serialization round-trip of the same objects.
            AnimalState restoredHorse =
                JsonUtility.FromJson<AnimalState>(JsonUtility.ToJson(horse));
            Assert.AreEqual(HorseUseKind.Draft, restoredHorse.CurrentUse);
            Assert.AreEqual(ShipmentId, restoredHorse.CurrentAssignmentId);
            Assert.AreEqual(WagonId, restoredHorse.AttachedWagonAssetId);
            EquipmentAsset restoredWagon =
                JsonUtility.FromJson<EquipmentAsset>(JsonUtility.ToJson(wagon));
            Assert.AreEqual(ShipmentId, restoredWagon.CurrentShipmentId);
            Assert.AreEqual(EthanId.ToString(), restoredWagon.CurrentDriverPersonId);
            Assert.IsTrue(restoredWagon.DraftAnimalIds.Contains(horse.AnimalId.ToString()));
        }
    }
}
