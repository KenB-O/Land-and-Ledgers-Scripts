// Slice S13 (2026-11-06): delivery and payment audit (Objective 29,
// verification only) — the manifest reconciles with the delivered lot,
// the fallback delivers with no manifest, the legacy collection overload's
// hazard is fenced, duplicate receipts stay idempotent, a refused delivery
// holds cargo and collects nothing, a mid-unload save/load still delivers,
// and the repeat-haul bounds hold. NO production change in this slice.
//
// SCOPE NOTE — read before citing these tests as coverage of the production
// wiring: SimulationDrivers.cs is harness-EXCLUDED, so the daily drive order
// (returns → pickups → departures → AdvanceTick → terminal sweep), the
// pickup drive itself, and the hooks wiring are source-reviewed only; no
// test in this file invokes them. What IS harness-tested here is every seam
// the drive relies on, through the harness-available pieces:
//   - the driver-level tick loop after a supplier pickup (the S10/S11
//     pattern: bind + authorize, pickup writes, return simulated through
//     CompleteDueJourneys, hold lifted via IsVehicleOffMap),
//   - the adapter's delivery-time checks and duplicate-receipt idempotency,
//   - the company runtimes' legacy vs verified collection overloads,
//   - the service-level save DTO round-trip for an open unload task,
//   - the journey authority's day-granular id rule and the supplier
//     runtime's duplicate-order refusal.
// The plan's intro says "seven tests" but its §4.3 table lists 8; all 8
// tabled tests are implemented here.
//
// RULES PINNED HERE (production must never call the legacy overloads):
//   - CollectFreightCharge(shipmentId, diagnostics) sets the collected flag
//     and returns the charge while debiting NOBODY; the flag it sets also
//     blocks the verified retry, so a legacy collection is unrecoverable.
//   - The non-resolver CollectAndPostUncollectedRevenue posts carrier
//     revenue with no debit. Production collection uses ONLY the resolver
//     overload (FreightProductionDriver.cs:448).
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
    public sealed class FreightDeliveryPaymentAuditTests
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
        private FakeBusinessCashPort buyerCash;
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
                AnimalOwnerKind.Business, CarrierId, Day, "Freight delivery payment audit fixture");
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

        // Drives the fixture to Completed, mirroring the S11 end-to-end
        // test. Returns the trace of statuses seen, for failure messages.
        private List<LogisticsShipmentStatus> TickToCompleted()
        {
            var trace = new List<LogisticsShipmentStatus>();
            for (int tick = 0; tick < 8 && shipment.Status != LogisticsShipmentStatus.Completed; tick++)
            {
                Tick();
                trace.Add(shipment.Status);
            }
            return trace;
        }

        // The normal path up to the return-day tick loop: journey departed
        // day 10 with the manifest written as the pickup drive does, lot
        // committed at the supplier on D+1, team bound and authorized, the
        // journey returned on its due day (production passes a null cargo
        // callback — Note-12: the manifest is a receipt, never a delivery
        // mechanism), and the off-map hold lifted.
        private OffMapJourneyRecord DriveNormalPathToReturnDay()
        {
            OffMapJourneyRecord record = DepartMillbrookWagon(10);
            record.PurchaseManifestUnits[MillbrookFlourFreightCustodyAdapter.FlourSkuId] = 1;
            SimulateSupplierPickup();
            BindAndAuthorize();

            List<OffMapJourneyRecord> returned = journeys.CompleteDueJourneys(12, null, diag);
            Assert.AreEqual(1, returned.Count, "the journey must complete exactly once on its due day");
            Assert.AreEqual(OffMapJourneyState.Returned, record.State);
            Assert.AreEqual(12, record.ActualReturnDayIndex);
            Assert.AreEqual(12, record.DueReturnDayIndex);
            Assert.AreEqual(1, record.PurchaseManifestUnits[MillbrookFlourFreightCustodyAdapter.FlourSkuId],
                "the manifest receipt must survive the return");

            hooks.IsVehicleOffMap = _ => false;
            return record;
        }

        // A fresh company holding one Completed, delivered, uncollected
        // shipment (125c, external payer), built the way FreightCompanyTests
        // builds its collection fixtures — used only by the legacy-fence
        // tests, one fresh company + shipment per test so the flag set by a
        // legacy call cannot leak into the verified path.
        private FreightCompanyRuntime BuildLegacyFenceCompany(string shipmentId,
            out LogisticsShipmentState fenceShipment)
        {
            var company = new FreightCompanyRuntime(CarrierId);
            fenceShipment = new LogisticsShipmentState
            {
                shipmentId = shipmentId,
                plannedQuantityUnits = 1,
                remainingQuantityUnits = 0,
                deliveredQuantityUnits = 1,
                freightChargeCents = FreightChargeCents,
                freightPayerBusinessInstanceId = BakeryId,
                cargoClass = LogisticsCargoClass.PackagedGoods,
                state = LogisticsShipmentStatus.Completed,
                deliveryApplied = true,
            };
            Assert.IsTrue(company.TryAcceptShipment(fenceShipment, diag),
                "fence shipment must be accepted: " + string.Join("; ", diag));
            return company;
        }

        [Test]
        public void Audit_NormalPath_ManifestMatchesDeliveredLot()
        {
            OffMapJourneyRecord record = DriveNormalPathToReturnDay();
            int payerBefore = payerRuntime.CurrentCashCents;
            int carrierBefore = carrierRuntime.CurrentCashCents;

            List<LogisticsShipmentStatus> trace = TickToCompleted();
            Assert.AreEqual(LogisticsShipmentStatus.Completed, shipment.Status,
                "shipment did not complete on the normal path; trace: " + string.Join(", ", trace));

            // The receipt and the delivery agree on SKU, units, and lot
            // identity (P1 / Note-19).
            Assert.AreEqual(OffMapJourneyState.Returned, record.State, "journey must stay Returned after delivery");
            Assert.AreEqual(12, record.ActualReturnDayIndex);
            Assert.AreEqual(1, record.PurchaseManifestUnits[MillbrookFlourFreightCustodyAdapter.FlourSkuId],
                "the manifest must still read one flour sack after delivery");
            Assert.AreEqual(1, bakery.FlourStock.Lots.Count, "exactly one bakery lot must be minted");
            BakeryFlourLot delivered = bakery.FlourStock.Lots[0];
            Assert.AreEqual(LotId, delivered.ImportOrderId, "delivered lot must carry the paid lot id");
            Assert.AreEqual(10, delivered.Units, "delivered lot must hold the lot's 10 lb");
            Assert.AreEqual(lot.SupplierBusinessId, delivered.OriginName, "delivered lot must name the supplier");
            Assert.AreEqual(Day, delivered.AcquiredDayIndex, "delivered lot must be acquired on the delivery day");
            Assert.IsTrue(lot.Received, "the paid Millbrook lot must be marked received");

            // And the charge settles exactly once, on the same completion.
            Assert.IsTrue(shipment.deliveryApplied);
            Assert.IsTrue(shipment.FreightRevenueCollected);
            Assert.AreEqual(payerBefore - FreightChargeCents, payerRuntime.CurrentCashCents,
                "freight charge debited from the bakery exactly once");
            Assert.AreEqual(carrierBefore + FreightChargeCents, carrierRuntime.CurrentCashCents,
                "freight charge credited to the carrier exactly once");
            Assert.AreEqual(0, freight.FindCompany(CarrierId).FindUncollectedFreightChargeShipmentIds().Count,
                "no uncollected charge remains");
        }

        [Test]
        public void Audit_FallbackPath_NoManifestStillDeliversAndSettlesOnce()
        {
            // No journey record and no pickup writes: the in-town load path.
            Assert.IsNull(journeys.FindActiveByVehicle(WagonId),
                "the fallback fixture must have no journey record at all");
            BindAndAuthorize();
            int payerBefore = payerRuntime.CurrentCashCents;
            int carrierBefore = carrierRuntime.CurrentCashCents;

            List<LogisticsShipmentStatus> trace = TickToCompleted();
            Assert.AreEqual(LogisticsShipmentStatus.Completed, shipment.Status,
                "shipment did not complete on the fallback path; trace: " + string.Join(", ", trace));

            // Delivery requires the lot commit, never the manifest (Note-12).
            Assert.IsTrue(lot.Received, "the paid Millbrook lot must be marked received");
            Assert.AreEqual(1, bakery.FlourStock.Lots.Count, "exactly one bakery lot must be minted");
            Assert.AreEqual(10, bakery.FlourStock.Lots[0].Units);
            Assert.AreEqual(LotId, bakery.FlourStock.Lots[0].ImportOrderId);
            Assert.IsTrue(shipment.deliveryApplied);
            Assert.IsTrue(shipment.FreightRevenueCollected);
            Assert.AreEqual(payerBefore - FreightChargeCents, payerRuntime.CurrentCashCents,
                "freight charge debited from the bakery exactly once");
            Assert.AreEqual(carrierBefore + FreightChargeCents, carrierRuntime.CurrentCashCents,
                "freight charge credited to the carrier exactly once");
            Assert.AreEqual(0, freight.FindCompany(CarrierId).FindUncollectedFreightChargeShipmentIds().Count,
                "no uncollected charge remains");
            Assert.IsNull(journeys.FindActiveByVehicle(WagonId),
                "no manifest was ever required: no journey record exists anywhere in the fixture");
        }

        [Test]
        public void LegacyCollect_SetsFlagWithoutDebiting_PayerCashUnchanged()
        {
            // RULE: production must NEVER call this overload. It sets the
            // collected flag and returns the charge while debiting nobody;
            // the flag it sets also blocks the verified retry, so a legacy
            // collection is unrecoverable (Note-17, P2).
            FreightCompanyRuntime company = BuildLegacyFenceCompany("audit-legacy-1", out _);
            BusinessRuntimeState payer = Runtime(BakeryId, 600);
            BusinessRuntimeState carrier = Runtime(CarrierId, 0);

            int returned = company.CollectFreightCharge("audit-legacy-1", diag);
            Assert.AreEqual(FreightChargeCents, returned, "the legacy overload returns the charge");
            Assert.AreEqual(600, payer.CurrentCashCents, "the legacy overload debits NOBODY — payer cash unchanged");
            Assert.AreEqual(0, carrier.CurrentCashCents, "no carrier credit posts from the legacy call itself");
            Assert.AreEqual(0, company.FindUncollectedFreightChargeShipmentIds().Count,
                "the legacy call sets the collected flag");

            int verified = company.CollectFreightCharge("audit-legacy-1", payer, diag);
            Assert.AreEqual(0, verified,
                "the verified retry now returns 0 — the legacy flag blocks it, so the 125c is unrecoverable");
            Assert.AreEqual(600, payer.CurrentCashCents, "the blocked retry debits nothing either");
        }

        [Test]
        public void LegacyCollectAndPost_PostsRevenueWithoutDebiting()
        {
            // RULE: production uses ONLY the resolver overload
            // (FreightProductionDriver.cs:448). The non-resolver overload
            // manufactures carrier revenue with no debit, which is why it
            // has no production callers (Note-17, P2).
            FreightCompanyRuntime company = BuildLegacyFenceCompany("audit-legacy-2", out _);
            BusinessRuntimeState payer = Runtime(BakeryId, 600);
            BusinessRuntimeState carrier = Runtime(CarrierId, 0);

            int posted = company.CollectAndPostUncollectedRevenue(carrier, diag);
            Assert.AreEqual(FreightChargeCents, posted, "the non-resolver overload posts the charge");
            Assert.AreEqual(FreightChargeCents, carrier.CurrentCashCents,
                "carrier cash rises by the charge with no debit behind it");
            Assert.AreEqual(600, payer.CurrentCashCents, "payer cash is unchanged — nobody was debited");
            Assert.AreEqual(0, company.FindUncollectedFreightChargeShipmentIds().Count,
                "the charge is marked collected, so the verified path can never recover it");
        }

        [Test]
        public void DuplicateReceipt_SecondTickDeliversNothingMore()
        {
            DriveNormalPathToReturnDay();
            List<LogisticsShipmentStatus> trace = TickToCompleted();
            Assert.AreEqual(LogisticsShipmentStatus.Completed, shipment.Status,
                "shipment did not complete; trace: " + string.Join(", ", trace));
            int payerAtCompletion = payerRuntime.CurrentCashCents;
            int carrierAtCompletion = carrierRuntime.CurrentCashCents;
            int deliveredAtCompletion = shipment.deliveredQuantityUnits;

            for (int tick = 0; tick < 3; tick++)
                Tick();

            Assert.AreEqual(1, bakery.FlourStock.Lots.Count, "still exactly one bakery lot after extra ticks");
            Assert.AreEqual(deliveredAtCompletion, shipment.deliveredQuantityUnits, "delivered units unchanged");
            Assert.AreEqual(0, shipment.remainingQuantityUnits, "wagon stays empty");
            Assert.AreEqual(payerAtCompletion, payerRuntime.CurrentCashCents, "no second debit on later ticks");
            Assert.AreEqual(carrierAtCompletion, carrierRuntime.CurrentCashCents, "no second credit on later ticks");
            Assert.IsTrue(shipment.FreightRevenueCollected, "charge stays collected");
        }

        [Test]
        public void RefusedDelivery_HoldsCargoAndCollectsNothing()
        {
            BindAndAuthorize();
            SimulateSupplierPickup();
            hooks.IsVehicleOffMap = _ => false;

            // Drive to Arrived, then refuse the receipt at the unload step:
            // the receiving hook answers false, as the adapter would for a
            // lot already received by a different shipment.
            var trace = new List<LogisticsShipmentStatus>();
            for (int tick = 0; tick < 8 && shipment.Status != LogisticsShipmentStatus.Arrived; tick++)
            {
                Tick();
                trace.Add(shipment.Status);
            }
            Assert.AreEqual(LogisticsShipmentStatus.Arrived, shipment.Status,
                "shipment did not arrive; trace: " + string.Join(", ", trace));
            hooks.ReceiveCargoLot = (LogisticsShipmentState s, int qty, out string reason) =>
            {
                reason = "audit refusal: recipient refused cargo lot";
                return false;
            };
            int payerBefore = payerRuntime.CurrentCashCents;
            int carrierBefore = carrierRuntime.CurrentCashCents;

            Tick(); // Arrived -> BeginUnloading -> Unloading
            Assert.AreEqual(LogisticsShipmentStatus.Unloading, shipment.Status, "unload must begin before it can be refused");
            Tick(); // Unloading -> WorkUnloading refuses -> Delayed
            Assert.AreEqual(LogisticsShipmentStatus.Delayed, shipment.Status, "a refused delivery strands the shipment Delayed");
            Assert.AreEqual(LogisticsShipmentStatus.Arrived, shipment.resumeStateAfterDelay,
                "the Delayed resume state must be Arrived");
            Tick();
            Tick();
            Assert.AreEqual(LogisticsShipmentStatus.Delayed, shipment.Status,
                "Delayed is a no-op in AdvanceTick — the strand holds across ticks");
            Assert.IsFalse(shipment.deliveryApplied, "nothing was delivered");
            Assert.IsTrue(shipment.sourceLotDebitApplied, "cargo stays in wagon custody");
            Assert.AreEqual(1, shipment.remainingQuantityUnits, "the sack stays on the wagon");
            Assert.IsFalse(lot.Received, "the lot is still unreceived");
            Assert.AreEqual(0, bakery.FlourStock.Lots.Count, "no flour lands on a refused delivery");
            Assert.AreEqual(payerBefore, payerRuntime.CurrentCashCents, "no debit for an undelivered shipment");
            Assert.AreEqual(carrierBefore, carrierRuntime.CurrentCashCents, "no credit for an undelivered shipment");
            Assert.AreEqual(0, freight.FindCompany(CarrierId).FindUncollectedFreightChargeShipmentIds().Count,
                "an undelivered shipment is never in the collectable scan");

            // The resume exists only as a manual path (§1.5 limit).
            freight.Service.ResumeShipment(shipment, hooks);
            Assert.AreEqual(LogisticsShipmentStatus.Arrived, shipment.Status, "manual resume returns the shipment to Arrived");
        }

        [Test]
        public void MidUnloadSaveRestore_CompletesDelivery()
        {
            BindAndAuthorize();
            SimulateSupplierPickup();
            hooks.IsVehicleOffMap = _ => false;

            // Drive to Unloading (BeginUnloading applied, unload task open).
            var trace = new List<LogisticsShipmentStatus>();
            for (int tick = 0; tick < 8 && shipment.Status != LogisticsShipmentStatus.Unloading; tick++)
            {
                Tick();
                trace.Add(shipment.Status);
            }
            Assert.AreEqual(LogisticsShipmentStatus.Unloading, shipment.Status,
                "shipment did not reach Unloading; trace: " + string.Join(", ", trace));

            // Service-level DTO round-trip, as the S9 save-seam tests do for
            // journeys: capture, assert the unload task id is carried, and
            // restore into a fresh service with the same hooks.
            FreightShipmentService.FreightServiceSaveDto dto =
                freight.Service.CaptureSaveDto(new List<LogisticsShipmentState> { shipment });
            Assert.IsTrue(dto.ShipmentIdsWithUnloadTasks.Contains(ShipmentId),
                "the save DTO must carry the open unload task id");
            var service2 = new FreightShipmentService();
            service2.RegisterWagon(wagon);
            List<LogisticsShipmentState> restored = service2.RestoreSaveDto(dto, hooks);
            Assert.AreEqual(1, restored.Count, "one shipment restored");
            LogisticsShipmentState r = restored[0];
            Assert.AreEqual(ShipmentId, r.ShipmentId, "identity preserved");
            Assert.AreEqual(LogisticsShipmentStatus.Unloading, r.Status, "status preserved");
            Assert.AreEqual(1, r.remainingQuantityUnits, "cargo preserved — no loss, no duplication");
            Assert.IsTrue(r.sourceLotDebitApplied, "lot-commit flag preserved");

            // The unload task must be REBOUND, not merely the state
            // round-tripped: without the rebind, WorkUnloading refuses with
            // "no unload task". With it, the next work completes delivery.
            Assert.IsTrue(service2.WorkUnloading(r, 60, hooks, out string workReason), workReason);
            Assert.AreEqual(LogisticsShipmentStatus.Completed, r.Status, "restored shipment completes after restore");
            Assert.IsTrue(r.deliveryApplied, "delivery applied after restore");
            Assert.IsTrue(lot.Received, "the paid Millbrook lot must be marked received");
            Assert.AreEqual(1, bakery.FlourStock.Lots.Count, "exactly one bakery lot must be minted");
            Assert.AreEqual(10, bakery.FlourStock.Lots[0].Units);
            Assert.AreEqual(LotId, bakery.FlourStock.Lots[0].ImportOrderId);

            // And the charge settles exactly once on the restored shipment,
            // through the verified resolver path.
            var company = new FreightCompanyRuntime(CarrierId);
            Assert.IsTrue(company.TryAcceptShipment(r, diag), "restored shipment must be accepted for settlement");
            BusinessRuntimeState settlePayer = Runtime(BakeryId, 20000);
            BusinessRuntimeState settleCarrier = Runtime(CarrierId, 0);
            int posted = company.CollectAndPostUncollectedRevenue(settleCarrier,
                id => id == BakeryId ? settlePayer : null, diag);
            Assert.AreEqual(FreightChargeCents, posted, "the charge settles once after the restore");
            Assert.AreEqual(20000 - FreightChargeCents, settlePayer.CurrentCashCents, "payer debited exactly once");
            Assert.AreEqual(FreightChargeCents, settleCarrier.CurrentCashCents, "carrier credited exactly once");
            Assert.AreEqual(0, company.CollectAndPostUncollectedRevenue(settleCarrier,
                id => id == BakeryId ? settlePayer : null, diag), "no second settlement on a later scan");
            Assert.AreEqual(20000 - FreightChargeCents, settlePayer.CurrentCashCents, "no second debit");
            Assert.AreEqual(FreightChargeCents, settleCarrier.CurrentCashCents, "no second credit");
        }

        [Test]
        public void RepeatHaul_SameDaySecondDepartureRefused_AndSecondOrderRefused()
        {
            // Authority half (Note-10): journey ids are day-granular. After
            // the day-10 departure returns, a second departure for the same
            // vehicle on day 10 is refused with the existing journey id named.
            OffMapJourneyRecord record = DepartMillbrookWagon(10);
            journeys.CompleteDueJourneys(12, null, diag);
            Assert.AreEqual(OffMapJourneyState.Returned, record.State);
            var secondDepartDiag = new List<string>();
            OffMapJourneyRecord second = journeys.DepartVehicle(
                WagonId, "town-center", "Millbrook Grain Co.", 0,
                "Millbrook flour pickup", null,
                new List<string> { AgreementId },
                10, null, null, secondDepartDiag);
            Assert.IsNull(second, "a same-day second departure must be refused");
            Assert.IsTrue(string.Join("; ", secondDepartDiag).Contains(record.JourneyId),
                "the refusal must name the existing journey id, but got: " + string.Join("; ", secondDepartDiag));

            // Order half (Note-18): the single-shot order chain refuses a
            // second order with the same request id, without a second debit.
            // The SimulationDrivers single-shot ids (one order id, one lot
            // id, one request id, IsRecurring = false) are source-reviewed,
            // stated here, not harness-tested.
            int buyerBefore = buyerCash.BalanceCents;
            string refusal = supplier.TryPlaceOrder("opening-flour-b1", BakeryId, "Glen Pierce", 1,
                buyerCash, 0, out OffMapAidCargoLot secondLot);
            Assert.IsTrue(!string.IsNullOrEmpty(refusal) && refusal.Contains("already exists"),
                "the duplicate order must be refused naming the existing order, but got: '" + refusal + "'");
            Assert.IsNull(secondLot, "no second lot is minted");
            Assert.AreEqual(buyerBefore, buyerCash.BalanceCents, "no second debit was made");
        }
    }
}
