using System.Collections.Generic;
using LandLedgers.Economy.Businesses.Logging;
using LandLedgers.Economy.Farming.Timber;
using LandLedgers.Primitives;
using NUnit.Framework;

// PORT NOTE (F6 Tier 2, 2026-11-13): ported verbatim from the forestry
// branch (main repo at 346d8746, 639 lines) onto this line's baseline
// 2cb4781, following the F6 Tier 1 counters-tests port. Only the header
// note and the two RegisterLotFromFields call sites changed (the ported
// overload gains a massKg parameter, plan 22 §4.1 deviation 3), the save
// test gains two assertions (OwnerId/MassKg round trip), and two new
// tests (L1, L2) pin the OwnerId/MassKg deviations. The fixture fells
// at 300 kg/log on this line (TimberStand.KilogramsPerLog defaults
// 300f; the forestry TimberStand has no such field), the same 300 kg/log
// the DocumentedLogLot shape uses — no fixture change needed.

namespace LandLedgers.Editor.Economy
{
    /// <summary>
    /// F3: the log lot custody ledger — one custody record per felled lot,
    /// tracking where the lot is, who holds it, what state it is in, and
    /// who has reserved it. Every test enforces the same doctrine as F1
    /// and F2: a lot stays tied to its harvest location until it is
    /// collected or legitimately transferred, two carriers cannot collect
    /// the same reserved lot, a retried collection never duplicates the
    /// lot, and a refused step leaves the state unchanged.
    /// </summary>
    [TestFixture]
    public sealed class LogLotCustodyLedgerTests
    {
        private sealed class Fixture
        {
            public const string StandId = "stand-sawmill-district";
            public const string LocationId = "sawmill-district";
            public const string Holder = "logging-business-1";
            public const string TreeKey = "world-tree-1";
            public const string TreeKey2 = "world-tree-2";
            public const string CarrierOne = "carrier-1";
            public const string CarrierTwo = "carrier-2";
            public const string MillBusiness = "sawmill-business-1";

            public LoggingStandRegistry Stands = new LoggingStandRegistry();
            public EntityIdRegistry Ids = new EntityIdRegistry();
            public LogLotCustodyLedger Ledger;
            public EntityId Worker = EntityId.For(EntityKind.Person, 5);
            public List<string> Diagnostics = new List<string>();

            public Fixture()
            {
                Assert.IsNull(Stands.RegisterStand(new TimberStand
                {
                    StandId = StandId,
                    DisplayName = "Sawmill District Pines",
                    LocationId = LocationId,
                    Species = "white pine",
                    StandingTimberUnits = 10,
                }));
                Assert.IsNull(Stands.GrantTimberRights(StandId, Holder, 200, "town-land-office"));
                Ledger = new LogLotCustodyLedger(Diagnostics);
            }

            public LogLot FellLot(int units, int dayIndex)
            {
                LogLot lot = TimberHarvest.FellLogs(Ids, Stands.GetStand(StandId), Holder, units, Worker, dayIndex, Diagnostics);
                Assert.IsNotNull(lot, "FellLogs must produce a lot on a granted stand.");
                return lot;
            }

            public LogLot RegisterLot(int units, int dayIndex, string treeKey)
            {
                LogLot lot = FellLot(units, dayIndex);
                Assert.IsNull(Ledger.RegisterLot(lot, LocationId, Holder, treeKey, dayIndex),
                    "RegisterLot refused a valid lot.");
                return lot;
            }

            public LogLot RegisterLot()
            {
                return RegisterLot(2, 200, TreeKey);
            }

            public string LotIdOf(LogLot lot)
            {
                return lot.LotId.ToString();
            }

            public LogLotCustodyRecord RecordOf(LogLot lot)
            {
                return Ledger.GetLot(LotIdOf(lot));
            }

            public void CollectAs(LogLot lot, string collector, int dayIndex)
            {
                string refusal;
                Assert.IsTrue(Ledger.CollectLot(LotIdOf(lot), collector, dayIndex, out refusal),
                    "CollectLot refused: " + refusal);
                Assert.IsNull(refusal);
            }

            public void DriveToLoaded(LogLot lot)
            {
                CollectAs(lot, CarrierOne, 201);
                Assert.IsNull(Ledger.LoadLot(LotIdOf(lot), CarrierOne, 202));
            }

            public void DriveToInTransit(LogLot lot)
            {
                DriveToLoaded(lot);
                Assert.IsNull(Ledger.MarkInTransit(LotIdOf(lot), CarrierOne, 203));
            }

            public void DriveToDelivered(LogLot lot)
            {
                DriveToInTransit(lot);
                Assert.IsNull(Ledger.MarkDelivered(LotIdOf(lot), MillBusiness, 204));
            }
        }

        [Test]
        public void RegisterLotRefusesNullLot()
        {
            var f = new Fixture();
            Assert.IsNotNull(f.Ledger.RegisterLot(null, Fixture.LocationId, Fixture.Holder, Fixture.TreeKey, 200));
            Assert.AreEqual(0, f.Ledger.LotCount);
        }

        [Test]
        public void RegisterLotRefusesAnonymousLot()
        {
            var f = new Fixture();
            var anonymous = new LogLot
            {
                LotId = EntityId.Invalid,
                LogUnits = 2,
                StandId = Fixture.StandId,
                Species = "white pine",
                FelledBy = f.Worker,
                FelledDayIndex = 200,
            };
            Assert.IsNotNull(f.Ledger.RegisterLot(anonymous, Fixture.LocationId, Fixture.Holder, Fixture.TreeKey, 200));
            Assert.AreEqual(0, f.Ledger.LotCount);
        }

        [Test]
        public void RegisterLotRefusesZeroLogUnits()
        {
            var f = new Fixture();
            LogLot lot = f.FellLot(2, 200);
            lot.LogUnits = 0;
            Assert.IsNotNull(f.Ledger.RegisterLot(lot, Fixture.LocationId, Fixture.Holder, Fixture.TreeKey, 200));
            Assert.AreEqual(0, f.Ledger.LotCount);
        }

        [Test]
        public void RegisterLotRefusesBlankStandId()
        {
            var f = new Fixture();
            LogLot lot = f.FellLot(2, 200);
            lot.StandId = string.Empty;
            Assert.IsNotNull(f.Ledger.RegisterLot(lot, Fixture.LocationId, Fixture.Holder, Fixture.TreeKey, 200));
            Assert.AreEqual(0, f.Ledger.LotCount);
        }

        [Test]
        public void RegisterLotRefusesBlankHarvestLocationId()
        {
            var f = new Fixture();
            LogLot lot = f.FellLot(2, 200);
            Assert.IsNotNull(f.Ledger.RegisterLot(lot, string.Empty, Fixture.Holder, Fixture.TreeKey, 200));
            Assert.AreEqual(0, f.Ledger.LotCount);
        }

        [Test]
        public void RegisterLotRefusesBlankCustodianId()
        {
            var f = new Fixture();
            LogLot lot = f.FellLot(2, 200);
            Assert.IsNotNull(f.Ledger.RegisterLot(lot, Fixture.LocationId, string.Empty, Fixture.TreeKey, 200));
            Assert.AreEqual(0, f.Ledger.LotCount);
        }

        [Test]
        public void RegisterLotSuccessRecordsTheLot()
        {
            var f = new Fixture();
            LogLot lot = f.FellLot(2, 200);
            Assert.IsNull(f.Ledger.RegisterLot(lot, Fixture.LocationId, Fixture.Holder, Fixture.TreeKey, 200));
            Assert.AreEqual(1, f.Ledger.LotCount);
            LogLotCustodyRecord record = f.RecordOf(lot);
            Assert.IsNotNull(record);
            Assert.AreEqual(LogLotCustodyState.Created, record.State);
            Assert.AreEqual(Fixture.LocationId, record.HarvestLocationId);
            Assert.AreEqual(Fixture.Holder, record.CustodianId);
            Assert.AreEqual(Fixture.StandId, record.StandId);
            Assert.AreEqual("white pine", record.Species);
            Assert.AreEqual(200, record.FelledDayIndex);
            Assert.AreEqual(Fixture.TreeKey, record.SourceTreeKey);
            Assert.AreEqual(1, f.Ledger.LotsAtLocation(Fixture.LocationId).Count);
            Assert.AreEqual(1, f.Ledger.LotsInState(LogLotCustodyState.Created).Count);
        }

        [Test]
        public void RegisterLotDuplicateLotIdIsRefused()
        {
            var f = new Fixture();
            LogLot lot = f.RegisterLot();
            Assert.IsNotNull(f.Ledger.RegisterLot(lot, Fixture.LocationId, Fixture.CarrierOne, Fixture.TreeKey, 201));
            Assert.AreEqual(1, f.Ledger.LotCount);
            Assert.AreEqual(Fixture.Holder, f.RecordOf(lot).CustodianId);
        }

        [Test]
        public void RegisterLotFromFieldsProducesIdenticalRecord()
        {
            var f = new Fixture();
            LogLot lot = f.FellLot(2, 200);
            var first = new LogLotCustodyLedger(new List<string>());
            var second = new LogLotCustodyLedger(new List<string>());
            Assert.IsNull(first.RegisterLot(lot, Fixture.LocationId, Fixture.Holder, Fixture.TreeKey, 200));
            Assert.IsNull(second.RegisterLotFromFields(f.LotIdOf(lot), lot.LogUnits, lot.StandId, lot.Species,
                lot.FelledBy, lot.FelledDayIndex, Fixture.LocationId, Fixture.Holder, Fixture.TreeKey, 200, lot.MassKg));
            LogLotCustodyRecord a = first.GetLot(f.LotIdOf(lot));
            LogLotCustodyRecord b = second.GetLot(f.LotIdOf(lot));
            Assert.AreEqual(a.LotId, b.LotId);
            Assert.AreEqual(a.LogUnits, b.LogUnits);
            Assert.AreEqual(a.StandId, b.StandId);
            Assert.AreEqual(a.Species, b.Species);
            Assert.AreEqual(a.FelledDayIndex, b.FelledDayIndex);
            Assert.AreEqual(a.SourceTreeKey, b.SourceTreeKey);
            Assert.AreEqual(a.State, b.State);
            Assert.AreEqual(a.CustodianId, b.CustodianId);
            Assert.AreEqual(a.HarvestLocationId, b.HarvestLocationId);
            Assert.AreEqual(a.FelledByKind, b.FelledByKind);
            Assert.AreEqual(a.FelledByNumber, b.FelledByNumber);
            Assert.AreEqual(a.OwnerId, b.OwnerId);
            Assert.AreEqual(a.MassKg, b.MassKg, 0.001f);
        }

        [Test]
        public void RegisterLotFromFieldsRefusesBlankIdZeroUnitsAndBlankStand()
        {
            var f = new Fixture();
            LogLot lot = f.FellLot(2, 200);
            string lotId = f.LotIdOf(lot);
            Assert.IsNotNull(f.Ledger.RegisterLotFromFields(string.Empty, 2, Fixture.StandId, "white pine",
                f.Worker, 200, Fixture.LocationId, Fixture.Holder, Fixture.TreeKey, 200, 600f));
            Assert.AreEqual(0, f.Ledger.LotCount);
            Assert.IsNotNull(f.Ledger.RegisterLotFromFields(lotId, 0, Fixture.StandId, "white pine",
                f.Worker, 200, Fixture.LocationId, Fixture.Holder, Fixture.TreeKey, 200, 600f));
            Assert.AreEqual(0, f.Ledger.LotCount);
            Assert.IsNotNull(f.Ledger.RegisterLotFromFields(lotId, 2, string.Empty, "white pine",
                f.Worker, 200, Fixture.LocationId, Fixture.Holder, Fixture.TreeKey, 200, 600f));
            Assert.AreEqual(0, f.Ledger.LotCount);
        }

        [Test]
        public void TryReserveLotRefusesUnknownLot()
        {
            var f = new Fixture();
            Assert.IsNotNull(f.Ledger.TryReserveLot("lot:999", Fixture.CarrierOne, 201));
        }

        [Test]
        public void TryReserveLotRefusesBlankReserver()
        {
            var f = new Fixture();
            LogLot lot = f.RegisterLot();
            Assert.IsNotNull(f.Ledger.TryReserveLot(f.LotIdOf(lot), string.Empty, 201));
            Assert.AreEqual(string.Empty, f.RecordOf(lot).ReservedBy);
        }

        [Test]
        public void TryReserveLotSuccessHoldsOneReserver()
        {
            var f = new Fixture();
            LogLot lot = f.RegisterLot();
            Assert.IsNull(f.Ledger.TryReserveLot(f.LotIdOf(lot), Fixture.CarrierOne, 201));
            Assert.AreEqual(Fixture.CarrierOne, f.RecordOf(lot).ReservedBy);
            Assert.AreEqual(201, f.RecordOf(lot).ReservedDayIndex);
        }

        [Test]
        public void TryReserveLotSecondReserverIsRefused()
        {
            var f = new Fixture();
            LogLot lot = f.RegisterLot();
            Assert.IsNull(f.Ledger.TryReserveLot(f.LotIdOf(lot), Fixture.CarrierOne, 201));
            Assert.IsNotNull(f.Ledger.TryReserveLot(f.LotIdOf(lot), Fixture.CarrierTwo, 201));
            Assert.AreEqual(Fixture.CarrierOne, f.RecordOf(lot).ReservedBy);
        }

        [Test]
        public void TryReserveLotRepeatBySameReserverIsIdempotent()
        {
            var f = new Fixture();
            LogLot lot = f.RegisterLot();
            Assert.IsNull(f.Ledger.TryReserveLot(f.LotIdOf(lot), Fixture.CarrierOne, 201));
            Assert.IsNull(f.Ledger.TryReserveLot(f.LotIdOf(lot), Fixture.CarrierOne, 202));
            Assert.AreEqual(Fixture.CarrierOne, f.RecordOf(lot).ReservedBy);
            Assert.AreEqual(201, f.RecordOf(lot).ReservedDayIndex);
        }

        [Test]
        public void ReleaseReservationByNonHolderIsRefused()
        {
            var f = new Fixture();
            LogLot lot = f.RegisterLot();
            Assert.IsNull(f.Ledger.TryReserveLot(f.LotIdOf(lot), Fixture.CarrierOne, 201));
            Assert.IsNotNull(f.Ledger.ReleaseReservation(f.LotIdOf(lot), Fixture.CarrierTwo));
            Assert.AreEqual(Fixture.CarrierOne, f.RecordOf(lot).ReservedBy);
        }

        [Test]
        public void ReleaseReservationByHolderClearsTheReservation()
        {
            var f = new Fixture();
            LogLot lot = f.RegisterLot();
            Assert.IsNull(f.Ledger.TryReserveLot(f.LotIdOf(lot), Fixture.CarrierOne, 201));
            Assert.IsNull(f.Ledger.ReleaseReservation(f.LotIdOf(lot), Fixture.CarrierOne));
            Assert.AreEqual(string.Empty, f.RecordOf(lot).ReservedBy);
            Assert.AreEqual(-1, f.RecordOf(lot).ReservedDayIndex);
            Assert.AreEqual(LogLotCustodyState.Created, f.RecordOf(lot).State);
        }

        [Test]
        public void CollectLotRefusesUnknownLot()
        {
            var f = new Fixture();
            string refusal;
            Assert.IsFalse(f.Ledger.CollectLot("lot:999", Fixture.CarrierOne, 201, out refusal));
            Assert.IsNotNull(refusal);
        }

        [Test]
        public void CollectLotByNonHolderOnReservedLotIsRefused()
        {
            var f = new Fixture();
            LogLot lot = f.RegisterLot();
            Assert.IsNull(f.Ledger.TryReserveLot(f.LotIdOf(lot), Fixture.CarrierOne, 201));
            string refusal;
            Assert.IsFalse(f.Ledger.CollectLot(f.LotIdOf(lot), Fixture.CarrierTwo, 202, out refusal));
            Assert.IsNotNull(refusal);
            Assert.AreEqual(LogLotCustodyState.Created, f.RecordOf(lot).State);
        }

        [Test]
        public void CollectLotByHoldingReserverSucceedsExactlyOnce()
        {
            var f = new Fixture();
            LogLot lot = f.RegisterLot();
            Assert.IsNull(f.Ledger.TryReserveLot(f.LotIdOf(lot), Fixture.CarrierOne, 201));
            string refusal;
            Assert.IsTrue(f.Ledger.CollectLot(f.LotIdOf(lot), Fixture.CarrierOne, 202, out refusal));
            Assert.IsNull(refusal);
            Assert.AreEqual(LogLotCustodyState.Collected, f.RecordOf(lot).State);
            Assert.AreEqual(Fixture.CarrierOne, f.RecordOf(lot).CustodianId);
            Assert.AreEqual(202, f.RecordOf(lot).CollectedDayIndex);
            Assert.AreEqual(string.Empty, f.RecordOf(lot).ReservedBy);
            Assert.AreEqual(Fixture.LocationId, f.RecordOf(lot).HarvestLocationId);
            Assert.AreEqual(1, f.Ledger.LotCount);
        }

        [Test]
        public void CollectLotOnUnreservedLotClaimsFirst()
        {
            var f = new Fixture();
            LogLot lot = f.RegisterLot();
            string refusal;
            Assert.IsTrue(f.Ledger.CollectLot(f.LotIdOf(lot), Fixture.CarrierOne, 201, out refusal));
            Assert.IsNull(refusal);
            Assert.IsFalse(f.Ledger.CollectLot(f.LotIdOf(lot), Fixture.CarrierTwo, 201, out refusal));
            Assert.IsNotNull(refusal);
            Assert.AreEqual(Fixture.CarrierOne, f.RecordOf(lot).CustodianId);
        }

        [Test]
        public void CollectLotRetriedBySameCollectorIsIdempotent()
        {
            var f = new Fixture();
            LogLot lot = f.RegisterLot();
            string refusal;
            Assert.IsTrue(f.Ledger.CollectLot(f.LotIdOf(lot), Fixture.CarrierOne, 201, out refusal));
            Assert.IsNull(refusal);
            Assert.IsTrue(f.Ledger.CollectLot(f.LotIdOf(lot), Fixture.CarrierOne, 202, out refusal));
            Assert.IsNull(refusal);
            Assert.AreEqual(1, f.Ledger.LotCount);
            Assert.AreEqual(LogLotCustodyState.Collected, f.RecordOf(lot).State);
            Assert.AreEqual(201, f.RecordOf(lot).CollectedDayIndex);
        }

        [Test]
        public void CollectLotRetriedByDifferentCollectorIsRefused()
        {
            var f = new Fixture();
            LogLot lot = f.RegisterLot();
            string refusal;
            Assert.IsTrue(f.Ledger.CollectLot(f.LotIdOf(lot), Fixture.CarrierOne, 201, out refusal));
            Assert.IsFalse(f.Ledger.CollectLot(f.LotIdOf(lot), Fixture.CarrierTwo, 202, out refusal));
            Assert.IsNotNull(refusal);
            Assert.AreEqual(Fixture.CarrierOne, f.RecordOf(lot).CustodianId);
        }

        [Test]
        public void CollectLotOnAlreadyLoadedLotIsRefused()
        {
            var f = new Fixture();
            LogLot lot = f.RegisterLot();
            f.DriveToLoaded(lot);
            string refusal;
            Assert.IsFalse(f.Ledger.CollectLot(f.LotIdOf(lot), Fixture.CarrierOne, 203, out refusal));
            Assert.IsNotNull(refusal);
            Assert.AreEqual(LogLotCustodyState.Loaded, f.RecordOf(lot).State);
        }

        [Test]
        public void LoadLotFromCreatedIsRefused()
        {
            var f = new Fixture();
            LogLot lot = f.RegisterLot();
            Assert.IsNotNull(f.Ledger.LoadLot(f.LotIdOf(lot), Fixture.CarrierOne, 201));
            Assert.AreEqual(LogLotCustodyState.Created, f.RecordOf(lot).State);
        }

        [Test]
        public void LoadLotByNonCustodianIsRefused()
        {
            var f = new Fixture();
            LogLot lot = f.RegisterLot();
            f.CollectAs(lot, Fixture.CarrierOne, 201);
            Assert.IsNotNull(f.Ledger.LoadLot(f.LotIdOf(lot), Fixture.CarrierTwo, 202));
            Assert.AreEqual(LogLotCustodyState.Collected, f.RecordOf(lot).State);
        }

        [Test]
        public void LoadLotByCustodianSucceeds()
        {
            var f = new Fixture();
            LogLot lot = f.RegisterLot();
            f.CollectAs(lot, Fixture.CarrierOne, 201);
            Assert.IsNull(f.Ledger.LoadLot(f.LotIdOf(lot), Fixture.CarrierOne, 202));
            Assert.AreEqual(LogLotCustodyState.Loaded, f.RecordOf(lot).State);
            Assert.AreEqual(202, f.RecordOf(lot).LoadedDayIndex);
            Assert.AreEqual(Fixture.LocationId, f.RecordOf(lot).HarvestLocationId);
        }

        [Test]
        public void MarkInTransitFromCollectedIsRefused()
        {
            var f = new Fixture();
            LogLot lot = f.RegisterLot();
            f.CollectAs(lot, Fixture.CarrierOne, 201);
            Assert.IsNotNull(f.Ledger.MarkInTransit(f.LotIdOf(lot), Fixture.CarrierOne, 202));
            Assert.AreEqual(LogLotCustodyState.Collected, f.RecordOf(lot).State);
        }

        [Test]
        public void MarkInTransitFromLoadedSucceeds()
        {
            var f = new Fixture();
            LogLot lot = f.RegisterLot();
            f.DriveToLoaded(lot);
            Assert.IsNull(f.Ledger.MarkInTransit(f.LotIdOf(lot), Fixture.CarrierOne, 203));
            Assert.AreEqual(LogLotCustodyState.InTransit, f.RecordOf(lot).State);
            Assert.AreEqual(203, f.RecordOf(lot).InTransitDayIndex);
            Assert.AreEqual(Fixture.LocationId, f.RecordOf(lot).HarvestLocationId);
        }

        [Test]
        public void MarkDeliveredFromLoadedIsRefused()
        {
            var f = new Fixture();
            LogLot lot = f.RegisterLot();
            f.DriveToLoaded(lot);
            Assert.IsNotNull(f.Ledger.MarkDelivered(f.LotIdOf(lot), Fixture.MillBusiness, 203));
            Assert.AreEqual(LogLotCustodyState.Loaded, f.RecordOf(lot).State);
        }

        [Test]
        public void MarkDeliveredFromInTransitSucceeds()
        {
            var f = new Fixture();
            LogLot lot = f.RegisterLot();
            f.DriveToInTransit(lot);
            Assert.IsNull(f.Ledger.MarkDelivered(f.LotIdOf(lot), Fixture.MillBusiness, 204));
            Assert.AreEqual(LogLotCustodyState.Delivered, f.RecordOf(lot).State);
            Assert.AreEqual(Fixture.MillBusiness, f.RecordOf(lot).CustodianId);
            Assert.AreEqual(204, f.RecordOf(lot).DeliveredDayIndex);
        }

        [Test]
        public void MarkProcessedFromInTransitIsRefused()
        {
            var f = new Fixture();
            LogLot lot = f.RegisterLot();
            f.DriveToInTransit(lot);
            Assert.IsNotNull(f.Ledger.MarkProcessed(f.LotIdOf(lot), Fixture.MillBusiness, 204));
            Assert.AreEqual(LogLotCustodyState.InTransit, f.RecordOf(lot).State);
        }

        [Test]
        public void MarkProcessedFromDeliveredSucceeds()
        {
            var f = new Fixture();
            LogLot lot = f.RegisterLot();
            f.DriveToDelivered(lot);
            Assert.IsNull(f.Ledger.MarkProcessed(f.LotIdOf(lot), Fixture.MillBusiness, 205));
            Assert.AreEqual(LogLotCustodyState.Processed, f.RecordOf(lot).State);
            Assert.AreEqual(205, f.RecordOf(lot).ProcessedDayIndex);
        }

        [Test]
        public void LaterTransitionsNeverChangeHarvestLocation()
        {
            var f = new Fixture();
            LogLot lot = f.RegisterLot();
            Assert.AreEqual(Fixture.LocationId, f.RecordOf(lot).HarvestLocationId);
            f.CollectAs(lot, Fixture.CarrierOne, 201);
            Assert.AreEqual(Fixture.LocationId, f.RecordOf(lot).HarvestLocationId);
            Assert.IsNull(f.Ledger.LoadLot(f.LotIdOf(lot), Fixture.CarrierOne, 202));
            Assert.AreEqual(Fixture.LocationId, f.RecordOf(lot).HarvestLocationId);
            Assert.IsNull(f.Ledger.MarkInTransit(f.LotIdOf(lot), Fixture.CarrierOne, 203));
            Assert.AreEqual(Fixture.LocationId, f.RecordOf(lot).HarvestLocationId);
            Assert.IsNull(f.Ledger.MarkDelivered(f.LotIdOf(lot), Fixture.MillBusiness, 204));
            Assert.AreEqual(Fixture.LocationId, f.RecordOf(lot).HarvestLocationId);
            Assert.IsNull(f.Ledger.MarkProcessed(f.LotIdOf(lot), Fixture.MillBusiness, 205));
            Assert.AreEqual(Fixture.LocationId, f.RecordOf(lot).HarvestLocationId);
            Assert.AreEqual(LogLotCustodyState.Processed, f.RecordOf(lot).State);
        }

        [Test]
        public void SaveRoundTripPreservesStatesCustodiansReservationsAndDayIndexes()
        {
            var f = new Fixture();
            LogLot lotOne = f.RegisterLot(2, 200, Fixture.TreeKey);
            LogLot lotTwo = f.RegisterLot(3, 200, Fixture.TreeKey2);
            Assert.IsNull(f.Ledger.TryReserveLot(f.LotIdOf(lotOne), Fixture.CarrierOne, 201));
            f.CollectAs(lotTwo, Fixture.CarrierTwo, 202);

            LogLotCustodyLedger.LogLotCustodyLedgerSaveDto dto = f.Ledger.CaptureSaveDto();
            var fresh = new LogLotCustodyLedger(new List<string>());
            fresh.LoadFromSaveDto(dto);
            Assert.AreEqual(2, fresh.LotCount);
            LogLotCustodyRecord reserved = fresh.GetLot(f.LotIdOf(lotOne));
            Assert.IsNotNull(reserved);
            Assert.AreEqual(LogLotCustodyState.Created, reserved.State);
            Assert.AreEqual(Fixture.CarrierOne, reserved.ReservedBy);
            Assert.AreEqual(201, reserved.ReservedDayIndex);
            Assert.AreEqual(Fixture.Holder, reserved.OwnerId);
            Assert.AreEqual(600f, reserved.MassKg, 0.001f);
            LogLotCustodyRecord collected = fresh.GetLot(f.LotIdOf(lotTwo));
            Assert.IsNotNull(collected);
            Assert.AreEqual(LogLotCustodyState.Collected, collected.State);
            Assert.AreEqual(Fixture.CarrierTwo, collected.CustodianId);
            Assert.AreEqual(202, collected.CollectedDayIndex);
            Assert.AreEqual(3, collected.LogUnits);
            Assert.AreEqual(Fixture.Holder, collected.OwnerId);
            Assert.AreEqual(900f, collected.MassKg, 0.001f);
        }

        [Test]
        public void NullDtoClearsToEmpty()
        {
            var f = new Fixture();
            f.RegisterLot();
            Assert.AreEqual(1, f.Ledger.LotCount);
            f.Ledger.LoadFromSaveDto(null);
            Assert.AreEqual(0, f.Ledger.LotCount);
        }

        [Test]
        public void LoadWithDuplicateLotIdsKeepsTheFirst()
        {
            var f = new Fixture();
            var dto = new LogLotCustodyLedger.LogLotCustodyLedgerSaveDto();
            dto.Lots.Add(new LogLotCustodyRecord
            {
                LotId = "lot:77",
                LogUnits = 2,
                StandId = Fixture.StandId,
                Species = "white pine",
                FelledDayIndex = 200,
                HarvestLocationId = Fixture.LocationId,
                CustodianId = "first-custodian",
                State = LogLotCustodyState.Created,
            });
            dto.Lots.Add(new LogLotCustodyRecord
            {
                LotId = "lot:77",
                LogUnits = 3,
                StandId = Fixture.StandId,
                Species = "white pine",
                FelledDayIndex = 200,
                HarvestLocationId = Fixture.LocationId,
                CustodianId = "second-custodian",
                State = LogLotCustodyState.Created,
            });
            f.Ledger.LoadFromSaveDto(dto);
            Assert.AreEqual(1, f.Ledger.LotCount);
            Assert.AreEqual("first-custodian", f.Ledger.GetLot("lot:77").CustodianId);
        }

        [Test]
        public void LoadWithBlankLotIdSkipsTheRecord()
        {
            var f = new Fixture();
            var dto = new LogLotCustodyLedger.LogLotCustodyLedgerSaveDto();
            dto.Lots.Add(new LogLotCustodyRecord
            {
                LotId = string.Empty,
                LogUnits = 2,
                StandId = Fixture.StandId,
                Species = "white pine",
                FelledDayIndex = 200,
                HarvestLocationId = Fixture.LocationId,
                CustodianId = Fixture.Holder,
                State = LogLotCustodyState.Created,
            });
            f.Ledger.LoadFromSaveDto(dto);
            Assert.AreEqual(0, f.Ledger.LotCount);
        }

        [Test]
        public void LoadWithStateOutsideZeroToFiveSkipsTheRecord()
        {
            var f = new Fixture();
            var dto = new LogLotCustodyLedger.LogLotCustodyLedgerSaveDto();
            dto.Lots.Add(new LogLotCustodyRecord
            {
                LotId = "lot:99",
                LogUnits = 2,
                StandId = Fixture.StandId,
                Species = "white pine",
                FelledDayIndex = 200,
                HarvestLocationId = Fixture.LocationId,
                CustodianId = Fixture.Holder,
                State = (LogLotCustodyState)7,
            });
            f.Ledger.LoadFromSaveDto(dto);
            Assert.AreEqual(0, f.Ledger.LotCount);
        }

        [Test]
        public void LoadTwiceYieldsTheSameCount()
        {
            var f = new Fixture();
            f.RegisterLot();
            LogLotCustodyLedger.LogLotCustodyLedgerSaveDto dto = f.Ledger.CaptureSaveDto();
            var fresh = new LogLotCustodyLedger(new List<string>());
            fresh.LoadFromSaveDto(dto);
            Assert.AreEqual(1, fresh.LotCount);
            fresh.LoadFromSaveDto(dto);
            Assert.AreEqual(1, fresh.LotCount);
        }

        [Test]
        public void L1_RegisterLotCopiesOwnerIdAndMassKgFromTheLiveLot()
        {
            var f = new Fixture();
            LogLot lot = f.FellLot(2, 200);
            Assert.IsNull(f.Ledger.RegisterLot(lot, Fixture.LocationId, Fixture.Holder, Fixture.TreeKey, 200));
            LogLotCustodyRecord record = f.RecordOf(lot);
            Assert.IsNotNull(record);
            Assert.AreEqual(Fixture.Holder, record.OwnerId);
            Assert.AreEqual(600f, record.MassKg, 0.001f);
            Assert.AreEqual(2, record.LogUnits);
        }

        [Test]
        public void L2_RegisterLotFromFieldsRecordsOwnerFromCustodianAndPassedMass()
        {
            var f = new Fixture();
            LogLot lot = f.FellLot(2, 200);
            Assert.IsNull(f.Ledger.RegisterLotFromFields(f.LotIdOf(lot), lot.LogUnits, lot.StandId, lot.Species,
                lot.FelledBy, lot.FelledDayIndex, Fixture.LocationId, Fixture.Holder, Fixture.TreeKey, 200, 600f));
            LogLotCustodyRecord record = f.RecordOf(lot);
            Assert.IsNotNull(record);
            Assert.AreEqual(Fixture.Holder, record.OwnerId);
            Assert.AreEqual(600f, record.MassKg, 0.001f);
            var second = new LogLotCustodyLedger(new List<string>());
            Assert.IsNull(second.RegisterLot(lot, Fixture.LocationId, Fixture.Holder, Fixture.TreeKey, 200));
            LogLotCustodyRecord live = second.GetLot(f.LotIdOf(lot));
            Assert.AreEqual(live.OwnerId, record.OwnerId);
            Assert.AreEqual(live.MassKg, record.MassKg, 0.001f);
        }
    }
}
