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

        // Haul 1 end to end, exactly as FreightRepeatabilityTests does it.
        // Asserts completion, receipt, and the fulfilled agreement; the
        // caller owns the cash assertions.
        private void DriveHaul1ToCompletion()
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
    }
}
