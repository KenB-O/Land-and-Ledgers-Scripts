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
    }
}
