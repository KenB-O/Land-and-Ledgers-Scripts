// Priority 7 Slice 0 (2026-11-08): freight integrity and settlement —
// reproduction pins for D-F1 and D-F2. Tests only; no production file is
// touched. Both tests are expected to FAIL at HEAD 196cc8b and to PASS after
// Slices 1–2 respectively.
//
// D-F1 pin: SimulationDrivers is harness-excluded, so the test replicates the
// formula as written (the P1 Slice 0 precedent); it pins the service-side
// consequence of the formula, and the call-site fix itself is source-reviewed
// + Codex-verified in Slice 1.
// D-F2 pin: drives the real driver restore (FreightProductionDriver.
// RestoreSaveDto) with the real dispatch path (AdvanceTick -> TryDispatch).
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
    public sealed class FreightIntegritySettlementTests
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
        private List<int> departureDays;
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
            // FreightRepeatabilityTests.
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
            departureDays = new List<int>();
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
                AnimalOwnerKind.Business, CarrierId, Day, "Freight integrity fixture");
            Assert.IsNotNull(horse);
            Assert.AreEqual("A0", horse.AnimalId.ToString());
            Assert.IsNull(HorseAuthority.Configure(horse, HorseUseCapabilities.GeneralPurpose,
                "ethan-staging", "freight-company-stable", Day));
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

            hooks = new FreightShipmentHooks
            {
                Tasks = tasks,
                Budgets = budgets,
                DayIndex = Day,
                Diagnostics = new List<string>(),
                Reservations = reservations,
                NowGameSeconds = NowGameSeconds(),
                CommitSourceLot = (LogisticsShipmentState s, int qty, out string reason) =>
                {
                    commitCalls++;
                    return adapter.CommitSourceLot(s, qty, out reason);
                },
                ReceiveCargoLot = (LogisticsShipmentState s, int qty, out string reason) =>
                    adapter.ReceiveCargoLot(s, qty, Day, out reason),
            };

            freight = BuildFixtureDriver(registerPayer: true);

            journeyModel = new JourneyModel();
            Assert.IsNull(journeyModel.RegisterLocation(new JourneyLocation("town-center", JourneyLocationKind.Waypoint, "Town Centre", 0f, 0f)));
            RegionalTownRegistry.ApplyAll(journeyModel, new List<string>());
            journeys = new OffMapJourneyAuthority(journeyModel);
        }

        private float NowGameSeconds() => Day * 86400f + 8 * 3600f;

        // ---- Priority 7 Slice 6 (2026-11-08): settlement exactly-once
        // acceptance. Builds the fixture driver + haul-1 request / quote /
        // acceptance / shipment / link / prepare / plan, assigning the
        // `freight`, `shipment`, and `agreement` fields. SetUp calls it
        // with the payer registered (the normal path); the
        // unresolvable-payer test calls it with registerPayer false, so
        // the driver's resolver has no payer runtime and no resolver
        // entry — the charge must stay uncollected (R-04) until the payer
        // is registered and a later tick retries. The discarded first
        // driver in that test never ticks, so its same-id shipment never
        // binds the shared wagon/horse or touches the lot.
        private FreightProductionDriver BuildFixtureDriver(bool registerPayer)
        {
            var driver = new FreightProductionDriver();
            driver.EnsureCompany(CarrierId, carrierRuntime);
            if (registerPayer)
                driver.RegisterBusinessRuntime(BakeryId, payerRuntime);
            driver.Service.RegisterWagon(wagon);

            FreightServiceAgreementBook book = driver.ServiceAgreements;
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
            shipment = driver.CreateShipment(CarrierId, ShipmentId, "millbrook_grain_co", BakeryId, 1,
                new FreightCargoSpec { MassKgPerUnit = 4.54f, VolumeM3PerUnit = 0.02f }, WagonId,
                hooks, EntityId.For(EntityKind.Person, EthanId), new List<EntityId> { horse.AnimalId },
                FreightChargeCents, BakeryId, diag, out string shipmentRefusal,
                AgreementId, LotId, MillbrookFlourFreightCustodyAdapter.FlourSkuId, true);
            Assert.IsNotNull(shipment, shipmentRefusal);
            Assert.IsNull(book.LinkShipment(agreement.AgreementId, ShipmentId));
            Assert.IsNull(book.MarkPreparedForDispatch(agreement.AgreementId, ShipmentId, 50000f));
            driver.SetDispatchPlan(ShipmentId, EntityId.For(EntityKind.Person, EthanId),
                new List<EntityId> { horse.AnimalId });
            return driver;
        }

        private EntityId Handler() => EntityId.For(EntityKind.Person, EthanId);

        private List<EntityId> TeamAnimals() => new List<EntityId> { horse.AnimalId };

        private string BindTeam() => WagonAuthority.TryAssignDraftTeam(wagon,
            new List<AnimalState> { horse },
            LandLedgers.Primitives.EntityId.For(LandLedgers.Primitives.EntityKind.Person, EthanId).ToString(), ShipmentId,
            new AnimalAvailabilityAuthority(reservations), NowGameSeconds(), float.MaxValue);

        private void BindAndAuthorize()
        {
            Assert.IsNull(BindTeam(), "fixture bind must succeed");
            Assert.IsTrue(freight.AuthorizeShipmentDispatch(ShipmentId, out _), "fixture authorization must succeed");
            Assert.IsFalse(shipment.requiresDispatchAuthorization);
        }

        private int Tick() => freight.AdvanceTick(86400f, hooks, Handler(), diag);

        private void ApplyPickupWritesFor(LogisticsShipmentState target)
        {
            target.sourceLotDebitApplied = true;
            target.loadApplied = true;
            target.sourceCommittedAtSchedule = true;
            target.remainingQuantityUnits = target.PlannedQuantityUnits;
            target.physicalLocationId = "wagon:" + WagonId;
        }

        private void SimulateSupplierPickup()
        {
            Assert.IsTrue(adapter.CommitSourceLot(shipment, shipment.PlannedQuantityUnits, out string reason), reason);
            ApplyPickupWritesFor(shipment);
        }

        private static LogisticsShipmentState FindShipmentIn(FreightProductionDriver driver, string shipmentId)
        {
            foreach (LogisticsShipmentState candidate in driver.AllShipments())
            {
                if (candidate != null && string.Equals(candidate.ShipmentId, shipmentId, StringComparison.Ordinal))
                    return candidate;
            }
            return null;
        }

        // Places a real Millbrook lot of the requested sack count on a fresh
        // supplier (fresh stock per case, so the 230 lb starting stock never
        // binds across the boundary cases).
        private OffMapAidCargoLot PlaceLot(int sacks, string requestId)
        {
            var freshSupplier = new OffMapAidBusinessRuntime(MillbrookDefinition());
            var cash = new FakeBusinessCashPort(20000);
            Assert.IsNull(freshSupplier.TryPlaceOrder(requestId, BakeryId, "Glen Pierce", sacks, cash, 0, out OffMapAidCargoLot placed),
                "a " + sacks + "-sack lot must place");
            Assert.AreEqual(sacks, placed.PackageUnits, "the lot must hold the requested sacks");
            Assert.AreEqual(sacks * 10, placed.InventoryUnits, "the lot's total pounds are 10 lb per sack");
            return placed;
        }

        // The production cargo spec as the registration call site builds it
        // AFTER the Slice 1 repair (SimulationDrivers.cs:4811-4816):
        // quantityUnits is the sack count, MassKgPerUnit is the PER-SACK mass
        // (the lot's total pounds divided by its sack count, converted to kg:
        // 10 lb -> 4.5359 kg), VolumeM3PerUnit is 0.01 m3 per sack-count unit
        // (unchanged in Slice 1 — its per-sack figure is unauthored, plan
        // section 4 / B-P7-1, pinned not repaired). Slice 1 rewrite (2026-11-08):
        // the mass line changed from `lot.InventoryUnits * poundsToKg` (the
        // whole lot's mass passed as the per-unit mass, squaring the sack
        // count) to the per-sack expression below; the ships assertion does
        // not change. SimulationDrivers is harness-excluded, so the test
        // replicates the formula (the P1 Slice 0 precedent); it pins the
        // service-side consequence of the formula, and the call-site fix
        // itself is source-reviewed + Codex-verified in Slice 1.
        private static FreightCargoSpec ProductionCargoSpec(OffMapAidCargoLot lot)
        {
            const float poundsToKg = 0.45359237f;
            return new FreightCargoSpec
            {
                MassKgPerUnit = ((float)lot.InventoryUnits / Math.Max(1, lot.PackageUnits)) * poundsToKg,
                VolumeM3PerUnit = 0.01f * Math.Max(1, lot.PackageUnits)
            };
        }

        // Builds the production-spec shipment against a fresh service holding
        // the fixture wagon (450 kg / 4 m3), so the capacity gate is isolated
        // from the fixture shipment's wagon exclusivity.
        private LogisticsShipmentState CreateWithProductionSpec(string shipmentId, OffMapAidCargoLot lot)
        {
            var service = new FreightShipmentService();
            service.RegisterWagon(wagon);
            LogisticsShipmentState created = service.CreateShipment(shipmentId, "millbrook_grain_co", BakeryId,
                lot.PackageUnits, ProductionCargoSpec(lot), WagonId, hooks, out string reason);
            Assert.IsNotNull(created,
                "CreateShipment must return a record (blocked or planned) for a " + lot.PackageUnits + "-sack lot, but refused: " + reason);
            return created;
        }

        private FreightProductionDriver RestoreIntoFreshDriver(FreightShipmentService.FreightServiceSaveDto dto)
        {
            var restoredFreight = new FreightProductionDriver();
            restoredFreight.EnsureCompany(CarrierId, carrierRuntime);
            restoredFreight.RegisterBusinessRuntime(BakeryId, payerRuntime);
            restoredFreight.Service.RegisterWagon(wagon);
            List<LogisticsShipmentState> restored = restoredFreight.RestoreSaveDto(dto, hooks, diag);
            Assert.AreEqual(1, restored.Count, "exactly 1 shipment restores in this fixture");
            return restoredFreight;
        }

        // Priority 7 Slice 8 (2026-11-08): the multi-shipment form of
        // RestoreIntoFreshDriver, for saves carrying more than the fixture's
        // single haul (a mid-transit second haul, or a full + smaller save
        // pair). Same fresh-driver setup; only the expected count differs.
        private FreightProductionDriver RestoreIntoFreshDriverMulti(
            FreightShipmentService.FreightServiceSaveDto dto, int expectedCount)
        {
            var restoredFreight = new FreightProductionDriver();
            restoredFreight.EnsureCompany(CarrierId, carrierRuntime);
            restoredFreight.RegisterBusinessRuntime(BakeryId, payerRuntime);
            restoredFreight.Service.RegisterWagon(wagon);
            List<LogisticsShipmentState> restored = restoredFreight.RestoreSaveDto(dto, hooks, diag);
            Assert.AreEqual(expectedCount, restored.Count,
                "exactly " + expectedCount + " shipments restore in this fixture");
            return restoredFreight;
        }

        // Priority 7 Slice 8 (2026-11-08): finds the persisted dispatch
        // plan for one shipment in a save DTO, or null when the DTO carries
        // none for it. CaptureSaveDto is the read surface for the driver's
        // private plan map (the P5 Slice 5 precedent: the private
        // load/unload-task maps are likewise read through the capture).
        private static FreightShipmentService.FreightDispatchPlanSaveDto FindPlanIn(
            FreightShipmentService.FreightServiceSaveDto dto, string shipmentId)
        {
            if (dto == null || dto.DispatchPlans == null) return null;
            foreach (FreightShipmentService.FreightDispatchPlanSaveDto plan in dto.DispatchPlans)
            {
                if (plan != null && string.Equals(plan.ShipmentId, shipmentId, StringComparison.Ordinal)) return plan;
            }
            return null;
        }

        // Checks that a lot built with the production cargo spec SHIPS, collecting
        // any mismatch instead of asserting immediately: NUnit aborts a test at
        // its first failing assert, so hard asserts would hide the 10-sack
        // boundary behind the 16-sack failure. The single failure message at
        // the end of ProductionCargoSpec_SixteenSackLot_BlockedPreLoad therefore
        // evidences every failing case in one run. remainingQuantityUnits is
        // NOT asserted here — CreateShipment sets only plannedQuantityUnits;
        // remainingQuantityUnits is first assigned in BeginLoading, so a fresh
        // Planned shipment legitimately reads 0.
        private void CheckShipsWithProductionSpec(int sacks, string requestId, List<string> failures)
        {
            OffMapAidCargoLot lot = PlaceLot(sacks, requestId);
            LogisticsShipmentState created = CreateWithProductionSpec("freight-shipment:pin-" + sacks + "sack", lot);
            if (created.IsPreLoadBlocked)
            {
                failures.Add(sacks + "-sack lot blocked pre-load with reason '" + created.BlockedReason
                    + "' (expected to ship)");
                return;
            }
            if (created.Status != LogisticsShipmentStatus.Planned)
                failures.Add(sacks + "-sack lot status is " + created.Status + " (expected Planned)");
            if (created.loadApplied)
                failures.Add(sacks + "-sack lot has loadApplied true at creation (expected false)");
            if (created.PlannedQuantityUnits != sacks)
                failures.Add(sacks + "-sack lot PlannedQuantityUnits is " + created.PlannedQuantityUnits
                    + " (expected " + sacks + ")");
        }

        [Test]
        public void ProductionCargoSpec_SixteenSackLot_BlockedPreLoad()
        {
            // D-F1 (fixed-behaviour form, coordinator correction 2026-11-08,
            // Option 2): the production spec as written in Slice 0 squared
            // the sack count — a 16-sack lot was checked at 16 x (160 lb in
            // kg) = 1,161.2 kg against the authored 450 kg wagon, so it
            // blocked pre-load; the test asserted the CORRECT behaviour (it
            // SHIPS) and therefore FAILED pre-fix, as a reproduction pin
            // must. Slice 1 (2026-11-08) rewrote only the formula replica in
            // ProductionCargoSpec to the corrected per-sack mass (10 lb ->
            // 4.5359 kg); the ships assertion never flips. The old-formula
            // blocking behavior stays pinned by the service-level
            // OverweightCargo_BlockedPreLoad_ZeroCargo_MR_P001 test, untouched.
            // SimulationDrivers is harness-excluded, so the test replicates
            // the formula (the P1 Slice 0 precedent); it pins the
            // service-side consequence of the formula, and the call-site fix
            // itself is source-reviewed + Codex-verified in Slice 1.
            var failures = new List<string>();
            // 16 sacks: checked at 1,161.2 kg under the old formula — FAILED
            // pre-fix; under the corrected formula it is 72.6 kg and ships.
            CheckShipsWithProductionSpec(16, "pin-sixteen-sack", failures);
            // Boundary companions, same formula: 9 sacks ship (367.4 kg
            // under the old formula, 40.8 kg under the corrected one) and
            // 10 sacks ship — under the old formula it blocked (453.6 kg),
            // so this companion FAILED pre-fix and evidenced the boundary;
            // after the Slice 1 replica rewrite both ship.
            CheckShipsWithProductionSpec(9, "pin-nine-sack", failures);
            CheckShipsWithProductionSpec(10, "pin-ten-sack", failures);
            Assert.IsTrue(failures.Count == 0,
                "D-F1: lots built with the production cargo spec must ship; failures: "
                + string.Join(" | ", failures));
        }

        // Slice 1 acceptance (2026-11-08): a lot built with the CORRECTED
        // per-sack spec (4.5359 kg/unit, volume as the production formula
        // gives it) creates successfully against the 450 kg / 4 m3 wagon,
        // binds its lot, and is not pre-load blocked. This proves the service
        // side accepts correctly-specified multi-sack shipments; it cannot
        // prove the harness-excluded call site (source-reviewed + Codex).
        private void CheckCorrectedCargoSpecShipsWhole(int sacks, string requestId)
        {
            OffMapAidCargoLot lot = PlaceLot(sacks, requestId);
            FreightCargoSpec spec = ProductionCargoSpec(lot);
            Assert.IsTrue(Math.Abs(spec.MassKgPerUnit - 4.5359237f) < 0.001f,
                "the corrected per-sack mass for a " + sacks + "-sack lot must be 10 lb in kg (4.5359), but was "
                + spec.MassKgPerUnit);
            var service = new FreightShipmentService();
            service.RegisterWagon(wagon);
            LogisticsShipmentState created = service.CreateShipment(
                "freight-shipment:corrected-" + sacks + "sack", "millbrook_grain_co", BakeryId,
                lot.PackageUnits, spec, WagonId, hooks, out string reason);
            Assert.IsNotNull(created,
                "CreateShipment must return a record for a " + sacks + "-sack lot with the corrected spec, but refused: " + reason);
            Assert.IsTrue(!created.IsPreLoadBlocked,
                "a " + sacks + "-sack lot with the corrected spec must not be pre-load blocked, but was: " + created.BlockedReason);
            Assert.AreEqual(LogisticsShipmentStatus.Planned, created.Status,
                "a " + sacks + "-sack lot with the corrected spec must be Planned");
            Assert.IsTrue(!created.loadApplied, "a fresh " + sacks + "-sack shipment must not have loadApplied at creation");
            Assert.AreEqual(sacks, created.PlannedQuantityUnits,
                "a " + sacks + "-sack lot must plan " + sacks + " units");
            Assert.IsTrue(service.TryBindSourceLot(created, lot.LotId,
                    MillbrookFlourFreightCustodyAdapter.FlourSkuId, out string bindReason),
                "the " + sacks + "-sack lot must bind to its shipment: " + bindReason);
            Assert.AreEqual(lot.LotId, created.CargoLotId,
                "the shipment must carry the lot id after binding");
        }

        [Test]
        public void CorrectedCargoSpec_SixteenSackLot_ShipsWhole()
        {
            // Slice 1 acceptance: the P3 Slice 4b clamped 16-sack lot
            // (160 lb) with the corrected per-sack spec is checked at
            // 16 x 4.5359 kg = 72.6 kg — far inside the authored 450 kg
            // wagon — and ships whole in one shipment.
            CheckCorrectedCargoSpecShipsWhole(16, "corrected-sixteen-sack");
        }

        [Test]
        public void CorrectedCargoSpec_EighteenSackLot_ShipsWhole()
        {
            // Slice 1 acceptance: an 18-sack lot (180 lb) with the corrected
            // per-sack spec is checked at 18 x 4.5359 kg = 81.6 kg — far
            // inside the authored 450 kg wagon — and ships whole.
            CheckCorrectedCargoSpecShipsWhole(18, "corrected-eighteen-sack");
        }

        [Test]
        public void DispatchPlan_LostAcrossRestore_PlannedShipmentNeverDispatches()
        {
            // D-F2: drive the fixture to a Planned, authorized, picked-up
            // shipment whose dispatch task is recorded but whose transport is
            // not yet bound (physicalTransportBound == false).
            BindAndAuthorize();
            SimulateSupplierPickup();
            Assert.AreEqual(LogisticsShipmentStatus.Planned, shipment.Status, "the picked-up shipment is Planned");
            Assert.IsTrue(shipment.loadApplied, "the supplier pickup applied the load");
            Assert.IsTrue(!shipment.physicalTransportBound, "transport is not yet bound before dispatch");

            // Capture the Planned snapshot BEFORE any dispatch happens.
            FreightShipmentService.FreightServiceSaveDto plannedDto = freight.CaptureSaveDto();

            // CONTROL first: dispatch the original to InTransit, capture,
            // restore, and verify the restored InTransit shipment advances —
            // pinning that the transport-bound plan rebuild works and the
            // failure below is specific to the Planned case.
            hooks.IsVehicleOffMap = _ => false;
            Tick();
            Assert.AreEqual(LogisticsShipmentStatus.InTransit, shipment.Status,
                "control setup: the original shipment must dispatch to InTransit");
            FreightShipmentService.FreightServiceSaveDto inTransitDto = freight.CaptureSaveDto();
            FreightProductionDriver controlDriver = RestoreIntoFreshDriver(inTransitDto);
            LogisticsShipmentState controlRestored = FindShipmentIn(controlDriver, ShipmentId);
            Assert.IsNotNull(controlRestored, "the control restore must hold the shipment");
            Assert.AreEqual(LogisticsShipmentStatus.InTransit, controlRestored.Status,
                "the control shipment resumes InTransit after restore");
            diag.Clear();
            controlDriver.AdvanceTick(86400f, hooks, Handler(), diag);
            Assert.AreEqual(LogisticsShipmentStatus.Arrived, controlRestored.Status,
                "control: the restored InTransit shipment must advance to Arrived on the next tick; diagnostics: "
                + string.Join("; ", diag));

            // MAIN: restore the Planned snapshot into a fresh driver and tick
            // with the wagon home. The restored shipment must dispatch.
            FreightProductionDriver restoredFreight = RestoreIntoFreshDriver(plannedDto);
            LogisticsShipmentState restored = FindShipmentIn(restoredFreight, ShipmentId);
            Assert.IsNotNull(restored, "the restored driver must hold the Planned shipment");
            Assert.AreEqual(LogisticsShipmentStatus.Planned, restored.Status, "the restored shipment is Planned");
            Assert.IsTrue(restored.loadApplied, "the restored shipment keeps its applied load");
            Assert.IsTrue(!restored.physicalTransportBound, "the restored shipment is not transport-bound");
            hooks.IsVehicleOffMap = _ => false;
            diag.Clear();
            var trace = new List<LogisticsShipmentStatus>();
            for (int tick = 0; tick < 4 && restored.Status != LogisticsShipmentStatus.InTransit; tick++)
            {
                restoredFreight.AdvanceTick(86400f, hooks, Handler(), diag);
                trace.Add(restored.Status);
            }
            string diagnosticsText = string.Join("; ", diag);
            Assert.IsTrue(!diagnosticsText.Contains("no dispatch plan"),
                "the wait must not occur: the dispatch plan was restored from the save DTO, so the shipment must dispatch without ever waiting on a missing plan, but diagnostics were: " + diagnosticsText);
            Assert.AreEqual(LogisticsShipmentStatus.InTransit, restored.Status,
                "D-F2: the restored Planned shipment must dispatch (InTransit) within the tick loop; trace: "
                + string.Join(", ", trace) + "; diagnostics: " + diagnosticsText);
        }

        [Test]
        public void DispatchPlan_OldSaveWithoutPlans_RestoresAsToday_NoCrash()
        {
            // Priority 7 Slice 2 (D-F2) old-save compatibility: a save
            // written before the repair carries no dispatch-plan field
            // (null here, as a deserialized pre-fix DTO would read it), and
            // restores exactly as today — no plan, the same "no dispatch
            // plan" diagnostic, no crash, and the shipment stays Planned
            // (stranded, as pre-fix saves in this shape stay stranded; no
            // silent fix is invented for old saves). Format version stays 6.
            BindAndAuthorize();
            SimulateSupplierPickup();
            Assert.AreEqual(LogisticsShipmentStatus.Planned, shipment.Status, "the picked-up shipment is Planned");
            Assert.IsTrue(shipment.loadApplied, "the supplier pickup applied the load");
            Assert.IsTrue(!shipment.physicalTransportBound, "transport is not yet bound before dispatch");

            FreightShipmentService.FreightServiceSaveDto dto = freight.CaptureSaveDto();
            Assert.IsNotNull(dto.DispatchPlans, "a current capture must carry the plan list");
            Assert.AreEqual(1, dto.DispatchPlans.Count, "the capture must carry exactly one plan for this fixture");
            Assert.AreEqual(ShipmentId, dto.DispatchPlans[0].ShipmentId, "the carried plan must name the shipment");
            Assert.AreEqual("P7", dto.DispatchPlans[0].DriverPersonId, "the carried plan must name driver Ethan (P7)");
            Assert.AreEqual(1, dto.DispatchPlans[0].AnimalIds.Count, "the carried plan must name one draft animal");
            Assert.AreEqual("A0", dto.DispatchPlans[0].AnimalIds[0], "the carried plan must name horse A0");

            // Simulate the old save: the field is absent (null).
            dto.DispatchPlans = null;
            FreightProductionDriver restoredFreight = RestoreIntoFreshDriver(dto);
            LogisticsShipmentState restored = FindShipmentIn(restoredFreight, ShipmentId);
            Assert.IsNotNull(restored, "the restored driver must hold the Planned shipment");
            Assert.AreEqual(LogisticsShipmentStatus.Planned, restored.Status, "the restored shipment is Planned");
            Assert.IsTrue(restored.loadApplied, "the restored shipment keeps its applied load");
            hooks.IsVehicleOffMap = _ => false;
            diag.Clear();
            restoredFreight.AdvanceTick(86400f, hooks, Handler(), diag);
            Assert.AreEqual(LogisticsShipmentStatus.Planned, restored.Status,
                "an old save without plans restores as today: the Planned shipment still waits");
            Assert.IsTrue(string.Join("; ", diag).Contains("no dispatch plan"),
                "an old save without plans must keep today's diagnostic, but diagnostics were: "
                + string.Join("; ", diag));
        }

        // ---- Priority 7 Slice 3 (2026-11-08): third consecutive legitimate
        // delivery acceptance. Tests only; no production file is touched.
        // Extends the proven two-haul sequence (FreightRepeatabilityTests)
        // to a THIRD consecutive legitimate delivery with a distinct source
        // lot and actual supplier pickup. Helpers below mirror the
        // FreightRepeatabilityTests shapes exactly (same ids, same call
        // order, same terminal-sweep replica); only the haul number, day,
        // and quote/shipment ids change.

        private string BindTeamFor(string shipmentId) => WagonAuthority.TryAssignDraftTeam(wagon,
            new List<AnimalState> { horse },
            LandLedgers.Primitives.EntityId.For(LandLedgers.Primitives.EntityKind.Person, EthanId).ToString(), shipmentId,
            new AnimalAvailabilityAuthority(reservations), NowGameSeconds(), float.MaxValue);

        private WorkTask FindLoadTaskFor(string shipmentId) => tasks.FindOpenTask("freight:load:" + shipmentId,
            EntityId.For(EntityKind.Business, 3), EntityId.Invalid);

        // Drives one already-picked-up shipment to Completed through the
        // return-day tick loop, on the given simulated day (the wagon is
        // back: IsVehicleOffMap false). Asserts no load task is created at
        // any tick. Records the day the shipment first reaches InTransit as
        // that haul's departure day, so the test can assert the hauls
        // depart on distinct days.
        private List<LogisticsShipmentStatus> TickUntilCompleted(LogisticsShipmentState target, string label, int dayIndex)
        {
            hooks.DayIndex = dayIndex;
            hooks.IsVehicleOffMap = _ => false;
            var trace = new List<LogisticsShipmentStatus>();
            bool departureRecorded = false;
            for (int tick = 0; tick < 8 && target.Status != LogisticsShipmentStatus.Completed; tick++)
            {
                Tick();
                trace.Add(target.Status);
                if (!departureRecorded && target.Status == LogisticsShipmentStatus.InTransit)
                {
                    departureDays.Add(dayIndex);
                    departureRecorded = true;
                }
                Assert.IsNull(FindLoadTaskFor(target.ShipmentId),
                    "no load task is created at any tick in the loop for " + label + " (tick " + tick + ")");
            }
            Assert.AreEqual(LogisticsShipmentStatus.Completed, target.Status,
                label + " did not complete; trace: " + string.Join(", ", trace));
            return trace;
        }

        // Priority 7 Slice 8 (2026-11-08): the same return-day tick loop as
        // TickUntilCompleted, but driving the GIVEN driver (a restored
        // driver) instead of the fixture `freight` field, so a test can
        // complete a haul with the restored driver only and prove nothing
        // settles twice. Same shape: no load task at any tick, the
        // departure day recorded when the shipment first reaches InTransit.
        private List<LogisticsShipmentStatus> TickUntilCompletedOn(FreightProductionDriver driver,
            LogisticsShipmentState target, string label, int dayIndex)
        {
            hooks.DayIndex = dayIndex;
            hooks.IsVehicleOffMap = _ => false;
            var trace = new List<LogisticsShipmentStatus>();
            bool departureRecorded = false;
            for (int tick = 0; tick < 8 && target.Status != LogisticsShipmentStatus.Completed; tick++)
            {
                driver.AdvanceTick(86400f, hooks, Handler(), diag);
                trace.Add(target.Status);
                if (!departureRecorded && target.Status == LogisticsShipmentStatus.InTransit)
                {
                    departureDays.Add(dayIndex);
                    departureRecorded = true;
                }
                Assert.IsNull(FindLoadTaskFor(target.ShipmentId),
                    "no load task is created at any tick in the loop for " + label + " (tick " + tick + ")");
            }
            Assert.AreEqual(LogisticsShipmentStatus.Completed, target.Status,
                label + " did not complete; trace: " + string.Join(", ", trace));
            return trace;
        }

        // Haul 1 end to end, exactly as FreightRepeatabilityTests does it.
        // Asserts completion, receipt, and the fulfilled agreement; the
        // caller owns the cash assertions.
        private void DriveHaul1ToCompletion(bool expectFulfilled = true)
        {
            BindAndAuthorize();
            SimulateSupplierPickup();
            TickUntilCompleted(shipment, "haul 1", Day);
            Assert.IsTrue(lot.Received, "the paid Millbrook lot must be marked received");
            Assert.AreEqual(1, bakery.FlourStock.Lots.Count);
            Assert.AreEqual(10, bakery.FlourStock.Lots[0].Units,
                "the inventory transfer is pounds, not the separate 18 bakery-unit quotation");
            Assert.AreEqual(LotId, bakery.FlourStock.Lots[0].ImportOrderId);
            Assert.IsTrue(shipment.deliveryApplied);
            if (expectFulfilled)
            {
                Assert.AreEqual(FreightServiceAgreementStatus.Fulfilled, agreement.Status,
                    "haul 1 agreement must be fulfilled by the tick's MarkFulfilled path");
            }
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

        private void SimulateSupplierPickupFor(LogisticsShipmentState target)
        {
            Assert.IsTrue(adapter.CommitSourceLot(target, target.PlannedQuantityUnits, out string reason), reason);
            ApplyPickupWritesFor(target);
        }

        // Places the next paid lot and builds its request, quote,
        // acceptance, shipment, and link. The day parameter is the haul's
        // simulated day; the reorder id is deterministic
        // ("flour-reorder-d<day>-n<k>", k = count of existing lots + 1).
        private void BuildHaul(int dayIndex, string expectedOrderId,
            out OffMapAidCargoLot nextLot, out string requestId,
            out FreightServiceAgreement nextAgreement, out LogisticsShipmentState nextShipment)
        {
            int buyerBefore = buyerCash.BalanceCents;
            Assert.IsNull(MillbrookFlourReorder.PlaceReorder(
                supplier, buyerCash, BakeryId, "Glen Pierce", 1, dayIndex, diag, out nextLot),
                "reorder on day " + dayIndex + " must place: " + string.Join("; ", diag));
            Assert.IsNotNull(nextLot, "reorder on day " + dayIndex + " must mint a lot");
            Assert.AreEqual(expectedOrderId, nextLot.OrderId,
                "reorder id is deterministic (day " + dayIndex + "; TryPlaceOrder prefixes the business id)");
            Assert.AreEqual(buyerBefore - nextLot.TotalCostCents, buyerCash.BalanceCents,
                "reorder on day " + dayIndex + " debits buyer cash exactly once");
            Assert.AreEqual(48, nextLot.TotalCostCents, "one sack costs the authored 48c");

            FreightServiceAgreementBook book = freight.ServiceAgreements;
            requestId = "freight-request:" + nextLot.OrderId;
            var request = new FreightServiceRequest
            {
                RequestId = requestId, RequestingBusinessId = BakeryId, CarrierBusinessId = CarrierId,
                PickupBusinessId = "millbrook_grain_co", DestinationBusinessId = BakeryId,
                CargoLotId = nextLot.LotId, Quantity = 1, QuantityUnit = "lb", RequestingPersonId = 2,
                CarrierOperatorPersonId = EthanId, PickupLocationId = "millbrook-pickup",
                DestinationLocationId = "bakery-door",
                CargoSkuId = MillbrookFlourFreightCustodyAdapter.FlourSkuId
            };
            Assert.IsNull(book.CreateRequest(request, out _), "request for day " + dayIndex + " must record");
            Assert.IsNull(book.SetQuote(requestId, new FreightServiceQuote
            {
                QuoteId = "quote-" + dayIndex, OneWayDistanceMiles = 50f, ChargeableMiles = 100,
                BaseServiceFeeCents = 25, RatePerMileCents = 1, MileageChargeCents = 100,
                TotalPriceCents = FreightChargeCents, PayerBusinessId = BakeryId, PaymentTiming = "After successful delivery"
            }), "quote for day " + dayIndex + " must record");
            Assert.IsNull(book.Accept(requestId, EthanId, dayIndex, dayIndex * 86400f + 40000f, out nextAgreement),
                "request for day " + dayIndex + " must accept");
            Assert.IsNotNull(nextAgreement, "acceptance for day " + dayIndex + " must produce an agreement");
            string nextShipmentId = "freight-shipment:" + requestId;
            nextShipment = freight.CreateShipment(CarrierId, nextShipmentId, "millbrook_grain_co", BakeryId, 1,
                new FreightCargoSpec { MassKgPerUnit = 4.54f, VolumeM3PerUnit = 0.02f }, WagonId,
                hooks, EntityId.For(EntityKind.Person, EthanId), TeamAnimals(),
                FreightChargeCents, BakeryId, diag, out string shipmentRefusal,
                nextAgreement.AgreementId, nextLot.LotId, MillbrookFlourFreightCustodyAdapter.FlourSkuId, true);
            Assert.IsNotNull(nextShipment, "shipment for day " + dayIndex + " must create: " + shipmentRefusal);
            Assert.AreEqual(nextLot.LotId, nextShipment.CargoLotId, "the new lot must bind cleanly to the new shipment");
            Assert.IsNull(book.LinkShipment(nextAgreement.AgreementId, nextShipmentId), "shipment for day " + dayIndex + " must link");
        }

        // Builds a multi-sack haul the way BuildHaul builds a 1-sack haul,
        // except the shipment is created with the lot's sack count and the
        // corrected production cargo spec (ProductionCargoSpec: per-sack
        // mass 4.5359 kg, volume as the production formula gives it). The
        // request itself keeps BuildHaul's shape exactly (Quantity 1,
        // QuantityUnit "lb") — no validation links the request quantity to
        // the shipment quantity, and no new request semantics are invented.
        // expectClamp: true when the requested sacks exceed Millbrook's live
        // stock and the P3 Slice 4b clamp must fire (the diag names it and
        // the lot is the clamped size); false when the full request places.
        private void BuildMultiSackHaul(int dayIndex, int sacksRequested, string expectedOrderId, bool expectClamp,
            out OffMapAidCargoLot nextLot, out string requestId,
            out FreightServiceAgreement nextAgreement, out LogisticsShipmentState nextShipment)
        {
            int buyerBefore = buyerCash.BalanceCents;
            int diagBefore = diag.Count;
            Assert.IsNull(MillbrookFlourReorder.PlaceReorder(
                supplier, buyerCash, BakeryId, "Glen Pierce", sacksRequested, dayIndex, diag, out nextLot),
                "multi-sack reorder on day " + dayIndex + " must place: " + string.Join("; ", diag));
            Assert.IsNotNull(nextLot, "multi-sack reorder on day " + dayIndex + " must mint a lot");
            Assert.AreEqual(expectedOrderId, nextLot.OrderId,
                "reorder id is deterministic (day " + dayIndex + "; TryPlaceOrder prefixes the business id)");
            Assert.AreEqual(buyerBefore - nextLot.TotalCostCents, buyerCash.BalanceCents,
                "multi-sack reorder on day " + dayIndex + " debits buyer cash exactly once");
            string newDiag = string.Join("; ", diag.GetRange(diagBefore, diag.Count - diagBefore));
            if (expectClamp)
            {
                Assert.IsTrue(newDiag.Contains("clamped"),
                    "the " + sacksRequested + "-sack request against short stock must clamp, but the reorder diagnostics were: " + newDiag);
                Assert.IsTrue(nextLot.PackageUnits < sacksRequested,
                    "a clamped lot must hold fewer sacks than requested (" + sacksRequested + "), but holds " + nextLot.PackageUnits);
            }
            else
            {
                Assert.IsTrue(!newDiag.Contains("clamped"),
                    "the " + sacksRequested + "-sack request against sufficient stock must not clamp, but the reorder diagnostics were: " + newDiag);
                Assert.AreEqual(sacksRequested, nextLot.PackageUnits,
                    "the unclamped lot must hold the requested sacks");
            }
            Assert.AreEqual(nextLot.PackageUnits * 10, nextLot.InventoryUnits, "the lot's total pounds are 10 lb per sack");
            Assert.AreEqual(nextLot.PackageUnits * 48, nextLot.TotalCostCents, "the lot costs the authored 48c per sack");

            FreightServiceAgreementBook book = freight.ServiceAgreements;
            requestId = "freight-request:" + nextLot.OrderId;
            var request = new FreightServiceRequest
            {
                RequestId = requestId, RequestingBusinessId = BakeryId, CarrierBusinessId = CarrierId,
                PickupBusinessId = "millbrook_grain_co", DestinationBusinessId = BakeryId,
                CargoLotId = nextLot.LotId, Quantity = 1, QuantityUnit = "lb", RequestingPersonId = 2,
                CarrierOperatorPersonId = EthanId, PickupLocationId = "millbrook-pickup",
                DestinationLocationId = "bakery-door",
                CargoSkuId = MillbrookFlourFreightCustodyAdapter.FlourSkuId
            };
            Assert.IsNull(book.CreateRequest(request, out _), "request for day " + dayIndex + " must record");
            Assert.IsNull(book.SetQuote(requestId, new FreightServiceQuote
            {
                QuoteId = "quote-" + dayIndex, OneWayDistanceMiles = 50f, ChargeableMiles = 100,
                BaseServiceFeeCents = 25, RatePerMileCents = 1, MileageChargeCents = 100,
                TotalPriceCents = FreightChargeCents, PayerBusinessId = BakeryId, PaymentTiming = "After successful delivery"
            }), "quote for day " + dayIndex + " must record");
            Assert.IsNull(book.Accept(requestId, EthanId, dayIndex, dayIndex * 86400f + 40000f, out nextAgreement),
                "request for day " + dayIndex + " must accept");
            Assert.IsNotNull(nextAgreement, "acceptance for day " + dayIndex + " must produce an agreement");
            string nextShipmentId = "freight-shipment:" + requestId;
            nextShipment = freight.CreateShipment(CarrierId, nextShipmentId, "millbrook_grain_co", BakeryId, nextLot.PackageUnits,
                ProductionCargoSpec(nextLot), WagonId,
                hooks, EntityId.For(EntityKind.Person, EthanId), TeamAnimals(),
                FreightChargeCents, BakeryId, diag, out string shipmentRefusal,
                nextAgreement.AgreementId, nextLot.LotId, MillbrookFlourFreightCustodyAdapter.FlourSkuId, true);
            Assert.IsNotNull(nextShipment, "multi-sack shipment for day " + dayIndex + " must create: " + shipmentRefusal);
            Assert.IsTrue(!nextShipment.IsPreLoadBlocked,
                "a " + nextLot.PackageUnits + "-sack lot with the corrected spec must ship whole, not block pre-load, but was: " + nextShipment.BlockedReason);
            Assert.AreEqual(nextLot.PackageUnits, nextShipment.PlannedQuantityUnits,
                "the multi-sack shipment must plan the lot's whole sack count");
            Assert.AreEqual(nextLot.LotId, nextShipment.CargoLotId, "the new lot must bind cleanly to the new shipment");
            Assert.IsNull(book.LinkShipment(nextAgreement.AgreementId, nextShipmentId), "shipment for day " + dayIndex + " must link");
        }

        private void BuildSecondHaul(out OffMapAidCargoLot lot2, out string request2Id,
            out FreightServiceAgreement agreement2, out LogisticsShipmentState shipment2)
        {
            BuildHaul(Day2, "millbrook_grain_co:flour-reorder-d13-n2",
                out lot2, out request2Id, out agreement2, out shipment2);
        }

        private void BuildThirdHaul(out OffMapAidCargoLot lot3, out string request3Id,
            out FreightServiceAgreement agreement3, out LogisticsShipmentState shipment3)
        {
            BuildHaul(Day3, "millbrook_grain_co:flour-reorder-d14-n3",
                out lot3, out request3Id, out agreement3, out shipment3);
        }

        // Prepare + sweep + rebind + authorize for a follow-on haul, given
        // the terminal (just-completed) shipment id whose draft-team mirror
        // the sweep must clear first.
        private void PrepareAndDispatchHaul(FreightServiceAgreement nextAgreement,
            LogisticsShipmentState nextShipment, string terminalShipmentId, int dayIndex)
        {
            FreightServiceAgreementBook book = freight.ServiceAgreements;
            Assert.IsNull(book.MarkPreparedForDispatch(nextAgreement.AgreementId, nextShipment.ShipmentId, dayIndex * 86400f + 50000f),
                "agreement for day " + dayIndex + " must prepare for dispatch");
            freight.SetDispatchPlan(nextShipment.ShipmentId, EntityId.For(EntityKind.Person, EthanId), TeamAnimals());
            ReleaseTerminalDraftTeam(terminalShipmentId);
            Assert.IsNull(BindTeamFor(nextShipment.ShipmentId), "the draft team must rebind to the day-" + dayIndex + " haul");
            Assert.IsTrue(freight.AuthorizeShipmentDispatch(nextShipment.ShipmentId, out _),
                "shipment authorization for day " + dayIndex + " must succeed");
            Assert.IsFalse(nextShipment.requiresDispatchAuthorization);
        }

        private void PrepareAndDispatchSecondHaul(FreightServiceAgreement agreement2, LogisticsShipmentState shipment2)
        {
            PrepareAndDispatchHaul(agreement2, shipment2, ShipmentId, Day2);
        }

        private void PrepareAndDispatchThirdHaul(FreightServiceAgreement agreement3, LogisticsShipmentState shipment3, string terminalShipmentId)
        {
            PrepareAndDispatchHaul(agreement3, shipment3, terminalShipmentId, Day3);
        }

        [Test]
        public void ThirdHaul_AfterSecondCompletes_SettlesOnceAndFreesCapacity()
        {
            // Third consecutive legitimate delivery: after hauls 1-2
            // complete, a third lot (day 14, deterministic id
            // flour-reorder-d14-n3, 48c debited once) is picked up at the
            // supplier and delivered, settling a third 125c exactly once.
            // Expected to PASS after Slices 1-2: it composes already-sound
            // seams; a failure names the leg that fails.
            int payerBefore = payerRuntime.CurrentCashCents;
            int carrierBefore = carrierRuntime.CurrentCashCents;

            DriveHaul1ToCompletion();
            Assert.AreEqual(payerBefore - FreightChargeCents, payerRuntime.CurrentCashCents,
                "haul 1 freight charge debited from the bakery exactly once");
            Assert.AreEqual(carrierBefore + FreightChargeCents, carrierRuntime.CurrentCashCents,
                "haul 1 freight charge credited to the carrier exactly once");

            BuildSecondHaul(out OffMapAidCargoLot lot2, out string request2Id,
                out FreightServiceAgreement agreement2, out LogisticsShipmentState shipment2);
            PrepareAndDispatchSecondHaul(agreement2, shipment2);
            SimulateSupplierPickupFor(shipment2);
            TickUntilCompleted(shipment2, "haul 2", Day2);

            Assert.IsTrue(lot2.Received, "the second paid Millbrook lot must be marked received");
            Assert.AreEqual(2, bakery.FlourStock.Lots.Count, "the bakery must hold exactly two flour lots after haul 2");
            Assert.AreEqual(payerBefore - 2 * FreightChargeCents, payerRuntime.CurrentCashCents,
                "payer debited a second 125c (total -250c across both hauls)");
            Assert.AreEqual(carrierBefore + 2 * FreightChargeCents, carrierRuntime.CurrentCashCents,
                "carrier credited a second 125c (total +250c across both hauls)");
            Assert.AreEqual(FreightServiceAgreementStatus.Fulfilled, agreement2.Status,
                "second agreement must be fulfilled");

            BuildThirdHaul(out OffMapAidCargoLot lot3, out string request3Id,
                out FreightServiceAgreement agreement3, out LogisticsShipmentState shipment3);
            PrepareAndDispatchThirdHaul(agreement3, shipment3, shipment2.ShipmentId);
            SimulateSupplierPickupFor(shipment3);
            TickUntilCompleted(shipment3, "haul 3", Day3);

            Assert.IsTrue(lot3.Received, "the third paid Millbrook lot must be marked received");
            Assert.AreEqual(3, bakery.FlourStock.Lots.Count, "the bakery must hold exactly three flour lots");
            var importIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (BakeryFlourLot flourLot in bakery.FlourStock.Lots)
            {
                Assert.AreEqual(10, flourLot.Units, "each haul credits one 10 lb sack");
                Assert.IsTrue(importIds.Add(flourLot.ImportOrderId), "no bakery lot was minted twice for one Millbrook lot");
            }
            Assert.IsTrue(importIds.Contains(LotId), "haul 1 lot must be among the bakery lots");
            Assert.IsTrue(importIds.Contains(lot2.LotId), "haul 2 lot must be among the bakery lots");
            Assert.IsTrue(importIds.Contains(lot3.LotId), "haul 3 lot must be among the bakery lots");
            Assert.AreEqual(payerBefore - 3 * FreightChargeCents, payerRuntime.CurrentCashCents,
                "payer debited a third 125c (total -375c across the three hauls)");
            Assert.AreEqual(carrierBefore + 3 * FreightChargeCents, carrierRuntime.CurrentCashCents,
                "carrier credited a third 125c (total +375c across the three hauls)");
            Assert.AreEqual(0, freight.FindCompany(CarrierId).FindUncollectedFreightChargeShipmentIds().Count,
                "no uncollected charge remains after all three hauls");
            Assert.AreEqual(FreightServiceAgreementStatus.Fulfilled, agreement3.Status,
                "third agreement must be fulfilled");
            Assert.IsTrue(!string.Equals(shipment.CargoLotId, shipment2.CargoLotId, StringComparison.Ordinal)
                && !string.Equals(shipment.CargoLotId, shipment3.CargoLotId, StringComparison.Ordinal)
                && !string.Equals(shipment2.CargoLotId, shipment3.CargoLotId, StringComparison.Ordinal),
                "the three shipments must carry three different cargo lots");

            // Hauls run on distinct days: the same-day-departure refusal is
            // by design and is NOT hit — each haul's recorded departure day
            // differs (12, 13, 14).
            Assert.AreEqual(3, departureDays.Count, "each haul must record a departure day");
            Assert.AreEqual(3, new HashSet<int>(departureDays).Count,
                "the three hauls must depart on distinct days, but departed on: " + string.Join(", ", departureDays));

            // Capacity free afterwards: no live shipment holds the team.
            Assert.IsNull(freight.Service.FindExclusivityConflict(null, WagonId,
                EntityId.For(EntityKind.Person, EthanId), TeamAnimals()),
                "wagon/driver/horse must be free after the third haul completes");
            // Lots 1-2 stay bound to their Completed shipments: a
            // hypothetical fourth shipment cannot re-bind either of them.
            var hypothetical = new LogisticsShipmentState { shipmentId = "freight-shipment:hypothetical-fourth" };
            Assert.IsTrue(!freight.Service.TryBindSourceLot(hypothetical, LotId,
                MillbrookFlourFreightCustodyAdapter.FlourSkuId, out string rebindReason1),
                "lot 1 must refuse re-binding to a fourth shipment");
            Assert.IsTrue(rebindReason1.Contains("already assigned"),
                "the lot 1 refusal must name the existing assignment, but got '" + rebindReason1 + "'");
            Assert.IsTrue(!freight.Service.TryBindSourceLot(hypothetical, lot2.LotId,
                MillbrookFlourFreightCustodyAdapter.FlourSkuId, out string rebindReason2),
                "lot 2 must refuse re-binding to a fourth shipment");
            Assert.IsTrue(rebindReason2.Contains("already assigned"),
                "the lot 2 refusal must name the existing assignment, but got '" + rebindReason2 + "'");
        }

        [Test]
        public void ThirdHaul_RequestIdDerivesFromLot_NeverCollides()
        {
            // Request ids derive from the lot ("freight-request:" +
            // lot.OrderId) and never collide: hauls 2-3 use the derived
            // form (haul 1's fixture request predates it and keeps its
            // "request-1" id), all three ids are distinct, and creating a
            // duplicate request for an existing lot id is refused by the
            // book's duplicate guard.
            DriveHaul1ToCompletion();
            BuildSecondHaul(out OffMapAidCargoLot lot2, out string request2Id,
                out FreightServiceAgreement agreement2, out LogisticsShipmentState shipment2);
            PrepareAndDispatchSecondHaul(agreement2, shipment2);
            SimulateSupplierPickupFor(shipment2);
            TickUntilCompleted(shipment2, "haul 2", Day2);
            BuildThirdHaul(out OffMapAidCargoLot lot3, out string request3Id,
                out FreightServiceAgreement agreement3, out LogisticsShipmentState shipment3);

            Assert.AreEqual("freight-request:" + lot2.OrderId, request2Id,
                "the haul 2 request id must derive from its lot");
            Assert.AreEqual("freight-request:" + lot3.OrderId, request3Id,
                "the haul 3 request id must derive from its lot");
            var requestIds = new HashSet<string>(StringComparer.Ordinal) { "request-1", request2Id, request3Id };
            Assert.AreEqual(3, requestIds.Count,
                "the three request ids must be distinct, but were: request-1, " + request2Id + ", " + request3Id);

            var duplicate = new FreightServiceRequest
            {
                RequestId = request3Id, RequestingBusinessId = BakeryId, CarrierBusinessId = CarrierId,
                PickupBusinessId = "millbrook_grain_co", DestinationBusinessId = BakeryId,
                CargoLotId = lot3.LotId, Quantity = 1, QuantityUnit = "lb", RequestingPersonId = 2,
                CarrierOperatorPersonId = EthanId, PickupLocationId = "millbrook-pickup",
                DestinationLocationId = "bakery-door",
                CargoSkuId = MillbrookFlourFreightCustodyAdapter.FlourSkuId
            };
            string refusal = freight.ServiceAgreements.CreateRequest(duplicate, out _);
            Assert.IsNotNull(refusal, "creating a duplicate request for an existing lot id must be refused");
            Assert.IsTrue(refusal.Contains("already exists"),
                "the duplicate refusal must name the existing request, but got '" + refusal + "'");
        }

        // ---- Priority 7 Slice 4 (2026-11-08): multi-sack shipment
        // acceptance. Tests only; no production file is touched. Proves the
        // full lifecycle for the exact lots D-F1 blocked, with the corrected
        // spec — and pins that the lot ships WHOLE (no split to dodge
        // capacity). Per the plan, the pre-Slice-1 failure is recorded from
        // Slice 0's pin instead of re-breaking the tree: the Slice 0 pin
        // (ProductionCargoSpec_SixteenSackLot_BlockedPreLoad) FAILED pre-fix
        // with the replicated production formula checking a 16-sack lot at
        // 1,161.2 kg against the authored 450 kg wagon.

        [Test]
        public void SixteenSackClampedLot_ShipsWhole_DeliversAndSettles()
        {
            // The exact lot D-F1 blocked. Millbrook holds 16 sacks (the
            // fixture supplier is drawn down from 220 lb to 160 lb by one
            // paid 6-sack order first); a forward request for 18 sacks
            // clamps to a 16-sack lot (768c debited once, the P3 Slice 4b
            // clamp); the lot ships in ONE shipment with the corrected
            // per-sack spec (72.6 kg total, inside 450 kg);
            // CommitFreightPickup commits the whole 16 sacks in that one
            // shipment; supplier pickup, return tick, unload, Bakery receipt
            // of 160 lb in ONE lot with ImportOrderId = the clamped lot id;
            // 125c settles exactly once; agreement Fulfilled; wagon free
            // afterwards.
            int payerBefore = payerRuntime.CurrentCashCents;
            int carrierBefore = carrierRuntime.CurrentCashCents;

            DriveHaul1ToCompletion();
            Assert.AreEqual(payerBefore - FreightChargeCents, payerRuntime.CurrentCashCents,
                "haul 1 freight charge debited from the bakery exactly once");
            Assert.AreEqual(carrierBefore + FreightChargeCents, carrierRuntime.CurrentCashCents,
                "haul 1 freight charge credited to the carrier exactly once");

            // Draw Millbrook down to exactly 16 sacks: 220 lb after the
            // opening order, minus one paid 6-sack order (288c). The drain
            // lot stays outstanding (uncommitted, unreceived) but covers
            // only 6 sacks, so the reorder guard (which refuses only when
            // outstanding sacks already cover the request) lets the
            // 18-sack request through to the clamp.
            int buyerBeforeDrain = buyerCash.BalanceCents;
            Assert.IsNull(supplier.TryPlaceOrder("drain-six-sack", BakeryId, "Glen Pierce", 6, buyerCash, Day2, out OffMapAidCargoLot drainLot),
                "the 6-sack draw-down order must place");
            Assert.AreEqual(6, drainLot.PackageUnits, "the draw-down lot must hold 6 sacks");
            Assert.AreEqual(288, drainLot.TotalCostCents, "the draw-down lot costs 6 x 48c = 288c");
            Assert.AreEqual(buyerBeforeDrain - 288, buyerCash.BalanceCents,
                "the draw-down order debits buyer cash exactly once");
            Assert.AreEqual(160, supplier.GetInventoryUnits(MillbrookFlourFreightCustodyAdapter.FlourSkuId),
                "Millbrook must hold exactly 16 sacks (160 lb) before the clamped reorder");

            BuildMultiSackHaul(Day2, 18, "millbrook_grain_co:flour-reorder-d13-n3", true,
                out OffMapAidCargoLot lot16, out _, out FreightServiceAgreement agreement16, out LogisticsShipmentState shipment16);
            Assert.AreEqual(16, lot16.PackageUnits, "the clamped lot must hold 16 sacks");
            Assert.AreEqual(768, lot16.TotalCostCents, "the clamped 16-sack lot costs 16 x 48c = 768c");
            Assert.AreEqual(160, lot16.InventoryUnits, "the clamped lot holds 160 lb");

            PrepareAndDispatchSecondHaul(agreement16, shipment16);
            SimulateSupplierPickupFor(shipment16);
            Assert.IsTrue(lot16.SourceCommitted, "the whole 16-sack lot must be committed to its one shipment");
            Assert.AreEqual(shipment16.ShipmentId, lot16.ShipmentId, "the lot must name its one shipment");
            TickUntilCompleted(shipment16, "16-sack haul", Day2);

            Assert.IsTrue(lot16.Received, "the clamped 16-sack lot must be marked received");
            Assert.AreEqual(2, bakery.FlourStock.Lots.Count, "the bakery must hold exactly two flour lots (haul 1 + the 16-sack haul)");
            BakeryFlourLot receipt16 = null;
            foreach (BakeryFlourLot flourLot in bakery.FlourStock.Lots)
            {
                if (string.Equals(flourLot.ImportOrderId, lot16.LotId, StringComparison.Ordinal)) receipt16 = flourLot;
            }
            Assert.IsNotNull(receipt16, "the bakery must hold a receipt for the clamped lot");
            Assert.AreEqual(160, receipt16.Units, "the clamped lot lands as 160 lb in ONE bakery lot");
            Assert.IsTrue(shipment16.deliveryApplied, "the 16-sack shipment must record delivery applied");
            Assert.AreEqual(payerBefore - 2 * FreightChargeCents, payerRuntime.CurrentCashCents,
                "payer debited a second 125c (total -250c across haul 1 and the 16-sack haul)");
            Assert.AreEqual(carrierBefore + 2 * FreightChargeCents, carrierRuntime.CurrentCashCents,
                "carrier credited a second 125c (total +250c)");
            Assert.AreEqual(0, freight.FindCompany(CarrierId).FindUncollectedFreightChargeShipmentIds().Count,
                "no uncollected charge remains after the 16-sack haul");
            Assert.AreEqual(FreightServiceAgreementStatus.Fulfilled, agreement16.Status,
                "the 16-sack agreement must be fulfilled");
            Assert.IsNull(freight.Service.FindExclusivityConflict(null, WagonId,
                EntityId.For(EntityKind.Person, EthanId), TeamAnimals()),
                "wagon/driver/horse must be free after the 16-sack haul completes");
        }

        [Test]
        public void EighteenSackLot_ShipsWhole_DeliversAndSettles()
        {
            // Millbrook holds 22 sacks after haul 1 (220 lb); an 18-sack
            // order places a full 18-sack lot (864c debited once, no clamp);
            // same whole-shipment shape as the 16-sack haul (81.6 kg total,
            // inside 450 kg); Bakery receipt of 180 lb in ONE lot; 125c
            // settles exactly once.
            int payerBefore = payerRuntime.CurrentCashCents;
            int carrierBefore = carrierRuntime.CurrentCashCents;

            DriveHaul1ToCompletion();
            Assert.AreEqual(payerBefore - FreightChargeCents, payerRuntime.CurrentCashCents,
                "haul 1 freight charge debited from the bakery exactly once");
            Assert.AreEqual(carrierBefore + FreightChargeCents, carrierRuntime.CurrentCashCents,
                "haul 1 freight charge credited to the carrier exactly once");

            BuildMultiSackHaul(Day2, 18, "millbrook_grain_co:flour-reorder-d13-n2", false,
                out OffMapAidCargoLot lot18, out _, out FreightServiceAgreement agreement18, out LogisticsShipmentState shipment18);
            Assert.AreEqual(18, lot18.PackageUnits, "the full lot must hold 18 sacks");
            Assert.AreEqual(864, lot18.TotalCostCents, "the full 18-sack lot costs 18 x 48c = 864c");
            Assert.AreEqual(180, lot18.InventoryUnits, "the full lot holds 180 lb");

            PrepareAndDispatchSecondHaul(agreement18, shipment18);
            SimulateSupplierPickupFor(shipment18);
            Assert.IsTrue(lot18.SourceCommitted, "the whole 18-sack lot must be committed to its one shipment");
            Assert.AreEqual(shipment18.ShipmentId, lot18.ShipmentId, "the lot must name its one shipment");
            TickUntilCompleted(shipment18, "18-sack haul", Day2);

            Assert.IsTrue(lot18.Received, "the full 18-sack lot must be marked received");
            Assert.AreEqual(2, bakery.FlourStock.Lots.Count, "the bakery must hold exactly two flour lots (haul 1 + the 18-sack haul)");
            BakeryFlourLot receipt18 = null;
            foreach (BakeryFlourLot flourLot in bakery.FlourStock.Lots)
            {
                if (string.Equals(flourLot.ImportOrderId, lot18.LotId, StringComparison.Ordinal)) receipt18 = flourLot;
            }
            Assert.IsNotNull(receipt18, "the bakery must hold a receipt for the 18-sack lot");
            Assert.AreEqual(180, receipt18.Units, "the 18-sack lot lands as 180 lb in ONE bakery lot");
            Assert.IsTrue(shipment18.deliveryApplied, "the 18-sack shipment must record delivery applied");
            Assert.AreEqual(payerBefore - 2 * FreightChargeCents, payerRuntime.CurrentCashCents,
                "payer debited a second 125c (total -250c across haul 1 and the 18-sack haul)");
            Assert.AreEqual(carrierBefore + 2 * FreightChargeCents, carrierRuntime.CurrentCashCents,
                "carrier credited a second 125c (total +250c)");
            Assert.AreEqual(0, freight.FindCompany(CarrierId).FindUncollectedFreightChargeShipmentIds().Count,
                "no uncollected charge remains after the 18-sack haul");
            Assert.AreEqual(FreightServiceAgreementStatus.Fulfilled, agreement18.Status,
                "the 18-sack agreement must be fulfilled");
            Assert.IsNull(freight.Service.FindExclusivityConflict(null, WagonId,
                EntityId.For(EntityKind.Person, EthanId), TeamAnimals()),
                "wagon/driver/horse must be free after the 18-sack haul completes");
        }

        [Test]
        public void MultiSackLot_SplitCommitRefused()
        {
            // Complementary pin: CommitFreightPickup with fewer than the
            // lot's whole PackageUnits is refused and the lot stays
            // uncommitted (the whole-lot rule; no partial pickup to dodge
            // capacity). Must pass before and after Slice 1 — it guards the
            // repair against over-correction. Two levels are pinned: the
            // supplier authority itself (OffMapAidBusinessRuntime.
            // CommitFreightPickup refuses a partial quantity) and the
            // custody adapter, whose ResolveLot refuses a quantity that does
            // not match the shipment's planned whole-lot quantity before the
            // authority is even reached. The whole commit afterwards proves
            // the refusals left nothing half-committed.
            Assert.IsNull(supplier.TryPlaceOrder("split-sixteen-sack", BakeryId, "Glen Pierce", 16, buyerCash, Day, out OffMapAidCargoLot lot16),
                "the 16-sack split-test lot must place");
            Assert.AreEqual(16, lot16.PackageUnits, "the split-test lot must hold 16 sacks");
            Assert.AreEqual(768, lot16.TotalCostCents, "the split-test lot costs 16 x 48c = 768c");

            // Supplier authority level: a partial quantity is refused.
            string supplierRefusal = supplier.CommitFreightPickup(lot16.LotId, "freight-shipment:split-test", 15);
            Assert.IsNotNull(supplierRefusal, "committing 15 of a 16-sack lot must be refused");
            Assert.IsTrue(supplierRefusal.Contains("does not match"),
                "the partial-commit refusal must name the quantity mismatch, but got '" + supplierRefusal + "'");
            Assert.IsTrue(!lot16.SourceCommitted, "the lot must stay uncommitted after the refused partial commit");
            Assert.IsTrue(string.IsNullOrEmpty(lot16.ShipmentId), "the lot must name no shipment after the refused partial commit");

            // Adapter level: bind the whole lot to a fresh-service shipment
            // (isolated from the fixture shipment's wagon hold), then a
            // 15-sack commit through the adapter is refused the same way.
            var service = new FreightShipmentService();
            service.RegisterWagon(wagon);
            LogisticsShipmentState bound = service.CreateShipment("freight-shipment:split-bound", "millbrook_grain_co", BakeryId,
                lot16.PackageUnits, ProductionCargoSpec(lot16), WagonId, hooks, out string createReason);
            Assert.IsNotNull(bound, "the split-test shipment must create: " + createReason);
            Assert.IsTrue(!bound.IsPreLoadBlocked, "the split-test shipment must not be pre-load blocked: " + bound.BlockedReason);
            Assert.IsTrue(service.TryBindSourceLot(bound, lot16.LotId, MillbrookFlourFreightCustodyAdapter.FlourSkuId, out string bindReason),
                "the 16-sack lot must bind to its split-test shipment: " + bindReason);
            Assert.IsTrue(!adapter.CommitSourceLot(bound, 15, out string adapterRefusal),
                "committing 15 of a 16-sack lot through the adapter must be refused");
            Assert.IsTrue(adapterRefusal.Contains("does not match"),
                "the adapter partial-commit refusal must name the quantity mismatch, but got '" + adapterRefusal + "'");
            Assert.IsTrue(!lot16.SourceCommitted, "the lot must stay uncommitted after the refused adapter partial commit");

            // The whole commit still succeeds afterwards — the refusals
            // left nothing half-committed.
            Assert.IsTrue(adapter.CommitSourceLot(bound, 16, out string wholeReason),
                "the whole 16-sack commit must succeed after the refused partials: " + wholeReason);
            Assert.IsTrue(lot16.SourceCommitted, "the whole commit must mark the lot committed");
            Assert.AreEqual(bound.ShipmentId, lot16.ShipmentId, "the whole commit must name the one shipment");
        }

        [Test]
        public void VolumeBoundary_TwentySackLot_Passes_TwentyOneSackLot_BlocksOnVolume()
        {
            // Pins the CURRENT volume formula's boundary with the corrected
            // mass: a 20-sack lot passes volume (4.00 m3, within the +0.0001
            // tolerance) and a 21-sack lot blocks on volume (4.41 m3). This
            // test pins the formula as written; it does NOT bless the squared
            // volume formula as correct — its per-sack figure is unauthored
            // (plan section 4 / B-P7-1, an owner decision). The mass leg is
            // clean in both cases (20 sacks = 90.7 kg, 21 sacks = 95.3 kg,
            // both far inside 450 kg), so the 21-sack block names volume.
            OffMapAidCargoLot lot20 = PlaceLot(20, "volume-twenty-sack");
            LogisticsShipmentState created20 = CreateWithProductionSpec("freight-shipment:volume-20sack", lot20);
            Assert.IsTrue(!created20.IsPreLoadBlocked,
                "a 20-sack lot must pass volume (4.00 m3 at capacity), but was blocked: " + created20.BlockedReason);
            Assert.AreEqual(LogisticsShipmentStatus.Planned, created20.Status, "a 20-sack lot must be Planned");
            Assert.AreEqual(20, created20.PlannedQuantityUnits, "a 20-sack lot must plan 20 units");

            OffMapAidCargoLot lot21 = PlaceLot(21, "volume-twenty-one-sack");
            LogisticsShipmentState created21 = CreateWithProductionSpec("freight-shipment:volume-21sack", lot21);
            Assert.IsTrue(created21.IsPreLoadBlocked,
                "a 21-sack lot must block on volume (4.41 m3 over the 4 m3 wagon)");
            Assert.IsTrue(created21.BlockedReason.Contains("volume"),
                "the 21-sack block must name volume, but got '" + created21.BlockedReason + "'");
            Assert.IsTrue(created21.BlockedReason.Contains("4.41"),
                "the 21-sack block must name the 4.41 m3 checked volume, but got '" + created21.BlockedReason + "'");
            Assert.IsTrue(!created21.BlockedReason.Contains("mass"),
                "the 21-sack block must be a volume block, not a mass block, but got '" + created21.BlockedReason + "'");
        }

        // ---- Priority 7 Slice 5 (2026-11-08): cargo custody + physical
        // journey authority acceptance. Tests only; no production file is
        // touched. Pins the custody chain for a multi-sack haul (goods move
        // with the wagon — never teleport, never duplicate, never lost), the
        // S10 off-map hold, and the conservation invariant. Expected to
        // PASS: a failure names the leg that fails and the slice stops and
        // reports instead of repairing inside the acceptance.

        private int CountBakeryReceiptsFor(string sourceLotId)
        {
            int count = 0;
            foreach (BakeryFlourLot flourLot in bakery.FlourStock.Lots)
            {
                if (string.Equals(flourLot.ImportOrderId, sourceLotId, StringComparison.Ordinal)) count++;
            }
            return count;
        }

        private BakeryFlourLot FindBakeryReceiptFor(string sourceLotId)
        {
            foreach (BakeryFlourLot flourLot in bakery.FlourStock.Lots)
            {
                if (string.Equals(flourLot.ImportOrderId, sourceLotId, StringComparison.Ordinal)) return flourLot;
            }
            return null;
        }

        // Drives haul 1 to completion, draws Millbrook down to exactly 16
        // sacks, and builds the clamped 16-sack haul (the Slice 4 shape).
        // The caller owns prepare/dispatch/pickup.
        private void BuildSixteenSackHaul(out OffMapAidCargoLot lot16,
            out FreightServiceAgreement agreement16, out LogisticsShipmentState shipment16)
        {
            DriveHaul1ToCompletion();

            int buyerBeforeDrain = buyerCash.BalanceCents;
            Assert.IsNull(supplier.TryPlaceOrder("drain-six-sack", BakeryId, "Glen Pierce", 6, buyerCash, Day2, out OffMapAidCargoLot drainLot),
                "the 6-sack draw-down order must place");
            Assert.AreEqual(6, drainLot.PackageUnits, "the draw-down lot must hold 6 sacks");
            Assert.AreEqual(288, drainLot.TotalCostCents, "the draw-down lot costs 6 x 48c = 288c");
            Assert.AreEqual(buyerBeforeDrain - 288, buyerCash.BalanceCents,
                "the draw-down order debits buyer cash exactly once");
            Assert.AreEqual(160, supplier.GetInventoryUnits(MillbrookFlourFreightCustodyAdapter.FlourSkuId),
                "Millbrook must hold exactly 16 sacks (160 lb) before the clamped reorder");

            BuildMultiSackHaul(Day2, 18, "millbrook_grain_co:flour-reorder-d13-n3", true,
                out lot16, out _, out agreement16, out shipment16);
            Assert.AreEqual(16, lot16.PackageUnits, "the clamped lot must hold 16 sacks");
            Assert.AreEqual(768, lot16.TotalCostCents, "the clamped 16-sack lot costs 16 x 48c = 768c");
            Assert.AreEqual(160, lot16.InventoryUnits, "the clamped lot holds 160 lb");
        }

        [Test]
        public void Custody_MultiSackHaul_GoodsMoveWithWagon_NeverTeleport()
        {
            // The custody chain for a 16-sack haul, leg by leg: after
            // pickup the goods are in wagon custody (physicalLocationId
            // "wagon:opening-freight-wagon", load applied, source lot
            // debited, 16 units remaining, lot committed to this shipment);
            // after the return leg the progress has advanced and the lot is
            // still unreceived; after unload the Bakery holds one lot with
            // the same ImportOrderId and the shipment sits at the Bakery.
            // At no leg does the lot exist in two places: the supplier lot
            // stays unreceived until Bakery receipt, and the Bakery receipt
            // count for this lot goes 0 -> 1 with the same id.
            BuildSixteenSackHaul(out OffMapAidCargoLot lot16,
                out FreightServiceAgreement agreement16, out LogisticsShipmentState shipment16);
            PrepareAndDispatchSecondHaul(agreement16, shipment16);

            // Pre-pickup leg: the lot exists only at the supplier.
            Assert.IsTrue(!lot16.Received, "the lot must be unreceived before pickup");
            Assert.AreEqual(0, CountBakeryReceiptsFor(lot16.LotId),
                "no Bakery receipt for the 16-sack lot may exist before pickup");
            Assert.AreEqual(1, bakery.FlourStock.Lots.Count, "only haul 1's receipt may exist before pickup");

            // Pickup leg: the goods move into wagon custody.
            SimulateSupplierPickupFor(shipment16);
            Assert.AreEqual("wagon:" + WagonId, shipment16.physicalLocationId,
                "after pickup the goods must be with the wagon, but the shipment sits at '" + shipment16.physicalLocationId + "'");
            Assert.IsTrue(shipment16.loadApplied, "the pickup must apply the load");
            Assert.IsTrue(shipment16.sourceLotDebitApplied, "the pickup must debit the source lot");
            Assert.AreEqual(16, shipment16.RemainingQuantityUnits,
                "the wagon must hold all 16 sacks after pickup");
            Assert.IsTrue(lot16.SourceCommitted, "the lot must be committed after pickup");
            Assert.AreEqual(shipment16.ShipmentId, lot16.ShipmentId,
                "the lot must name its one shipment after pickup");
            Assert.IsTrue(!lot16.Received, "the lot must stay unreceived while in wagon custody");
            Assert.AreEqual(0, CountBakeryReceiptsFor(lot16.LotId),
                "no Bakery receipt for the 16-sack lot may exist while it is in wagon custody");
            Assert.IsTrue(freight.Service.VerifyConservation(shipment16, 0, out string conservationAtPickup),
                "conservation must hold at pickup (0 delivered / 16 remaining): " + conservationAtPickup);

            // Return leg, tick 1: dispatch (Planned -> InTransit). The
            // goods stay with the wagon; nothing has teleported ahead.
            hooks.DayIndex = Day2;
            hooks.IsVehicleOffMap = _ => false;
            Tick();
            Assert.AreEqual(LogisticsShipmentStatus.InTransit, shipment16.Status,
                "the picked-up shipment must dispatch on the return tick");
            Assert.AreEqual("wagon:" + WagonId, shipment16.physicalLocationId,
                "the goods must stay with the wagon on dispatch");
            Assert.AreEqual(0f, shipment16.physicalProgress01,
                "no transit progress has accrued at dispatch");
            Assert.IsTrue(!lot16.Received, "the lot must stay unreceived at dispatch");
            Assert.AreEqual(0, CountBakeryReceiptsFor(lot16.LotId),
                "no Bakery receipt for the 16-sack lot may exist at dispatch");

            // Return leg, tick 2: transit advances to arrival. Progress
            // moves, the lot is still unreceived, still no Bakery receipt.
            Tick();
            Assert.IsTrue(shipment16.physicalProgress01 > 0f,
                "physicalProgress01 must advance on the return leg, but stayed 0");
            Assert.AreEqual(1f, shipment16.physicalProgress01,
                "transit progress must reach 1 on arrival");
            Assert.AreEqual(LogisticsShipmentStatus.Arrived, shipment16.Status,
                "the shipment must arrive on the return leg");
            Assert.IsTrue(!lot16.Received,
                "the lot must still be unreceived at arrival — it has not teleported ahead of the wagon");
            Assert.AreEqual(0, CountBakeryReceiptsFor(lot16.LotId),
                "no Bakery receipt for the 16-sack lot may exist at arrival");
            Assert.IsTrue(freight.Service.VerifyConservation(shipment16, 0, out string conservationAtArrival),
                "conservation must hold at arrival (0 delivered / 16 remaining): " + conservationAtArrival);

            // Delivery leg: unload to completion. The lot lands once, in
            // one Bakery lot, and the shipment sits at the Bakery.
            TickUntilCompleted(shipment16, "16-sack custody haul", Day2);
            Assert.IsTrue(lot16.Received, "the 16-sack lot must be marked received after unload");
            Assert.AreEqual(1, CountBakeryReceiptsFor(lot16.LotId),
                "the Bakery receipt count for this lot must go 0 -> 1 with the same id");
            BakeryFlourLot receipt16 = FindBakeryReceiptFor(lot16.LotId);
            Assert.IsNotNull(receipt16, "the Bakery must hold a receipt for the 16-sack lot");
            Assert.AreEqual(160, receipt16.Units, "the 16-sack lot lands as 160 lb in ONE bakery lot");
            Assert.AreEqual(lot16.LotId, receipt16.ImportOrderId,
                "the Bakery receipt must carry the same lot id as the supplier lot");
            Assert.AreEqual(BakeryId, shipment16.physicalLocationId,
                "after unload the goods must be at the Bakery, but the shipment sits at '" + shipment16.physicalLocationId + "'");
            Assert.IsTrue(shipment16.deliveryApplied, "the 16-sack shipment must record delivery applied");
            Assert.IsTrue(freight.Service.VerifyConservation(shipment16, 16, out string conservationAtDelivery),
                "conservation must hold at delivery (16 delivered / 0 remaining): " + conservationAtDelivery);
        }

        [Test]
        public void JourneyAuthority_OffMapHold_SkipsShipment_AndLiftsOnReturn()
        {
            // The S10 off-map hold, pinned for a loaded haul: while the
            // wagon is off-map, AdvanceTick advances nothing for the loaded
            // shipment — it stays InTransit with cargo intact and no transit
            // progress accrues. With the hold lifted, the very same tick
            // advances it to Arrived. The journey authority, not the
            // calendar, governs the return.
            BindAndAuthorize();
            SimulateSupplierPickup();
            hooks.DayIndex = Day;
            hooks.IsVehicleOffMap = _ => false;
            Tick();
            Assert.AreEqual(LogisticsShipmentStatus.InTransit, shipment.Status,
                "setup: the loaded shipment must dispatch to InTransit");
            Assert.AreEqual(0f, shipment.physicalProgress01,
                "setup: no transit progress has accrued at dispatch");
            Assert.AreEqual(1, shipment.RemainingQuantityUnits,
                "setup: the 1-sack cargo must be intact at dispatch");
            Assert.IsTrue(!lot.Received, "setup: the lot must be unreceived at dispatch");

            // Hold: the wagon is off-map, so the tick must skip it.
            hooks.IsVehicleOffMap = _ => true;
            diag.Clear();
            int held = Tick();
            Assert.AreEqual(0, held,
                "the S10 hold must advance nothing while the wagon is off-map");
            Assert.AreEqual(LogisticsShipmentStatus.InTransit, shipment.Status,
                "the held shipment must stay InTransit");
            Assert.AreEqual(0f, shipment.physicalProgress01,
                "the held shipment must accrue no transit progress");
            Assert.AreEqual(1, shipment.RemainingQuantityUnits,
                "the held shipment must keep its cargo intact");
            Assert.IsTrue(shipment.loadApplied, "the held shipment must keep its applied load");
            Assert.IsTrue(lot.SourceCommitted, "the held lot must stay committed to this shipment");
            Assert.AreEqual(ShipmentId, lot.ShipmentId,
                "the held lot must still name its one shipment");
            Assert.IsTrue(!lot.Received, "the held lot must stay unreceived");

            // Lift: the same tick now advances the shipment.
            hooks.IsVehicleOffMap = _ => false;
            int advanced = Tick();
            Assert.IsTrue(advanced > 0,
                "lifting the hold must advance the shipment on the same tick");
            Assert.AreEqual(LogisticsShipmentStatus.Arrived, shipment.Status,
                "with the hold lifted the same tick must advance the shipment to Arrived");
            Assert.AreEqual(1f, shipment.physicalProgress01,
                "transit progress must reach 1 on arrival");
            Assert.AreEqual(1, shipment.RemainingQuantityUnits,
                "the cargo must still be intact at arrival");
            Assert.IsTrue(!lot.Received, "the lot must still be unreceived at arrival");
        }

        [Test]
        public void Custody_DoubleCommitRefused_AndConservationHolds()
        {
            // A lot ships whole to one shipment only: committing the same
            // 16-sack lot to a second shipment is refused and leaves the
            // first commitment untouched. Conservation holds at every leg —
            // 0 delivered / 16 remaining in transit, 16 delivered / 0
            // remaining after delivery — and a deliberately wrong delivered
            // count is refused with the mismatch named.
            BuildSixteenSackHaul(out OffMapAidCargoLot lot16,
                out FreightServiceAgreement agreement16, out LogisticsShipmentState shipment16);

            // Pickup leg: conservation at 0/16.
            SimulateSupplierPickupFor(shipment16);
            Assert.IsTrue(freight.Service.VerifyConservation(shipment16, 0, out string conservationAtPickup),
                "conservation must hold at pickup (0 delivered / 16 remaining): " + conservationAtPickup);
            Assert.AreEqual(0, shipment16.DeliveredQuantityUnits,
                "nothing is delivered at pickup");
            Assert.AreEqual(16, shipment16.RemainingQuantityUnits,
                "all 16 sacks remain in wagon custody at pickup");

            // Double commit: a second shipment naming the same lot is
            // refused. The second shipment is built on its own service
            // (isolated from the fixture shipment's wagon hold) and bound
            // to the lot, so the refusal comes from the commit authority
            // itself, not from the bind or the capacity gate.
            var secondService = new FreightShipmentService();
            secondService.RegisterWagon(wagon);
            LogisticsShipmentState second = secondService.CreateShipment(
                "freight-shipment:double-commit-second", "millbrook_grain_co", BakeryId,
                lot16.PackageUnits, ProductionCargoSpec(lot16), WagonId, hooks, out string secondReason);
            Assert.IsNotNull(second,
                "the second shipment must create on its own service: " + secondReason);
            Assert.IsTrue(secondService.TryBindSourceLot(second, lot16.LotId,
                MillbrookFlourFreightCustodyAdapter.FlourSkuId, out string bindReason),
                "the 16-sack lot must bind to the second shipment for the double-commit probe: " + bindReason);
            Assert.IsTrue(!adapter.CommitSourceLot(second, 16, out string doubleReason),
                "committing the same lot to a second shipment must be refused");
            Assert.IsTrue(doubleReason.Contains("already committed to another shipment"),
                "the double-commit refusal must name the existing commitment, but got '" + doubleReason + "'");
            Assert.IsTrue(lot16.SourceCommitted,
                "the lot must stay committed after the refused double commit");
            Assert.AreEqual(shipment16.ShipmentId, lot16.ShipmentId,
                "the lot must still name its first shipment after the refused double commit");
            Assert.IsTrue(!lot16.Received,
                "the lot must stay unreceived after the refused double commit");
            Assert.IsTrue(freight.Service.VerifyConservation(shipment16, 0, out string conservationAfterRefusal),
                "conservation must still hold after the refused double commit (0 delivered / 16 remaining): "
                + conservationAfterRefusal);

            // Delivery leg: conservation at 16/0, and a wrong count fails.
            PrepareAndDispatchSecondHaul(agreement16, shipment16);
            TickUntilCompleted(shipment16, "16-sack custody haul", Day2);
            Assert.IsTrue(lot16.Received, "the 16-sack lot must be marked received after unload");
            Assert.AreEqual(16, shipment16.DeliveredQuantityUnits,
                "all 16 sacks must be delivered");
            Assert.AreEqual(0, shipment16.RemainingQuantityUnits,
                "nothing must remain in wagon custody after delivery");
            Assert.IsTrue(freight.Service.VerifyConservation(shipment16, 16, out string conservationAtDelivery),
                "conservation must hold at delivery (16 delivered / 0 remaining): " + conservationAtDelivery);
            Assert.IsTrue(!freight.Service.VerifyConservation(shipment16, 15, out string wrongReason),
                "a deliberately wrong delivered count (15 of 16) must be refused");
            Assert.IsTrue(wrongReason.Contains("planned 16") && wrongReason.Contains("delivered 15"),
                "the wrong-count refusal must name the mismatch, but got '" + wrongReason + "'");
        }

        // ---- Priority 7 Slice 6 (2026-11-08): 125c settlement
        // exactly-once acceptance. Tests only; no production file is
        // touched. Checked against FreightDeliveryPaymentAuditTests first:
        // that file pins the normal path (one haul), the fallback path,
        // the legacy-collect fence, duplicate receipts, refused delivery,
        // mid-unload save/restore, and same-day departure — none of it is
        // duplicated here. What was missing, and is pinned here: the
        // three-haul settlement statement (375c), the unresolvable-payer
        // retry (R-04), and the freight_service revenue category with
        // per-shipment provenance. All three tests live in this file (not
        // FreightDeliveryPaymentAuditTests.cs) because the three-haul run
        // and its helpers live here.

        [Test]
        public void Settlement_ThreeHauls_PayerMinus375CarrierPlus375_NoDuplicate()
        {
            // The Slice 3 three-haul run's money legs asserted as one
            // settlement statement: payer 20000c -> 19625c, carrier 0c ->
            // 375c, each haul's freightRevenueCollected true exactly once,
            // no uncollected charge remains, and a second AdvanceTick +
            // second CollectAndPostUncollectedRevenue pass posts 0c more
            // (the re-scan is a no-op). Expected to PASS.
            int payerBefore = payerRuntime.CurrentCashCents;
            int carrierBefore = carrierRuntime.CurrentCashCents;

            DriveHaul1ToCompletion();
            Assert.IsTrue(shipment.FreightRevenueCollected,
                "haul 1 charge must be collected exactly once");
            Assert.AreEqual(payerBefore - FreightChargeCents, payerRuntime.CurrentCashCents,
                "haul 1: payer debited 125c once");
            Assert.AreEqual(carrierBefore + FreightChargeCents, carrierRuntime.CurrentCashCents,
                "haul 1: carrier credited 125c once");

            BuildSecondHaul(out OffMapAidCargoLot lot2, out string request2Id,
                out FreightServiceAgreement agreement2, out LogisticsShipmentState shipment2);
            PrepareAndDispatchSecondHaul(agreement2, shipment2);
            SimulateSupplierPickupFor(shipment2);
            TickUntilCompleted(shipment2, "haul 2", Day2);
            Assert.IsTrue(shipment2.FreightRevenueCollected,
                "haul 2 charge must be collected exactly once");
            Assert.AreEqual(payerBefore - 2 * FreightChargeCents, payerRuntime.CurrentCashCents,
                "payer debited a second 125c (total -250c across both hauls)");
            Assert.AreEqual(carrierBefore + 2 * FreightChargeCents, carrierRuntime.CurrentCashCents,
                "carrier credited a second 125c (total +250c across both hauls)");

            BuildThirdHaul(out OffMapAidCargoLot lot3, out string request3Id,
                out FreightServiceAgreement agreement3, out LogisticsShipmentState shipment3);
            PrepareAndDispatchThirdHaul(agreement3, shipment3, shipment2.ShipmentId);
            SimulateSupplierPickupFor(shipment3);
            TickUntilCompleted(shipment3, "haul 3", Day3);
            Assert.IsTrue(shipment3.FreightRevenueCollected,
                "haul 3 charge must be collected exactly once");

            // The settlement statement: payer 20000c -> 19625c, carrier
            // 0c -> 375c across the three hauls.
            Assert.AreEqual(19625, payerRuntime.CurrentCashCents,
                "payer 20000c -> 19625c across the three hauls");
            Assert.AreEqual(payerBefore - 3 * FreightChargeCents, payerRuntime.CurrentCashCents,
                "payer debited a third 125c (total -375c across the three hauls)");
            Assert.AreEqual(375, carrierRuntime.CurrentCashCents,
                "carrier 0c -> 375c across the three hauls");
            Assert.AreEqual(carrierBefore + 3 * FreightChargeCents, carrierRuntime.CurrentCashCents,
                "carrier credited a third 125c (total +375c across the three hauls)");
            Assert.AreEqual(0, freight.FindCompany(CarrierId).FindUncollectedFreightChargeShipmentIds().Count,
                "no uncollected charge remains after all three hauls");

            // A second AdvanceTick + a second collection pass posts 0c
            // more: the collected flags make the re-scan a no-op.
            Tick();
            int reposted = freight.FindCompany(CarrierId).CollectAndPostUncollectedRevenue(
                carrierRuntime, freight.ResolvePayerRuntime, diag);
            Assert.AreEqual(0, reposted, "a second collection pass must post 0c more");
            Assert.AreEqual(19625, payerRuntime.CurrentCashCents, "no second debit on the re-scan");
            Assert.AreEqual(375, carrierRuntime.CurrentCashCents, "no second credit on the re-scan");
            Assert.IsTrue(shipment.FreightRevenueCollected && shipment2.FreightRevenueCollected
                && shipment3.FreightRevenueCollected,
                "all three hauls stay collected exactly once after the re-scan");
        }

        [Test]
        public void Settlement_UnresolvablePayer_StaysUncollected_RetriedNeverPosted()
        {
            // R-04: a haul whose payer runtime is NOT registered with the
            // driver (no RegisterBusinessRuntime, no resolver entry)
            // completes and delivers, but the charge stays uncollected —
            // no phantom carrier credit — the agreement stays Accepted,
            // and a later tick after registering the payer runtime collects
            // exactly 125c once and fulfils the agreement. Expected to
            // PASS. The fixture driver built by SetUp (payer registered)
            // is replaced here by one built without the payer; it never
            // ticks, so its same-id shipment never binds the shared
            // wagon/horse or touches the lot.
            freight = BuildFixtureDriver(registerPayer: false);
            int payerBefore = payerRuntime.CurrentCashCents;
            int carrierBefore = carrierRuntime.CurrentCashCents;

            DriveHaul1ToCompletion(expectFulfilled: false);

            Assert.IsTrue(shipment.deliveryApplied,
                "the haul still delivers with an unresolvable payer");
            Assert.IsTrue(lot.Received, "the paid lot must still be marked received");
            Assert.IsTrue(!shipment.FreightRevenueCollected,
                "the charge must stay uncollected while the payer cannot be resolved");
            List<string> uncollected = freight.FindCompany(CarrierId).FindUncollectedFreightChargeShipmentIds();
            Assert.AreEqual(1, uncollected.Count,
                "exactly the one completed shipment must be uncollected");
            Assert.AreEqual(ShipmentId, uncollected[0],
                "the uncollected charge must be this haul's shipment");
            Assert.AreEqual(payerBefore, payerRuntime.CurrentCashCents,
                "no debit while the payer is unresolvable");
            Assert.AreEqual(carrierBefore, carrierRuntime.CurrentCashCents,
                "no carrier credit while the payer is unresolvable — no phantom revenue");
            Assert.AreEqual(FreightServiceAgreementStatus.Accepted, agreement.Status,
                "the agreement stays Accepted, NOT Fulfilled, while its charge is uncollected (AUD-338)");

            // Retries without a payer still collect nothing.
            Tick();
            Tick();
            Assert.IsTrue(!shipment.FreightRevenueCollected,
                "retries without a payer must still collect nothing");
            Assert.AreEqual(carrierBefore, carrierRuntime.CurrentCashCents,
                "still no carrier credit on retry without a payer");

            // Register the payer runtime; the next tick collects exactly
            // 125c once and fulfils the agreement.
            freight.RegisterBusinessRuntime(BakeryId, payerRuntime);
            Tick();
            Assert.IsTrue(shipment.FreightRevenueCollected,
                "the charge must collect once the payer resolves");
            Assert.AreEqual(payerBefore - FreightChargeCents, payerRuntime.CurrentCashCents,
                "the payer is debited exactly 125c on the retry");
            Assert.AreEqual(carrierBefore + FreightChargeCents, carrierRuntime.CurrentCashCents,
                "the carrier is credited exactly 125c on the retry");
            Assert.AreEqual(0, freight.FindCompany(CarrierId).FindUncollectedFreightChargeShipmentIds().Count,
                "no uncollected charge remains after the retry");
            Assert.AreEqual(FreightServiceAgreementStatus.Fulfilled, agreement.Status,
                "the agreement fulfils once its charge is collected");

            // And a further collection pass posts nothing more.
            int reposted = freight.FindCompany(CarrierId).CollectAndPostUncollectedRevenue(
                carrierRuntime, freight.ResolvePayerRuntime, diag);
            Assert.AreEqual(0, reposted, "no second settlement after the retry collection");
            Assert.AreEqual(payerBefore - FreightChargeCents, payerRuntime.CurrentCashCents,
                "no second debit after the retry collection");
            Assert.AreEqual(carrierBefore + FreightChargeCents, carrierRuntime.CurrentCashCents,
                "no second credit after the retry collection");
        }

        [Test]
        public void Settlement_CarrierRevenueCategory_IsFreightService()
        {
            // The posted revenue lands under category "freight_service"
            // (FreightRevenueCategoryId) with provenance naming the
            // shipment id, and the payer debit and carrier credit are
            // equal per shipment — asserted as the pair, not just the
            // totals. Expected to PASS. The category stock entry is seeded
            // first: a bare BusinessRuntimeState carries no category rows,
            // and RecordDailyServiceRevenue records the category leg only
            // when the row exists (its cash leg always moves).
            CategoryStockState freightService = carrierRuntime.EnsureCategoryStock(
                FreightCompanyRuntime.FreightRevenueCategoryId, 0, 0);
            Assert.IsNotNull(freightService,
                "the freight_service category row must exist to observe the category leg");
            Assert.AreEqual(0, freightService.WeekToDateRevenueCents,
                "no freight_service revenue before any haul");

            int payerBefore = payerRuntime.CurrentCashCents;
            int carrierBefore = carrierRuntime.CurrentCashCents;

            DriveHaul1ToCompletion();
            AssertSettlementPair(1, payerBefore, carrierBefore);
            AssertCategoryRevenue(1 * FreightChargeCents, "after haul 1");
            AssertProvenanceLogged(ShipmentId);

            BuildSecondHaul(out OffMapAidCargoLot lot2, out string request2Id,
                out FreightServiceAgreement agreement2, out LogisticsShipmentState shipment2);
            PrepareAndDispatchSecondHaul(agreement2, shipment2);
            SimulateSupplierPickupFor(shipment2);
            TickUntilCompleted(shipment2, "haul 2", Day2);
            AssertSettlementPair(2, payerBefore, carrierBefore);
            AssertCategoryRevenue(2 * FreightChargeCents, "after haul 2");
            AssertProvenanceLogged(shipment2.ShipmentId);

            BuildThirdHaul(out OffMapAidCargoLot lot3, out string request3Id,
                out FreightServiceAgreement agreement3, out LogisticsShipmentState shipment3);
            PrepareAndDispatchThirdHaul(agreement3, shipment3, shipment2.ShipmentId);
            SimulateSupplierPickupFor(shipment3);
            TickUntilCompleted(shipment3, "haul 3", Day3);
            AssertSettlementPair(3, payerBefore, carrierBefore);
            AssertCategoryRevenue(3 * FreightChargeCents, "after haul 3");
            AssertProvenanceLogged(shipment3.ShipmentId);
        }

        // Per-shipment pair: after `hauls` hauls, the payer debit and the
        // carrier credit are equal (125c each), asserted against the same
        // starting balances — not just the final totals.
        private void AssertSettlementPair(int hauls, int payerBefore, int carrierBefore)
        {
            Assert.AreEqual(payerBefore - hauls * FreightChargeCents, payerRuntime.CurrentCashCents,
                "payer debit after " + hauls + " haul(s) must be " + (hauls * FreightChargeCents) + "c");
            Assert.AreEqual(carrierBefore + hauls * FreightChargeCents, carrierRuntime.CurrentCashCents,
                "carrier credit after " + hauls + " haul(s) must be " + (hauls * FreightChargeCents) + "c");
        }

        // The freight_service category leg on the carrier runtime: the
        // revenue posted through RecordDailyServiceRevenue accumulates in
        // the category row's daily and week-to-date totals.
        private void AssertCategoryRevenue(int expectedCents, string when)
        {
            CategoryStockState freightService = carrierRuntime.GetCategoryStock(
                FreightCompanyRuntime.FreightRevenueCategoryId);
            Assert.IsNotNull(freightService, "the freight_service category row must exist " + when);
            Assert.AreEqual(expectedCents, freightService.WeekToDateRevenueCents,
                "freight_service category revenue " + when + " must be " + expectedCents + "c");
            Assert.AreEqual(expectedCents, freightService.LastDailyRevenueCents,
                "freight_service daily category revenue " + when + " must be " + expectedCents + "c");
        }

        // The settlement diagnostic CollectAndPostUncollectedRevenue writes
        // names the shipment id as the provenance of the posted revenue.
        private void AssertProvenanceLogged(string shipmentId)
        {
            foreach (string line in diag)
            {
                if (line != null && line.Contains("freight revenue posted") && line.Contains(shipmentId))
                    return;
            }
            Assert.Fail("no settlement diagnostic names shipment '" + shipmentId
                + "' with its freight revenue posting; diagnostics: " + string.Join("; ", diag));
        }

        // ---- Priority 7 Slice 7 (2026-11-08): wagon reservation release
        // + refused delivery + partial remainder + retry acceptance. Tests
        // only; no production file is touched. Expected to PASS: release,
        // refusal, and retry are pinned for two hauls by
        // FreightRepeatabilityTests (CapacityRelease_*, RefusedDelivery_*),
        // and this slice extends those pins to the third haul and to a
        // Partial remainder.

        [Test]
        public void Release_AfterThirdHaul_TeamRebindable_ReservationsReleased()
        {
            // After the Slice 3 third haul completes: transport unbound,
            // no exclusivity conflict on wagon/driver/horse, double
            // ReleaseShipmentReservations is a no-op, the team refuses
            // rebind before the terminal sweep and rebinds after it (the
            // same two halves as the haul-1 pin, now after haul 3).
            DriveHaul1ToCompletion();
            BuildSecondHaul(out OffMapAidCargoLot lot2, out string request2Id,
                out FreightServiceAgreement agreement2, out LogisticsShipmentState shipment2);
            PrepareAndDispatchSecondHaul(agreement2, shipment2);
            SimulateSupplierPickupFor(shipment2);
            TickUntilCompleted(shipment2, "haul 2", Day2);
            Assert.IsTrue(lot2.Received, "the second paid Millbrook lot must be marked received");

            BuildThirdHaul(out OffMapAidCargoLot lot3, out string request3Id,
                out FreightServiceAgreement agreement3, out LogisticsShipmentState shipment3);
            PrepareAndDispatchThirdHaul(agreement3, shipment3, shipment2.ShipmentId);
            SimulateSupplierPickupFor(shipment3);
            TickUntilCompleted(shipment3, "haul 3", Day3);
            Assert.IsTrue(lot3.Received, "the third paid Millbrook lot must be marked received");
            Assert.AreEqual(FreightServiceAgreementStatus.Fulfilled, agreement3.Status,
                "third agreement must be fulfilled");

            Assert.IsFalse(shipment.physicalTransportBound, "completion must unbind haul 1 transport");
            Assert.IsFalse(shipment2.physicalTransportBound, "completion must unbind haul 2 transport");
            Assert.IsFalse(shipment3.physicalTransportBound, "completion must unbind haul 3 transport");
            Assert.IsNull(freight.Service.FindExclusivityConflict(null, WagonId,
                EntityId.For(EntityKind.Person, EthanId), TeamAnimals()),
                "no live shipment may hold the wagon/driver/horse after the third haul completes");
            // ReleaseShipmentReservations is a no-op on re-call.
            freight.Service.ReleaseShipmentReservations(shipment3);
            freight.Service.ReleaseShipmentReservations(shipment3);
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
            ReleaseTerminalDraftTeam(shipment3.ShipmentId);
            Assert.IsNull(BindTeamFor("freight-shipment:probe"),
                "after the sweep the draft team must rebind to a new shipment id");
        }

        [Test]
        public void RefusedDelivery_ThirdHaul_HoldsCargoCollectsNothing_RetriesOnce()
        {
            // Refuse the third haul's delivery (receiver returns false):
            // Delayed, 1 unit in wagon custody (a 1-sack third haul, so the
            // exact count is 1), no delivery, lot not received, payer/carrier
            // unchanged from the two-haul totals, agreement stays Accepted;
            // resume -> Arrived -> completes; lot received once, no duplicate
            // Bakery lot, third 125c settles exactly once, agreement
            // Fulfilled. The haul-2 shape from FreightRepeatabilityTests
            // (RefusedDelivery_SecondHaul_HoldsCargoCollectsNothing), now
            // after two completed hauls.
            int payerBefore = payerRuntime.CurrentCashCents;
            int carrierBefore = carrierRuntime.CurrentCashCents;

            DriveHaul1ToCompletion();
            BuildSecondHaul(out OffMapAidCargoLot lot2, out string request2Id,
                out FreightServiceAgreement agreement2, out LogisticsShipmentState shipment2);
            PrepareAndDispatchSecondHaul(agreement2, shipment2);
            SimulateSupplierPickupFor(shipment2);
            TickUntilCompleted(shipment2, "haul 2", Day2);
            Assert.IsTrue(lot2.Received, "the second paid Millbrook lot must be marked received");
            Assert.AreEqual(payerBefore - 2 * FreightChargeCents, payerRuntime.CurrentCashCents,
                "two hauls settled before the refused third haul (total -250c)");
            Assert.AreEqual(carrierBefore + 2 * FreightChargeCents, carrierRuntime.CurrentCashCents,
                "carrier credited two hauls before the refused third haul (total +250c)");

            BuildThirdHaul(out OffMapAidCargoLot lot3, out string request3Id,
                out FreightServiceAgreement agreement3, out LogisticsShipmentState shipment3);
            PrepareAndDispatchThirdHaul(agreement3, shipment3, shipment2.ShipmentId);
            SimulateSupplierPickupFor(shipment3);

            // The receiver refuses the third haul (the bakery cannot take
            // the lot).
            ReceiveFreightCargoLotHandler adapterReceive = hooks.ReceiveCargoLot;
            hooks.ReceiveCargoLot = (LogisticsShipmentState s, int qty, out string reason) =>
            {
                reason = "test refusal: bakery cannot receive the lot";
                return false;
            };
            hooks.IsVehicleOffMap = _ => false;
            var trace = new List<LogisticsShipmentStatus>();
            for (int tick = 0; tick < 8 && shipment3.Status != LogisticsShipmentStatus.Delayed; tick++)
            {
                Tick();
                trace.Add(shipment3.Status);
            }
            Assert.AreEqual(LogisticsShipmentStatus.Delayed, shipment3.Status,
                "a refused third-haul delivery must hold as Delayed; trace: " + string.Join(", ", trace));
            Assert.AreEqual(1, shipment3.RemainingQuantityUnits, "cargo stays in wagon custody");
            Assert.IsTrue(!shipment3.deliveryApplied, "no delivery is applied");
            Assert.IsTrue(!lot3.Received, "the lot is not received");
            Assert.AreEqual(payerBefore - 2 * FreightChargeCents, payerRuntime.CurrentCashCents,
                "no third debit while refused (only hauls 1-2 settled)");
            Assert.AreEqual(carrierBefore + 2 * FreightChargeCents, carrierRuntime.CurrentCashCents,
                "no third credit while refused");
            Assert.AreEqual(FreightServiceAgreementStatus.Accepted, agreement3.Status,
                "no agreement fulfilment while refused");

            // Allow the receiver and retry: resume from the delay, then tick
            // to completion. The retry settles exactly once.
            hooks.ReceiveCargoLot = adapterReceive;
            freight.Service.ResumeShipment(shipment3, hooks);
            Assert.AreEqual(LogisticsShipmentStatus.Arrived, shipment3.Status, "the retry resumes at Arrived");
            TickUntilCompleted(shipment3, "retried haul 3", Day3);
            Assert.IsTrue(lot3.Received, "the retried haul receives the lot once");
            Assert.AreEqual(3, bakery.FlourStock.Lots.Count, "no duplicate bakery lot after the retry");
            var importIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (BakeryFlourLot flourLot in bakery.FlourStock.Lots)
            {
                Assert.AreEqual(10, flourLot.Units, "each haul credits one 10 lb sack");
                Assert.IsTrue(importIds.Add(flourLot.ImportOrderId), "no bakery lot was minted twice for one Millbrook lot");
            }
            Assert.IsTrue(shipment3.FreightRevenueCollected, "the third charge must be collected exactly once");
            Assert.AreEqual(payerBefore - 3 * FreightChargeCents, payerRuntime.CurrentCashCents,
                "the retry settles the third 125c exactly once (total -375c)");
            Assert.AreEqual(carrierBefore + 3 * FreightChargeCents, carrierRuntime.CurrentCashCents,
                "the carrier is credited the third 125c exactly once (total +375c)");
            Assert.AreEqual(0, freight.FindCompany(CarrierId).FindUncollectedFreightChargeShipmentIds().Count,
                "no uncollected charge remains");
            Assert.AreEqual(FreightServiceAgreementStatus.Fulfilled, agreement3.Status,
                "the retried haul fulfils its agreement");

            // A further collection pass posts nothing more: the retry
            // settled exactly once, never twice.
            int reposted = freight.FindCompany(CarrierId).CollectAndPostUncollectedRevenue(
                carrierRuntime, freight.ResolvePayerRuntime, diag);
            Assert.AreEqual(0, reposted, "no second settlement after the retry collection");
            Assert.AreEqual(payerBefore - 3 * FreightChargeCents, payerRuntime.CurrentCashCents,
                "no second debit after the retry collection");
            Assert.AreEqual(carrierBefore + 3 * FreightChargeCents, carrierRuntime.CurrentCashCents,
                "no second credit after the retry collection");
        }

        // Builds a 2-unit haul the way BuildHaul builds a 1-sack haul,
        // except the shipment is created WITHOUT binding the lot: the
        // shipment carries 2 generic units through the non-custody
        // (ReceiveShipment) path, while the request/quote/acceptance and
        // the 125c charge still name the real 2-sack Millbrook lot and its
        // agreement. The custody path cannot produce a Partial: a custody
        // shipment's WorkUnloading calls the bool ReceiveCargoLot hook, so
        // it either delivers the whole lot or holds as Delayed — partial
        // acceptance exists only on the ReceiveShipment path
        // (FreightShipmentService.WorkUnloading, non-custody branch), which
        // is the path this test pins. The placed 2-sack lot therefore stays
        // outstanding (uncommitted, unreceived) and no Bakery receipt is
        // asserted here; the plan's Partial assertions (exact counts,
        // deliveryApplied, conservation, settlement, fulfilment) all hold
        // on this path.
        private void BuildTwoUnitHaul(out OffMapAidCargoLot nextLot, out string requestId,
            out FreightServiceAgreement nextAgreement, out LogisticsShipmentState nextShipment)
        {
            int buyerBefore = buyerCash.BalanceCents;
            Assert.IsNull(MillbrookFlourReorder.PlaceReorder(
                supplier, buyerCash, BakeryId, "Glen Pierce", 2, Day2, diag, out nextLot),
                "2-sack reorder on day " + Day2 + " must place: " + string.Join("; ", diag));
            Assert.IsNotNull(nextLot, "2-sack reorder must mint a lot");
            Assert.AreEqual("millbrook_grain_co:flour-reorder-d13-n2", nextLot.OrderId,
                "reorder id is deterministic (day 13; TryPlaceOrder prefixes the business id)");
            Assert.AreEqual(buyerBefore - nextLot.TotalCostCents, buyerCash.BalanceCents,
                "2-sack reorder debits buyer cash exactly once");
            Assert.AreEqual(96, nextLot.TotalCostCents, "two sacks cost the authored 96c");
            Assert.AreEqual(2, nextLot.PackageUnits, "the lot must hold 2 sacks");

            FreightServiceAgreementBook book = freight.ServiceAgreements;
            requestId = "freight-request:" + nextLot.OrderId;
            var request = new FreightServiceRequest
            {
                RequestId = requestId, RequestingBusinessId = BakeryId, CarrierBusinessId = CarrierId,
                PickupBusinessId = "millbrook_grain_co", DestinationBusinessId = BakeryId,
                CargoLotId = nextLot.LotId, Quantity = 1, QuantityUnit = "lb", RequestingPersonId = 2,
                CarrierOperatorPersonId = EthanId, PickupLocationId = "millbrook-pickup",
                DestinationLocationId = "bakery-door",
                CargoSkuId = MillbrookFlourFreightCustodyAdapter.FlourSkuId
            };
            Assert.IsNull(book.CreateRequest(request, out _), "request for the 2-unit haul must record");
            Assert.IsNull(book.SetQuote(requestId, new FreightServiceQuote
            {
                QuoteId = "quote-" + Day2, OneWayDistanceMiles = 50f, ChargeableMiles = 100,
                BaseServiceFeeCents = 25, RatePerMileCents = 1, MileageChargeCents = 100,
                TotalPriceCents = FreightChargeCents, PayerBusinessId = BakeryId, PaymentTiming = "After successful delivery"
            }), "quote for the 2-unit haul must record");
            Assert.IsNull(book.Accept(requestId, EthanId, Day2, Day2 * 86400f + 40000f, out nextAgreement),
                "request for the 2-unit haul must accept");
            Assert.IsNotNull(nextAgreement, "acceptance for the 2-unit haul must produce an agreement");
            string nextShipmentId = "freight-shipment:" + requestId;
            nextShipment = freight.CreateShipment(CarrierId, nextShipmentId, "millbrook_grain_co", BakeryId, 2,
                new FreightCargoSpec { MassKgPerUnit = 4.54f, VolumeM3PerUnit = 0.02f }, WagonId,
                hooks, EntityId.For(EntityKind.Person, EthanId), TeamAnimals(),
                FreightChargeCents, BakeryId, diag, out string shipmentRefusal,
                nextAgreement.AgreementId, string.Empty, string.Empty, true);
            Assert.IsNotNull(nextShipment, "2-unit shipment must create: " + shipmentRefusal);
            Assert.IsTrue(!nextShipment.requiresSourceLotCustody,
                "the 2-unit shipment must not require source-lot custody (no lot bound), so the partial path applies");
            Assert.AreEqual(2, nextShipment.PlannedQuantityUnits, "the 2-unit shipment must plan 2 units");
            Assert.IsNull(book.LinkShipment(nextAgreement.AgreementId, nextShipmentId), "2-unit shipment must link");
        }

        [Test]
        public void PartialAcceptance_RemainderStaysInWagon_ResumeCompletes()
        {
            // Receiver accepts part of a multi-unit shipment (a 2-unit
            // haul: accept 1, hold 1): status Partial with exact
            // accepted/remaining counts, deliveryApplied false, no
            // settlement, no fulfilment; BeginUnloading resumes the
            // remainder; on full acceptance the shipment completes,
            // deliveryApplied true, conservation holds (planned ==
            // delivered + remaining), and the 125c settles exactly once.
            // Unlike the other Slice 7 tests this haul loads through the
            // real driver loading path (BeginLoading/WorkLoading), not the
            // simulated supplier pickup, because a non-custody shipment has
            // no source lot to commit.
            int payerBefore = payerRuntime.CurrentCashCents;
            int carrierBefore = carrierRuntime.CurrentCashCents;

            DriveHaul1ToCompletion();
            Assert.AreEqual(payerBefore - FreightChargeCents, payerRuntime.CurrentCashCents,
                "haul 1 freight charge debited from the bakery exactly once");
            Assert.AreEqual(carrierBefore + FreightChargeCents, carrierRuntime.CurrentCashCents,
                "haul 1 freight charge credited to the carrier exactly once");

            BuildTwoUnitHaul(out OffMapAidCargoLot lot2, out string request2Id,
                out FreightServiceAgreement agreement2, out LogisticsShipmentState shipment2);
            PrepareAndDispatchSecondHaul(agreement2, shipment2);

            // The receiver accepts 1 unit on the first offer and the whole
            // remainder on the next offer.
            int acceptedTotal = 0;
            hooks.ReceiveShipment = (LogisticsShipmentState s, int qty, out string reason) =>
            {
                reason = string.Empty;
                int accepted = qty > 1 ? 1 : qty;
                acceptedTotal += accepted;
                return accepted;
            };

            hooks.DayIndex = Day2;
            hooks.IsVehicleOffMap = _ => false;
            var trace = new List<LogisticsShipmentStatus>();
            for (int tick = 0; tick < 8 && shipment2.Status != LogisticsShipmentStatus.Partial; tick++)
            {
                Tick();
                trace.Add(shipment2.Status);
            }
            Assert.AreEqual(LogisticsShipmentStatus.Partial, shipment2.Status,
                "a half-accepted delivery must hold as Partial; trace: " + string.Join(", ", trace));
            Assert.AreEqual(1, shipment2.DeliveredQuantityUnits, "exactly 1 unit accepted");
            Assert.AreEqual(1, shipment2.RemainingQuantityUnits, "exactly 1 unit remains in wagon custody");
            Assert.AreEqual(1, acceptedTotal, "the receiver took exactly 1 unit on the first offer");
            Assert.IsTrue(!shipment2.deliveryApplied, "no delivery is applied while a remainder is held");
            Assert.IsTrue(freight.Service.VerifyConservation(shipment2, 1, out string conservationAtPartial),
                "conservation must hold at Partial (planned 2 == delivered 1 + remaining 1): " + conservationAtPartial);
            Assert.IsTrue(!shipment2.FreightRevenueCollected, "no settlement while a remainder is held");
            Assert.AreEqual(payerBefore - FreightChargeCents, payerRuntime.CurrentCashCents,
                "no second debit while Partial (only haul 1 settled)");
            Assert.AreEqual(carrierBefore + FreightChargeCents, carrierRuntime.CurrentCashCents,
                "no second credit while Partial");
            Assert.AreEqual(FreightServiceAgreementStatus.Accepted, agreement2.Status,
                "no agreement fulfilment while Partial");
            string conflict = freight.Service.FindExclusivityConflict(null, WagonId,
                EntityId.For(EntityKind.Person, EthanId), TeamAnimals());
            Assert.IsNotNull(conflict, "the Partial remainder must keep the wagon bound");
            Assert.IsTrue(conflict.Contains(WagonId),
                "the Partial conflict must name the wagon, but got '" + conflict + "'");

            // Resume the remainder: the driver's Partial branch re-offers it
            // through BeginUnloading, and the receiver now takes it all.
            TickUntilCompleted(shipment2, "partial remainder resume", Day2);
            Assert.AreEqual(2, shipment2.DeliveredQuantityUnits, "both units delivered after the resume");
            Assert.AreEqual(0, shipment2.RemainingQuantityUnits, "nothing remains in wagon custody after the resume");
            Assert.AreEqual(2, acceptedTotal, "the receiver took the remaining 1 unit on the resume");
            Assert.IsTrue(shipment2.deliveryApplied, "delivery is applied on full acceptance");
            Assert.IsTrue(freight.Service.VerifyConservation(shipment2, 2, out string conservationAtDelivery),
                "conservation must hold at delivery (planned 2 == delivered 2 + remaining 0): " + conservationAtDelivery);
            Assert.IsTrue(shipment2.FreightRevenueCollected, "the 125c must settle exactly once on completion");
            Assert.AreEqual(payerBefore - 2 * FreightChargeCents, payerRuntime.CurrentCashCents,
                "payer debited a second 125c (total -250c across haul 1 and the 2-unit haul)");
            Assert.AreEqual(carrierBefore + 2 * FreightChargeCents, carrierRuntime.CurrentCashCents,
                "carrier credited a second 125c (total +250c)");
            Assert.AreEqual(0, freight.FindCompany(CarrierId).FindUncollectedFreightChargeShipmentIds().Count,
                "no uncollected charge remains after the resumed delivery");
            Assert.AreEqual(FreightServiceAgreementStatus.Fulfilled, agreement2.Status,
                "the resumed haul fulfils its agreement");
            Assert.IsNull(freight.Service.FindExclusivityConflict(null, WagonId,
                EntityId.For(EntityKind.Person, EthanId), TeamAnimals()),
                "wagon/driver/horse must be free after the resumed delivery completes");
        }

        // ---- Priority 7 Slice 8 (2026-11-08): save/load mid-journey
        // acceptance. Tests only, except for the one conditional repair the
        // task authorizes (see SaveLoad_SmallerSecondSave_DropsStaleShipment_
        // P7 below): P5 Slices 5–7 proved the save/load seams, but no test
        // captured a Planned, picked-up (return-day) shipment across a
        // restore and drove it to completion, and no P7-level test proved a
        // multi-sack haul survives a mid-transit restore with no duplicate
        // settlement. Expected to PASS after Slice 2 — the D-F2 case FAILS
        // before Slice 2 by the Slice 0 pin, and that ordering is confirmed
        // from the Slice 0 log rather than re-breaking the tree.

        [Test]
        public void SaveLoad_ReturnDayPlannedShipment_CompletesAfterRestore()
        {
            // The D-F2 acceptance: capture a Planned, authorized, picked-up
            // shipment (load applied, not yet dispatched); restore into a
            // fresh driver; tick with the wagon home; it dispatches
            // (InTransit), arrives, unloads, delivers, settles 125c once,
            // and fulfils its agreement — with the dispatch plan carried by
            // the DTO (the restored driver is asserted to hold a plan for
            // the shipment id, via the CaptureSaveDto read surface). The
            // D-F2 pin (Slice 0/2) proved only the dispatch; this test
            // drives the restored shipment to completion.
            int payerBefore = payerRuntime.CurrentCashCents;
            int carrierBefore = carrierRuntime.CurrentCashCents;

            BindAndAuthorize();
            SimulateSupplierPickup();
            Assert.AreEqual(LogisticsShipmentStatus.Planned, shipment.Status, "the picked-up shipment is Planned");
            Assert.IsTrue(shipment.loadApplied, "the supplier pickup applied the load");
            Assert.IsTrue(!shipment.physicalTransportBound, "transport is not yet bound before dispatch");

            FreightShipmentService.FreightServiceSaveDto dto = freight.CaptureSaveDto();
            FreightShipmentService.FreightDispatchPlanSaveDto carried = FindPlanIn(dto, ShipmentId);
            Assert.IsNotNull(carried, "the DTO must carry a dispatch plan for the Planned shipment");
            Assert.AreEqual("P7", carried.DriverPersonId, "the carried plan must name driver Ethan (P7)");
            Assert.AreEqual(1, carried.AnimalIds.Count, "the carried plan must name one draft animal");
            Assert.AreEqual("A0", carried.AnimalIds[0], "the carried plan must name horse A0");

            FreightProductionDriver restoredFreight = RestoreIntoFreshDriver(dto);
            LogisticsShipmentState restored = FindShipmentIn(restoredFreight, ShipmentId);
            Assert.IsNotNull(restored, "the restored driver must hold the Planned shipment");
            Assert.AreEqual(LogisticsShipmentStatus.Planned, restored.Status, "the restored shipment is Planned");
            Assert.IsTrue(restored.loadApplied, "the restored shipment keeps its applied load");
            Assert.IsTrue(!restored.physicalTransportBound, "the restored shipment is not transport-bound");
            FreightShipmentService.FreightDispatchPlanSaveDto restoredPlan =
                FindPlanIn(restoredFreight.CaptureSaveDto(), ShipmentId);
            Assert.IsNotNull(restoredPlan, "the restored driver must hold a dispatch plan for the shipment id");
            Assert.AreEqual("P7", restoredPlan.DriverPersonId, "the restored plan must name driver Ethan (P7)");
            Assert.AreEqual(1, restoredPlan.AnimalIds.Count, "the restored plan must name one draft animal");
            Assert.AreEqual("A0", restoredPlan.AnimalIds[0], "the restored plan must name horse A0");

            List<LogisticsShipmentStatus> trace =
                TickUntilCompletedOn(restoredFreight, restored, "restored return-day haul", Day);
            Assert.IsTrue(trace.Contains(LogisticsShipmentStatus.InTransit),
                "the restored shipment must dispatch (InTransit); trace: " + string.Join(", ", trace));
            Assert.IsTrue(trace.Contains(LogisticsShipmentStatus.Arrived),
                "the restored shipment must arrive; trace: " + string.Join(", ", trace));

            Assert.IsTrue(restored.deliveryApplied, "the restored shipment must record delivery applied");
            Assert.IsTrue(lot.Received, "the paid Millbrook lot must be marked received");
            Assert.AreEqual(1, bakery.FlourStock.Lots.Count, "no duplicate bakery lot after restore");
            Assert.AreEqual(10, bakery.FlourStock.Lots[0].Units, "the receipt holds one 10 lb sack");
            Assert.AreEqual(LotId, bakery.FlourStock.Lots[0].ImportOrderId,
                "the receipt must name the Millbrook lot");
            Assert.IsTrue(restored.FreightRevenueCollected, "the 125c must settle exactly once");
            Assert.AreEqual(payerBefore - FreightChargeCents, payerRuntime.CurrentCashCents,
                "payer debited exactly 125c once");
            Assert.AreEqual(carrierBefore + FreightChargeCents, carrierRuntime.CurrentCashCents,
                "carrier credited exactly 125c once");
            Assert.AreEqual(0, restoredFreight.FindCompany(CarrierId).FindUncollectedFreightChargeShipmentIds().Count,
                "no uncollected charge remains after restore");
            Assert.AreEqual(FreightServiceAgreementStatus.Fulfilled,
                restoredFreight.ServiceAgreements.FindAgreement(AgreementId).Status,
                "the agreement must fulfil after the restored haul completes");
        }

        [Test]
        public void SaveLoad_MidTransitSixteenSackHaul_RestoresExactlyOnce()
        {
            // A 16-sack haul captured InTransit (corrected spec); restore
            // into a fresh driver; cargo intact (16 units), lot unreceived,
            // agreement Accepted; complete with the restored driver only;
            // the Bakery receives exactly 160 lb in one lot, payer/carrier
            // move exactly 125c once, no duplicate lot, no duplicate
            // settlement, agreement Fulfilled.
            int payerBefore = payerRuntime.CurrentCashCents;
            int carrierBefore = carrierRuntime.CurrentCashCents;

            BuildSixteenSackHaul(out OffMapAidCargoLot lot16,
                out FreightServiceAgreement agreement16, out LogisticsShipmentState shipment16);
            PrepareAndDispatchSecondHaul(agreement16, shipment16);
            SimulateSupplierPickupFor(shipment16);

            hooks.DayIndex = Day2;
            hooks.IsVehicleOffMap = _ => false;
            Tick();
            Assert.AreEqual(LogisticsShipmentStatus.InTransit, shipment16.Status,
                "the 16-sack haul must be InTransit at capture");
            Assert.AreEqual(16, shipment16.RemainingQuantityUnits, "cargo intact at capture");
            Assert.IsTrue(!lot16.Received, "lot 16 is not received yet at capture");
            Assert.AreEqual(FreightServiceAgreementStatus.Accepted, agreement16.Status,
                "the 16-sack agreement is Accepted at capture");
            int payerAtCapture = payerRuntime.CurrentCashCents;
            int carrierAtCapture = carrierRuntime.CurrentCashCents;
            Assert.AreEqual(payerBefore - FreightChargeCents, payerAtCapture,
                "only haul 1 has settled at capture (payer -125c)");
            Assert.AreEqual(carrierBefore + FreightChargeCents, carrierAtCapture,
                "only haul 1 has settled at capture (carrier +125c)");

            FreightShipmentService.FreightServiceSaveDto dto = freight.CaptureSaveDto();

            FreightProductionDriver restoredFreight = RestoreIntoFreshDriverMulti(dto, 2);
            LogisticsShipmentState restored1 = FindShipmentIn(restoredFreight, ShipmentId);
            LogisticsShipmentState restored16 = FindShipmentIn(restoredFreight, shipment16.ShipmentId);
            Assert.IsNotNull(restored1, "haul 1 must be among the restored shipments");
            Assert.IsNotNull(restored16, "the 16-sack haul must be among the restored shipments");
            Assert.AreEqual(LogisticsShipmentStatus.Completed, restored1.Status, "haul 1 stays Completed after restore");
            Assert.IsTrue(restored1.FreightRevenueCollected, "haul 1 stays collected after restore");
            Assert.AreEqual(LogisticsShipmentStatus.InTransit, restored16.Status,
                "the 16-sack haul resumes InTransit");
            Assert.AreEqual(16, restored16.RemainingQuantityUnits, "the 16-sack cargo is intact after restore");
            Assert.IsTrue(!lot16.Received, "lot 16 stays unreceived after restore");
            Assert.AreEqual(FreightServiceAgreementStatus.Accepted,
                restoredFreight.ServiceAgreements.FindAgreement(agreement16.AgreementId).Status,
                "the 16-sack agreement stays Accepted after restore");

            // Finish with the restored driver only: the original driver is
            // never ticked again, so nothing can settle twice.
            TickUntilCompletedOn(restoredFreight, restored16, "restored 16-sack haul", Day2);

            Assert.IsTrue(lot16.Received, "lot 16 received exactly once after restore");
            Assert.AreEqual(2, bakery.FlourStock.Lots.Count, "no duplicate bakery lot after restore");
            Assert.AreEqual(1, CountBakeryReceiptsFor(lot16.LotId),
                "exactly one Bakery receipt for the 16-sack lot");
            BakeryFlourLot receipt16 = FindBakeryReceiptFor(lot16.LotId);
            Assert.IsNotNull(receipt16, "the bakery must hold a receipt for the 16-sack lot");
            Assert.AreEqual(160, receipt16.Units, "the 16-sack lot lands as 160 lb in ONE bakery lot");
            Assert.IsTrue(restored16.FreightRevenueCollected, "the 16-sack charge must be collected exactly once");
            Assert.AreEqual(payerAtCapture - FreightChargeCents, payerRuntime.CurrentCashCents,
                "the restored haul moves the payer exactly 125c once");
            Assert.AreEqual(carrierAtCapture + FreightChargeCents, carrierRuntime.CurrentCashCents,
                "the restored haul moves the carrier exactly 125c once");
            Assert.AreEqual(payerBefore - 2 * FreightChargeCents, payerRuntime.CurrentCashCents,
                "payer total -250c across haul 1 and the restored 16-sack haul");
            Assert.AreEqual(carrierBefore + 2 * FreightChargeCents, carrierRuntime.CurrentCashCents,
                "carrier total +250c across haul 1 and the restored 16-sack haul");
            Assert.AreEqual(0, restoredFreight.FindCompany(CarrierId).FindUncollectedFreightChargeShipmentIds().Count,
                "no uncollected charge remains after restore");
            Assert.AreEqual(FreightServiceAgreementStatus.Fulfilled,
                restoredFreight.ServiceAgreements.FindAgreement(agreement16.AgreementId).Status,
                "the 16-sack agreement must fulfil");
            int reposted = restoredFreight.FindCompany(CarrierId).CollectAndPostUncollectedRevenue(
                carrierRuntime, restoredFreight.ResolvePayerRuntime, diag);
            Assert.AreEqual(0, reposted, "a second collection pass must post 0c more");
            Assert.AreEqual(payerBefore - 2 * FreightChargeCents, payerRuntime.CurrentCashCents,
                "no second debit on the re-scan");
            Assert.AreEqual(carrierBefore + 2 * FreightChargeCents, carrierRuntime.CurrentCashCents,
                "no second credit on the re-scan");
        }

        [Test]
        public void SaveLoad_DoubleRestore_NoDuplicateSettlementOrShipment()
        {
            // Restore the same DTO twice into the same live instances (the
            // E-DEFECT-02 shape): exactly the same tracked set, no second
            // shipment id, no second settlement record, no second cash
            // movement on any runtime, and the Millbrook lot count
            // unchanged. Then restore a third time from the SAME DTO object
            // (not a fresh serialization) and assert the same. The DTO is a
            // mid-transit capture (haul 1 Completed + collected, haul 2
            // InTransit with its cargo), so the restore also proves a
            // repeated load never re-collects haul 1 or duplicates haul 2.
            DriveHaul1ToCompletion();
            BuildSecondHaul(out OffMapAidCargoLot lot2, out string request2Id,
                out FreightServiceAgreement agreement2, out LogisticsShipmentState shipment2);
            PrepareAndDispatchSecondHaul(agreement2, shipment2);
            SimulateSupplierPickupFor(shipment2);
            hooks.DayIndex = Day2;
            hooks.IsVehicleOffMap = _ => false;
            Tick();
            Assert.AreEqual(LogisticsShipmentStatus.InTransit, shipment2.Status,
                "haul 2 must be InTransit at capture");
            Assert.AreEqual(1, shipment2.RemainingQuantityUnits, "cargo intact at capture");
            Assert.IsTrue(!lot2.Received, "lot 2 is not received yet at capture");

            FreightShipmentService.FreightServiceSaveDto dto = freight.CaptureSaveDto();
            Assert.AreEqual(2, dto.Shipments.Count, "the captured DTO carries both shipments");
            Assert.AreEqual(2, dto.DispatchPlans.Count, "the captured DTO carries both plans");

            int payerBefore = payerRuntime.CurrentCashCents;
            int carrierBefore = carrierRuntime.CurrentCashCents;
            int buyerBefore = buyerCash.BalanceCents;
            int bakeryLotsBefore = bakery.FlourStock.Lots.Count;
            int millbrookLotsBefore = supplier.Lots.Count;

            var live = new FreightProductionDriver();
            live.EnsureCompany(CarrierId, carrierRuntime);
            live.RegisterBusinessRuntime(BakeryId, payerRuntime);
            live.Service.RegisterWagon(wagon);

            // First restore, then the same DTO object a second and a third
            // time into the same live driver/service/company instances.
            for (int restore = 1; restore <= 3; restore++)
            {
                live.RestoreSaveDto(dto, hooks, diag);
                Assert.AreEqual(2, live.AllShipments().Count,
                    "restore " + restore + ": exactly the same tracked set (2 shipments)");
                var ids = new HashSet<string>(StringComparer.Ordinal);
                foreach (LogisticsShipmentState s in live.AllShipments())
                    Assert.IsTrue(ids.Add(s.ShipmentId),
                        "restore " + restore + ": no second shipment id ('" + s.ShipmentId + "' twice)");
                Assert.AreEqual(2, ids.Count, "restore " + restore + ": exactly 2 distinct shipment ids");
                Assert.AreEqual(2, live.Service.TrackedShipments.Count,
                    "restore " + restore + ": the service tracks exactly 2 shipments");
                LogisticsShipmentState r1 = FindShipmentIn(live, ShipmentId);
                LogisticsShipmentState r2 = FindShipmentIn(live, shipment2.ShipmentId);
                Assert.IsNotNull(r1, "restore " + restore + ": haul 1 must be present");
                Assert.IsNotNull(r2, "restore " + restore + ": haul 2 must be present");
                Assert.AreEqual(LogisticsShipmentStatus.Completed, r1.Status,
                    "restore " + restore + ": haul 1 stays Completed");
                Assert.IsTrue(r1.FreightRevenueCollected,
                    "restore " + restore + ": haul 1 stays collected — no second settlement record");
                Assert.AreEqual(LogisticsShipmentStatus.InTransit, r2.Status,
                    "restore " + restore + ": haul 2 stays InTransit");
                Assert.IsTrue(!r2.FreightRevenueCollected,
                    "restore " + restore + ": haul 2 stays uncollected until it completes");
                // FindUncollectedFreightChargeShipmentIds lists only
                // Completed + delivered + uncollected shipments, so with
                // haul 1 collected and haul 2 still InTransit the list is
                // EMPTY — no charge is due yet, and no second settlement
                // record exists for either haul. (First draft asserted 1
                // here, naming haul 2 — wrong: an InTransit shipment is
                // not collectable until it completes; verified in source,
                // FreightCompanyRuntime.cs:379-395.)
                List<string> uncollected =
                    live.FindCompany(CarrierId).FindUncollectedFreightChargeShipmentIds();
                Assert.AreEqual(0, uncollected.Count,
                    "restore " + restore + ": no charge is due (haul 1 collected, haul 2 not yet complete)");
                Assert.IsTrue(!uncollected.Contains(ShipmentId),
                    "restore " + restore + ": no second settlement record for haul 1");
                Assert.IsTrue(!uncollected.Contains(shipment2.ShipmentId),
                    "restore " + restore + ": haul 2 is not collectable before it completes");
                Assert.AreEqual(payerBefore, payerRuntime.CurrentCashCents,
                    "restore " + restore + ": no second cash movement on the payer runtime");
                Assert.AreEqual(carrierBefore, carrierRuntime.CurrentCashCents,
                    "restore " + restore + ": no second cash movement on the carrier runtime");
                Assert.AreEqual(buyerBefore, buyerCash.BalanceCents,
                    "restore " + restore + ": no second cash movement on the buyer cash port");
                Assert.AreEqual(bakeryLotsBefore, bakery.FlourStock.Lots.Count,
                    "restore " + restore + ": no second bakery lot");
                Assert.AreEqual(millbrookLotsBefore, supplier.Lots.Count,
                    "restore " + restore + ": the Millbrook lot count is unchanged");
                FreightShipmentService.FreightServiceSaveDto recaptured = live.CaptureSaveDto();
                Assert.IsNotNull(FindPlanIn(recaptured, ShipmentId),
                    "restore " + restore + ": haul 1's plan survives, exactly once");
                Assert.IsNotNull(FindPlanIn(recaptured, shipment2.ShipmentId),
                    "restore " + restore + ": haul 2's plan survives, exactly once");
                Assert.AreEqual(2, recaptured.DispatchPlans.Count,
                    "restore " + restore + ": exactly two plans, no duplicate plan");
            }

            // The DTO object itself must be pristine after three restores
            // from it: restoring must not mutate the save.
            Assert.AreEqual(2, dto.Shipments.Count, "the DTO must still carry both shipments after three restores");
            Assert.AreEqual(2, dto.DispatchPlans.Count, "the DTO must still carry both plans after three restores");
        }

        [Test]
        public void SaveLoad_SmallerSecondSave_DropsStaleShipment_P7()
        {
            // Reference, do not redo: P5 Slice 5 already pins the
            // SERVICE-level smaller-save drop
            // (FreightServiceRestore_SmallerSecondSave_DropsStaleShipment).
            // This test asserts only the DRIVER-level composed half: a
            // smaller DTO restored through
            // FreightProductionDriver.RestoreSaveDto drops the stale
            // shipment from the company's active list and the dispatch-plan
            // map alike. The P5 test does NOT cover the driver half (it
            // restores through a bare FreightShipmentService, never through
            // the driver), so this test is not omitted.
            //
            // Slice 2 flag resolution (2026-11-08): FreightProductionDriver.
            // RestoreSaveDto did NOT clear `dispatchPlans` at its start
            // (the plan did not ask for it, so the Slice 2 coder left the
            // pre-existing non-clearing behaviour alone), while the
            // company's active lists ARE rebuilt clear-then-load
            // (RestoreActiveShipments clears) and the service half clears
            // (P5 Slice 5). The plan map therefore kept a stale entry for
            // a dropped shipment. The plan map has no public reader, and a
            // re-capture after the smaller restore cannot see the stale
            // entry — CaptureSaveDto only writes plans for shipments in the
            // company's active list — so the distinguishing probe is a
            // re-add: restore a DTO that carries the dropped shipment again
            // with a DIFFERENT plan driver (P8). Without the clear, the
            // DTO-plan branch skips it (dispatchPlans.ContainsKey) and the
            // shipment dispatches with the stale P7 plan; with the clear,
            // the fresh P8 plan wins. This test FAILS pre-clear on the P8
            // assertions and PASSES after the one-line clear (the minimal
            // clear-then-load repair at the same layer, P5 Slice 5
            // precedent).
            DriveHaul1ToCompletion();

            // The smaller save: haul 1 only, captured before haul 2 exists.
            FreightShipmentService.FreightServiceSaveDto smallerDto = freight.CaptureSaveDto();
            Assert.AreEqual(1, smallerDto.Shipments.Count, "the smaller save carries only haul 1");

            BuildSecondHaul(out OffMapAidCargoLot lot2, out string request2Id,
                out FreightServiceAgreement agreement2, out LogisticsShipmentState shipment2);
            PrepareAndDispatchSecondHaul(agreement2, shipment2);
            SimulateSupplierPickupFor(shipment2);
            Assert.AreEqual(LogisticsShipmentStatus.Planned, shipment2.Status,
                "haul 2 is Planned, picked up, not yet dispatched");
            Assert.IsTrue(shipment2.loadApplied, "haul 2 load applied");
            Assert.IsTrue(!shipment2.physicalTransportBound, "haul 2 transport not yet bound");

            FreightShipmentService.FreightServiceSaveDto fullDto = freight.CaptureSaveDto();
            Assert.AreEqual(2, fullDto.Shipments.Count, "the full save carries both hauls");
            Assert.AreEqual(2, fullDto.DispatchPlans.Count, "the full save carries both plans");

            FreightProductionDriver live = RestoreIntoFreshDriverMulti(fullDto, 2);
            Assert.AreEqual(2, live.AllShipments().Count, "the live driver holds both shipments after the full restore");
            Assert.IsNotNull(FindPlanIn(live.CaptureSaveDto(), ShipmentId),
                "the live driver must hold haul 1's plan after the full restore");
            Assert.IsNotNull(FindPlanIn(live.CaptureSaveDto(), shipment2.ShipmentId),
                "the live driver must hold haul 2's plan after the full restore");

            // Smaller second save into the SAME live driver: the stale
            // shipment must drop from the company's active list.
            live.RestoreSaveDto(smallerDto, hooks, diag);
            Assert.AreEqual(1, live.AllShipments().Count,
                "the smaller restore must drop the stale shipment from the company list");
            Assert.IsNull(FindShipmentIn(live, shipment2.ShipmentId),
                "the stale shipment must be gone from the live driver");
            Assert.IsNotNull(FindShipmentIn(live, ShipmentId), "haul 1 survives the smaller restore");

            // Re-add probe: the full DTO again, but with haul 2's plan
            // naming a different driver (P8). The fresh plan must win.
            string freshDriver = EntityId.For(EntityKind.Person, 8).ToString();
            FreightShipmentService.FreightDispatchPlanSaveDto fullPlan2 = FindPlanIn(fullDto, shipment2.ShipmentId);
            Assert.IsNotNull(fullPlan2, "the full save must carry haul 2's plan to rewrite");
            fullPlan2.DriverPersonId = freshDriver;
            live.RestoreSaveDto(fullDto, hooks, diag);
            Assert.AreEqual(2, live.AllShipments().Count, "the re-added shipment returns to the company list");
            LogisticsShipmentState readded = FindShipmentIn(live, shipment2.ShipmentId);
            Assert.IsNotNull(readded, "the re-added shipment must be in the live driver");
            Assert.AreEqual(LogisticsShipmentStatus.Planned, readded.Status, "the re-added shipment is Planned");
            Assert.IsTrue(readded.loadApplied, "the re-added shipment keeps its applied load");
            FreightShipmentService.FreightDispatchPlanSaveDto readdedPlan =
                FindPlanIn(live.CaptureSaveDto(), shipment2.ShipmentId);
            Assert.IsNotNull(readdedPlan, "the re-added shipment must hold a plan");
            Assert.AreEqual(freshDriver, readdedPlan.DriverPersonId,
                "the re-added shipment must hold the FRESH plan (" + freshDriver + "); a stale P7 plan surviving "
                + "the smaller restore would block it, but the re-captured plan names '"
                + readdedPlan.DriverPersonId + "'");

            // Behavioural confirmation: the re-added shipment dispatches
            // with the fresh plan driver, not the stale one.
            hooks.DayIndex = Day2;
            hooks.IsVehicleOffMap = _ => false;
            diag.Clear();
            live.AdvanceTick(86400f, hooks, Handler(), diag);
            Assert.AreEqual(LogisticsShipmentStatus.InTransit, readded.Status,
                "the re-added shipment must dispatch");
            Assert.AreEqual(freshDriver, readded.physicalDriverPersonId,
                "the re-added shipment must dispatch with the fresh plan driver (" + freshDriver
                + "), not the stale P7 plan, but dispatched with '" + readded.physicalDriverPersonId + "'");
        }
    }
}
