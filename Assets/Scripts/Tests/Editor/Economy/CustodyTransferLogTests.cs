// Copyright (c) Land & Ledgers contributors. Licensed under the MIT License.
// Priority 9 Slice F3 — custody transfer log tests (P9 plan section 2.4,
// section 4 Slice F3, directive Priority D). Pins: the log's record shape
// and duplicate refusal, the bounded store (512, PROVISIONAL), the
// end-to-end chain through the real authorities (Millbrook order, freight
// commit, Bakery receipt, dispense, bake, retail transfer, household
// purchase, household receipt), no double transfer through duplicate
// receipt processing, the null-log legs (transfers still complete), and
// that the log records custody only — ownership stays on the lot owning
// authority (the receipt books the lot to the household, never the
// shopper). Acceptance cases E (lot identity and provenance preserved),
// F (no double transfer), M (partial transfer preserves total quantity),
// and R (no change to existing economic transactions: 16-sack haul,
// 125c settlement, 12 lb to 24 loaves) are all pinned by the chain test.

using System;
using System.Collections.Generic;
using LandLedgers.Animals;
using LandLedgers.Economy;
using LandLedgers.Economy.Blacksmith;
using LandLedgers.Economy.BreadDrivers;
using LandLedgers.Economy.Businesses.Bakery;
using LandLedgers.Economy.Businesses.GeneralStore;
using LandLedgers.Economy.Core;
using LandLedgers.Economy.Equipment;
using LandLedgers.Economy.Equipment.Workstations;
using LandLedgers.Economy.Financing;
using LandLedgers.Economy.Freight;
using LandLedgers.Economy.Logistics;
using LandLedgers.Economy.Ordering;
using LandLedgers.Economy.Transport;
using LandLedgers.Orchestration.Player;
using LandLedgers.Persistence;
using LandLedgers.Population;
using LandLedgers.Primitives;
using LandLedgers.Tasks;
using LandLedgers.Time;
using LandLedgers.World.Journeys;
using LandLedgers.World.Regional;
using NUnit.Framework;
using UnityEngine;
using EntityId = LandLedgers.Primitives.EntityId;

namespace LandLedgers.Editor.Economy
{
    [TestFixture]
    public sealed class CustodyTransferLogTests
    {
        private const int Day = 12;
        private const int EthanId = 7;
        private const string CarrierId = "livery_freight_003";
        private const string BakeryId = "bakery_001";
        private const string StoreInstanceId = "general-store-1";
        private const string WagonId = "opening-freight-wagon";
        private const string ShipmentId = "freight-shipment:request-1";
        private const string AgreementId = "freight-agreement:request-1";
        private const string LotId = "aidlot-millbrook_grain_co:chain-flour-16";
        private const string OrderRequestId = "chain-flour-16";
        private const int Sacks = 16;
        private const int FreightChargeCents = 125;
        private const int HouseholdId = 7;
        private const int ShopperPersonId = 1;

        private static CustodyTransferRecord SampleRecord(
            string causeKind = CustodyTransferLog.CauseMillbrookOrder,
            string causeId = "millbrook_grain_co:test-order",
            int units = 16,
            int dayIndex = 0)
        {
            return new CustodyTransferRecord(
                dayIndex, "bakery_flour_sack", units,
                LotKey.Empty,
                new LotKey(CustodyTransferLog.MillbrookAuthority("millbrook_grain_co"), "aidlot-test"),
                CustodyTransferLog.SupplierCustodian("millbrook_grain_co"),
                CustodyTransferLog.SupplierCustodian("millbrook_grain_co"),
                causeId, causeKind);
        }

        [Test]
        public void RecordShape_AssignsSequentialRecordIds()
        {
            var log = new CustodyTransferLog();
            Assert.AreEqual(0, log.Count);

            Assert.IsTrue(log.Record(SampleRecord(), out string diag0), diag0);
            Assert.IsTrue(log.Record(SampleRecord(CustodyTransferLog.CauseFreightCommit, ShipmentId, 16, 11), out string diag1), diag1);
            Assert.AreEqual(2, log.Count);
            Assert.AreEqual("custody-1", log.Records[0].RecordId);
            Assert.AreEqual("custody-2", log.Records[1].RecordId);
            Assert.AreEqual(0, log.Records[0].DayIndex);
            Assert.AreEqual("bakery_flour_sack", log.Records[0].ItemId);
            Assert.AreEqual(16, log.Records[0].Units);
            Assert.IsTrue(log.Records[0].SourceLotKey.IsEmpty, "a creation event has an empty source lot key");
            Assert.AreEqual("millbrook:millbrook_grain_co/aidlot-test", log.Records[0].DestinationLotKey.ToString());
            Assert.AreEqual("supplier:millbrook_grain_co", log.Records[0].FromCustodian);
            Assert.AreEqual(CustodyTransferLog.CauseMillbrookOrder, log.Records[0].CauseKind);
        }

        [Test]
        public void DuplicateRefusal_SameKindAndId_SecondRefused()
        {
            var log = new CustodyTransferLog();
            Assert.IsTrue(log.Record(SampleRecord(), out _));
            Assert.IsTrue(!log.Record(SampleRecord(), out string diagnostic),
                "the second record with the same CauseKind plus CauseId must be refused");
            Assert.IsTrue(diagnostic.Contains("duplicate"), "the refusal must name itself as a duplicate: " + diagnostic);
            Assert.AreEqual(1, log.Count, "the log never double-records a transfer");
            Assert.AreEqual("custody-1", log.Records[0].RecordId);
        }

        [Test]
        public void SameCauseIdDifferentKind_IsNotDuplicate()
        {
            var log = new CustodyTransferLog();
            Assert.IsTrue(log.Record(SampleRecord(CustodyTransferLog.CauseFreightCommit, ShipmentId), out _));
            Assert.IsTrue(log.Record(SampleRecord(CustodyTransferLog.CauseFreightReceive, ShipmentId), out string diag),
                "freight-commit and freight-receive share the shipment idempotency key but are different transfers: " + diag);
            Assert.AreEqual(2, log.Count);
        }

        [Test]
        public void Validation_NullEmptyCauseOrZeroUnits_Refused()
        {
            var log = new CustodyTransferLog();
            Assert.IsTrue(!log.Record(null, out _), "a null record must be refused");
            Assert.IsTrue(!log.Record(SampleRecord(causeId: string.Empty), out _),
                "a record with no idempotency key must be refused");
            Assert.IsTrue(!log.Record(SampleRecord(causeKind: string.Empty), out _),
                "a record with no kind must be refused");
            Assert.IsTrue(!log.Record(SampleRecord(units: 0), out _),
                "a record moving zero units must be refused");
            Assert.IsTrue(!log.Record(SampleRecord(units: -3), out _),
                "a record moving negative units must be refused");
            Assert.AreEqual(0, log.Count);
            Assert.AreEqual(5, log.Diagnostics.Count, "every refusal is named in diagnostics");
        }

        [Test]
        public void LatestForLot_FindsLatestBySourceOrDestination()
        {
            var log = new CustodyTransferLog();
            var millbrookKey = new LotKey(CustodyTransferLog.MillbrookAuthority("millbrook_grain_co"), "aidlot-test");
            var bakeryKey = new LotKey(CustodyTransferLog.BakeryAuthority(BakeryId), "Lot:9");
            Assert.IsTrue(log.Record(SampleRecord(), out _));
            Assert.IsTrue(log.Record(new CustodyTransferRecord(12, "bakery_flour_sack", 160,
                millbrookKey, bakeryKey,
                CustodyTransferLog.WagonCustodian(WagonId),
                CustodyTransferLog.BakeryAuthority(BakeryId),
                ShipmentId, CustodyTransferLog.CauseFreightReceive), out _));

            CustodyTransferRecord latest = log.LatestForLot(millbrookKey);
            Assert.IsNotNull(latest, "the lot is the source of the receive and the destination of the order");
            Assert.AreEqual(CustodyTransferLog.CauseFreightReceive, latest.CauseKind,
                "the latest touching record wins, not the first");
            Assert.AreEqual("bakery:bakery_001", log.LatestForLot(bakeryKey).ToCustodian);
        }

        [Test]
        public void LatestForCause_FindsRecord()
        {
            var log = new CustodyTransferLog();
            Assert.IsTrue(log.Record(SampleRecord(), out _));
            CustodyTransferRecord found = log.LatestForCause(
                CustodyTransferLog.CauseMillbrookOrder, "millbrook_grain_co:test-order");
            Assert.IsNotNull(found);
            Assert.AreEqual("custody-1", found.RecordId);
            Assert.IsNull(log.LatestForCause(CustodyTransferLog.CauseMillbrookOrder, "millbrook_grain_co:no-such-order"));
            Assert.IsNull(log.LatestForCause(CustodyTransferLog.CauseFreightCommit, "millbrook_grain_co:test-order"));
        }

        [Test]
        public void LatestForUnknown_ReturnsNull()
        {
            var log = new CustodyTransferLog();
            Assert.IsTrue(log.Record(SampleRecord(), out _));
            Assert.IsNull(log.LatestForLot(new LotKey("millbrook:millbrook_grain_co", "aidlot-no-such-lot")));
            Assert.IsNull(log.LatestForLot(LotKey.Empty), "an empty key matches nothing");
            Assert.IsNull(log.LatestForCause(null, null));
        }

        [Test]
        public void BoundedStore_EvictsOldestFirstCountsEvictions()
        {
            var log = new CustodyTransferLog();
            for (int i = 0; i < CustodyTransferLog.MaxRecords + 10; i++)
            {
                Assert.IsTrue(log.Record(SampleRecord(
                    CustodyTransferLog.CauseRetailPurchase, "receipt-" + i, 1, i), out string diag),
                    "record " + i + " must be accepted: " + diag);
            }
            Assert.AreEqual(CustodyTransferLog.MaxRecords, log.Count, "the store never holds more than the cap");
            Assert.AreEqual(10, log.EvictedCount, "ten oldest records evicted, counted");
            Assert.AreEqual("custody-11", log.Records[0].RecordId, "the oldest retained record is the eleventh accepted");
            Assert.AreEqual("custody-" + (CustodyTransferLog.MaxRecords + 10), log.Records[log.Count - 1].RecordId);
            Assert.IsTrue(log.Diagnostics.Count >= 10, "evictions are counted in diagnostics");
        }

        [Test]
        public void BoundedStore_SequenceNeverReused()
        {
            var log = new CustodyTransferLog();
            for (int i = 0; i < CustodyTransferLog.MaxRecords + 1; i++)
            {
                Assert.IsTrue(log.Record(SampleRecord(
                    CustodyTransferLog.CauseRetailPurchase, "receipt-" + i, 1, i), out _));
            }
            Assert.AreEqual("custody-2", log.Records[0].RecordId, "custody-1 was evicted, never reused");
            Assert.AreEqual("custody-" + (CustodyTransferLog.MaxRecords + 1), log.Records[log.Count - 1].RecordId);
            var seen = new HashSet<string>();
            foreach (CustodyTransferRecord record in log.Records)
            {
                Assert.IsTrue(seen.Add(record.RecordId), "record id " + record.RecordId + " appears twice");
            }
        }

        [Test]
        public void DuplicateAfterEviction_StillRefused()
        {
            var log = new CustodyTransferLog();
            Assert.IsTrue(log.Record(SampleRecord(), out _), "the original records");
            for (int i = 0; i < CustodyTransferLog.MaxRecords; i++)
            {
                Assert.IsTrue(log.Record(SampleRecord(
                    CustodyTransferLog.CauseRetailPurchase, "receipt-" + i, 1, i), out _));
            }
            Assert.IsNull(log.LatestForCause(CustodyTransferLog.CauseMillbrookOrder, "millbrook_grain_co:test-order"),
                "the original was evicted from the retained store");
            Assert.IsTrue(!log.Record(SampleRecord(), out string diagnostic),
                "the re-record must still be refused: " + diagnostic);
            Assert.IsTrue(diagnostic.Contains("duplicate"), "the refusal names the duplicate: " + diagnostic);
            Assert.AreEqual(CustodyTransferLog.MaxRecords, log.Count);
        }

        [Test]
        public void LotKey_EmptyEqualityAndToString()
        {
            Assert.IsTrue(LotKey.Empty.IsEmpty);
            Assert.AreEqual("<empty>", LotKey.Empty.ToString());
            var a = new LotKey("bakery:bakery_001", "Lot:9");
            var b = new LotKey("bakery:bakery_001", "Lot:9");
            var c = new LotKey("bakery:bakery_001", "Lot:10");
            var d = new LotKey("millbrook:millbrook_grain_co", "Lot:9");
            Assert.IsTrue(a == b, "equal keys compare equal");
            Assert.IsTrue(a != c, "different lot ids are different keys");
            Assert.IsTrue(a != d, "the same lot id under another authority is a different key");
            Assert.IsTrue(!a.IsEmpty);
            Assert.AreEqual("bakery:bakery_001/Lot:9", a.ToString());
        }

        // ---- shared chain fixture: the real authorities, merged from
        // FreightDeliveryPaymentAuditTests (freight) and
        // BakeryFoodChainEndToEndTests (bakery + shopping). ----

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

        // Production port over a real BusinessRuntimeState, copied from
        // BakeryFoodChainEndToEndTests: the production GeneralStoreTradingPort
        // wraps a MonoBehaviour manager and cannot be constructed in Roslyn.
        private sealed class RuntimeStorePort : IGeneralStoreTradingPort
        {
            public BusinessRuntimeState Runtime;
            public Dictionary<string, int> Prices = new Dictionary<string, int>();
            public int Cap = int.MaxValue;
            public List<GeneralStoreCategoryOffer> OfferList = new List<GeneralStoreCategoryOffer>();

            public string StoreBusinessId => "general-store-1";
            public string StoreName => "General Store";
            public string LocationId => "general-store";
            public IReadOnlyList<GeneralStoreCategoryOffer> CategoryOffers => OfferList;
            public StoreTradingCapability TradingCapability => new StoreTradingCapability { Staffed = true };
            public int CurrentCashCents => Runtime != null ? Runtime.CurrentCashCents : 0;
            public bool OffersDelivery => false;
            public bool OffersTradeCredit => false;
            public int TradeCreditLimitCents => 0;

            public int CategoryStockUnits(string categoryId)
            {
                CategoryStockState stock = Runtime != null ? Runtime.GetCategoryStock(categoryId) : null;
                return stock != null ? stock.CurrentStockUnits : 0;
            }
            public int CategoryTargetStockUnits(string categoryId)
            {
                CategoryStockState stock = Runtime != null ? Runtime.GetCategoryStock(categoryId) : null;
                return stock != null ? stock.TargetStockUnits : 0;
            }
            public int CategoryPricePerUnitCents(string categoryId)
            {
                return Prices.TryGetValue(categoryId, out int p) ? p : 0;
            }
            public bool TryConsumeStoreUnits(string categoryId, int units, out int consumedUnits)
            {
                consumedUnits = 0;
                return Runtime != null && Runtime.TryConsumeCategoryStockUnits(categoryId, units, out consumedUnits);
            }
            public void RecordCashSettlement(string categoryId, int unitsSold, int revenueCents)
            {
                if (Runtime != null) Runtime.RecordGenericRetailSettlement(categoryId, unitsSold, revenueCents);
            }
            public void RecordCreditSettlement(string categoryId, int unitsSold, int amountCents, string obligationId) { }
            public string SpendCash(int amountCents, string purpose)
            {
                if (Runtime == null) return "no runtime";
                if (Runtime.CurrentCashCents < amountCents) return "cannot afford";
                Runtime.SpendCents(amountCents);
                return null;
            }
            public string ReceiveStock(string categoryId, int units, string provenanceLabel)
            {
                if (Runtime == null) return "no runtime";
                if (units > Cap) return $"capped port refuses {units}u (cap {Cap})";
                int accepted = Runtime.AddCategoryStockUnits(categoryId, units);
                if (accepted < units) return $"store accepted {accepted}/{units}u {categoryId}: category full.";
                return null;
            }
            public int CategoryReceivableUnits(string categoryId)
            {
                CategoryStockState stock = Runtime != null ? Runtime.GetCategoryStock(categoryId) : null;
                if (stock == null) return 0;
                if (Cap < int.MaxValue) return Math.Min(Cap, stock.TargetStockUnits > 0 ? Math.Max(0, stock.TargetStockUnits - stock.CurrentStockUnits) : Cap);
                return stock.TargetStockUnits > 0 ? Math.Max(0, stock.TargetStockUnits - stock.CurrentStockUnits) : int.MaxValue;
            }
            public int DeliveryFeeCents(string itemId, int units) => 0;
        }

        private sealed class Chain
        {
            public List<string> Diag = new List<string>();
            public CustodyTransferLog Log;
            public EntityIdRegistry Ids = new EntityIdRegistry();

            // Millbrook + freight.
            public OffMapAidBusinessRuntime Supplier;
            public FakeBusinessCashPort BakeryCash;
            public OffMapAidCargoLot Lot;
            public BakeryShopRuntime Bakery = new BakeryShopRuntime(BakeryId);
            public BusinessRuntimeState BakeryRuntime = new BusinessRuntimeState();
            public MillbrookFlourFreightCustodyAdapter Adapter;
            public TaskAuthority Tasks;
            public WorkTimeBudgetStore Budgets;
            public EquipmentReservationService Reservations;
            public FreightProductionDriver Freight;
            public FreightShipmentHooks Hooks;
            public AnimalState Horse;
            public EquipmentAsset Wagon;
            public LogisticsShipmentState Shipment;
            public FreightServiceAgreement Agreement;
            public BusinessRuntimeState CarrierRuntime;
            public BusinessRuntimeState PayerRuntime;
            public JourneyModel JourneyModel;
            public OffMapJourneyAuthority Journeys;

            // Bakery production + store.
            public BusinessRuntimeState StoreRuntime = new BusinessRuntimeState();
            public RuntimeStorePort StorePort;
            public Pass3StoreCatalog Catalog = new Pass3StoreCatalog();
            public Pass3WholesaleIntake Intake = new Pass3WholesaleIntake();
            public Series2BakeryProductionDriver ProductionDriver;

            // Shopping.
            public PopulationState Population;
            public HouseholdMembershipRegistry Membership;
            public HouseholdLedgerRegistry Ledgers;
            public HouseholdInventoryRegistry Inventories;
            public HouseholdNeedRegistry Needs;
            public SupplierDirectory Directory;
            public JourneyModel ShoppingJourneys;
            public PersonScheduleTracker Tracker;
            public ShopperSellerKnowledge Knowledge;
            public HouseholdShoppingLoop Loop;
            public Dictionary<int, string> PersonLocations = new Dictionary<int, string>();
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

        private static Func<string, WorkstationComponentView?> OvenFinder()
        {
            var kinds = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                { "oven-chamber-1", "oven-chamber" },
                { "kneading-table-1", "kneading-table" },
                { "proofing-rack-1", "proofing-rack" },
                { "bake-peels-1", "bake-peels" },
            };
            return assetId => kinds.TryGetValue(assetId, out string kind)
                ? (WorkstationComponentView?)new WorkstationComponentView
                {
                    AssetId = assetId,
                    Kind = kind,
                    Condition01 = 0.9f,
                    IsUsable = true,
                }
                : null;
        }

        private static BusinessRuntimeState Runtime(string id, int cash)
        {
            var runtime = new BusinessRuntimeState();
            runtime.BindBusinessIdentity(id);
            runtime.SetCurrentCashCents(cash);
            return runtime;
        }

        private static Chain NewChain(CustodyTransferLog log)
        {
            var s = new Chain { Log = log };
            s.Supplier = new OffMapAidBusinessRuntime(MillbrookDefinition());
            s.Supplier.CustodyLog = log;
            s.BakeryCash = new FakeBusinessCashPort(20000);
            s.Bakery.CustodyLog = log;
            s.Adapter = new MillbrookFlourFreightCustodyAdapter(s.Supplier, s.Bakery, s.Ids, log);

            // Freight (mirrors FreightDeliveryPaymentAuditTests.SetUp).
            s.Tasks = new TaskAuthority();
            s.Budgets = new WorkTimeBudgetStore();
            s.Budgets.EnsureDay(Day);
            s.Budgets.EnsureDay(Day + 1);
            s.Budgets.EnsureDay(Day + 2);
            s.Reservations = new EquipmentReservationService();
            s.Horse = new AnimalRegistry(s.Ids).RegisterAnimal(AnimalSpecies.Horse, "Draft", AnimalSex.Male,
                AnimalOwnerKind.Business, CarrierId, Day, "Custody chain fixture");
            Assert.IsNotNull(s.Horse);
            Assert.IsNull(HorseAuthority.Configure(s.Horse, HorseUseCapabilities.GeneralPurpose,
                "ethan-staging", "freight-company-stable", Day));
            s.Horse.TransportState = HorseTransportState.AttachedToWagon;
            s.Horse.AttachedWagonAssetId = WagonId;
            s.Wagon = new EquipmentAsset
            {
                AssetId = WagonId, Kind = "freight-wagon", DisplayName = "Opening Freight Wagon",
                Condition01 = 1f, OwnerKind = "business", OwnerId = CarrierId,
                LocationId = "ethan-staging", HitchPreparationShipmentId = ShipmentId,
                HitchingAnimalIds = new List<string> { s.Horse.AnimalId.ToString() }
            };
            Assert.IsNull(WagonAuthority.Configure(s.Wagon, WagonSubtype.GeneralFarmFreight.ToString(),
                450f, 4f, "ethan-staging"));
            s.CarrierRuntime = Runtime(CarrierId, 0);
            s.PayerRuntime = Runtime(BakeryId, 20000);
            s.Freight = new FreightProductionDriver();
            s.Freight.EnsureCompany(CarrierId, s.CarrierRuntime);
            s.Freight.RegisterBusinessRuntime(BakeryId, s.PayerRuntime);
            s.Freight.Service.RegisterWagon(s.Wagon);
            s.Hooks = new FreightShipmentHooks
            {
                Tasks = s.Tasks,
                Budgets = s.Budgets,
                DayIndex = Day,
                Diagnostics = new List<string>(),
                Reservations = s.Reservations,
                NowGameSeconds = Day * 86400f + 8 * 3600f,
                CommitSourceLot = (LogisticsShipmentState shipment, int qty, out string reason) =>
                    s.Adapter.CommitSourceLot(shipment, qty, out reason),
                ReceiveCargoLot = (LogisticsShipmentState shipment, int qty, out string reason) =>
                    s.Adapter.ReceiveCargoLot(shipment, qty, Day, out reason),
            };
            s.JourneyModel = new JourneyModel();
            Assert.IsNull(s.JourneyModel.RegisterLocation(new JourneyLocation("town-center", JourneyLocationKind.Waypoint, "Town Centre", 0f, 0f)));
            RegionalTownRegistry.ApplyAll(s.JourneyModel, new List<string>());
            s.Journeys = new OffMapJourneyAuthority(s.JourneyModel);

            // Bakery production + store (mirrors BakeryFoodChainEndToEndTests.NewSlice).
            s.StoreRuntime.BindBusinessIdentity(StoreInstanceId);
            s.StoreRuntime.AddCashCents(5000);
            s.StoreRuntime.EnsureCategoryStock("staple_food", 0, 1000);
            s.StoreRuntime.EnsureCategoryStock("animal_feed", 0, 0);
            s.BakeryRuntime.BindBusinessIdentity(BakeryId);
            s.BakeryRuntime.AddCashCents(2000);
            s.StorePort = new RuntimeStorePort { Runtime = s.StoreRuntime };
            s.StorePort.Prices["staple_food"] = 5;
            s.StorePort.OfferList.Add(new GeneralStoreCategoryOffer
            {
                CategoryId = "staple_food",
                OfferedItemIds = new List<string> { HouseholdItemCatalog.BreadId },
                StoreUnitsPerItemUnit = 1,
            });
            s.ProductionDriver = new Series2BakeryProductionDriver(
                s.Bakery, new Series2InStockBakeryFuelSource(s.Bakery));

            // Shopping (mirrors BakeryFoodChainEndToEndTests.NewSlice).
            s.Population = new PopulationState();
            s.Population.people.Add(new PersonState
            {
                id = ShopperPersonId, firstName = "Glen", lastName = "Test", age = 34,
                ageBand = AgeBand.Adult18Plus, householdId = HouseholdId,
            });
            s.Population.households.Add(new HouseholdState { id = HouseholdId });
            s.Membership = new HouseholdMembershipRegistry();
            Assert.IsNull(s.Membership.Register(new HouseholdMembership
            {
                MembershipId = 1, PersonId = ShopperPersonId, HouseholdId = HouseholdId, StartDayIndex = 0,
            }));
            s.Ledgers = new HouseholdLedgerRegistry();
            s.Inventories = new HouseholdInventoryRegistry();
            s.Needs = new HouseholdNeedRegistry();
            s.ShoppingJourneys = new JourneyModel();
            s.ShoppingJourneys.RegisterLocation(new JourneyLocation("home", JourneyLocationKind.Farmstead, "Test Home", 0f, 0f));
            s.ShoppingJourneys.RegisterLocation(new JourneyLocation("general-store", JourneyLocationKind.Store, "General Store", 2f, 0f));
            Assert.IsNull(s.ShoppingJourneys.AddEdge("home", "general-store", 2.0f, "river road"));
            // One shared TaskAuthority serves the freight service and the
            // shopping loop, as in production.
            HouseholdShoppingLoop.RegisterTaskDefinitions(s.Tasks);
            s.Tracker = new PersonScheduleTracker();
            s.Knowledge = new ShopperSellerKnowledge();
            s.Directory = new SupplierDirectory();
            GeneralStoreSupplierAdapter.RegisterIn(s.Directory, s.StorePort);
            s.Knowledge.LearnSupplier(ShopperPersonId, StoreInstanceId, "lives in the same town", 0);
            s.Loop = new HouseholdShoppingLoop(
                s.Population, s.Membership, s.Ledgers, s.Inventories, s.Needs,
                s.Directory, s.ShoppingJourneys, s.Tasks, s.Budgets, s.Tracker, s.Knowledge,
                new FinancialObligationAuthority(), s.Ids);
            s.Loop.CustodyLog = log;
            s.PersonLocations[ShopperPersonId] = "home";
            return s;
        }

        private static void PlaceFreightOrder(Chain s)
        {
            Assert.IsNull(s.Supplier.TryPlaceOrder(
                OrderRequestId, BakeryId, "Glen Pierce", Sacks, s.BakeryCash, 0, out OffMapAidCargoLot lot),
                "fixture order must place");
            s.Lot = lot;
            Assert.AreEqual(LotId, lot.LotId);
            Assert.AreEqual(Sacks, lot.PackageUnits);
            Assert.AreEqual(Sacks * 10, lot.InventoryUnits, "16 sacks x 10 lb");
            Assert.AreEqual(Sacks * 48, lot.TotalCostCents, "16 sacks x 48c");
        }

        private static void BuildFreightShipment(Chain s)
        {
            FreightServiceAgreementBook book = s.Freight.ServiceAgreements;
            var request = new FreightServiceRequest
            {
                RequestId = "request-1", RequestingBusinessId = BakeryId, CarrierBusinessId = CarrierId,
                PickupBusinessId = "millbrook_grain_co", DestinationBusinessId = BakeryId,
                CargoLotId = LotId, Quantity = Sacks, QuantityUnit = "lb", RequestingPersonId = 2,
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
            Assert.IsNull(book.Accept(request.RequestId, EthanId, Day, 40000f, out s.Agreement));
            s.Shipment = s.Freight.CreateShipment(CarrierId, ShipmentId, "millbrook_grain_co", BakeryId, Sacks,
                new FreightCargoSpec { MassKgPerUnit = 4.54f, VolumeM3PerUnit = 0.02f }, WagonId,
                s.Hooks, EntityId.For(EntityKind.Person, EthanId), new List<EntityId> { s.Horse.AnimalId },
                FreightChargeCents, BakeryId, s.Diag, out string shipmentRefusal,
                AgreementId, LotId, MillbrookFlourFreightCustodyAdapter.FlourSkuId, true);
            Assert.IsNotNull(s.Shipment, shipmentRefusal);
            Assert.IsNull(book.LinkShipment(s.Agreement.AgreementId, ShipmentId));
            Assert.IsNull(book.MarkPreparedForDispatch(s.Agreement.AgreementId, ShipmentId, 50000f));
            s.Freight.SetDispatchPlan(ShipmentId, EntityId.For(EntityKind.Person, EthanId),
                new List<EntityId> { s.Horse.AnimalId });
        }

        private static void BindAndAuthorize(Chain s)
        {
            string bind = WagonAuthority.TryAssignDraftTeam(s.Wagon,
                new List<AnimalState> { s.Horse },
                EntityId.For(EntityKind.Person, EthanId).ToString(), ShipmentId,
                new AnimalAvailabilityAuthority(s.Reservations), Day * 86400f + 8 * 3600f, float.MaxValue);
            Assert.IsNull(bind, "fixture bind must succeed");
            Assert.IsTrue(s.Freight.AuthorizeShipmentDispatch(ShipmentId, out _), "fixture authorization must succeed");
        }

        private static List<LogisticsShipmentStatus> TickToCompleted(Chain s)
        {
            var trace = new List<LogisticsShipmentStatus>();
            for (int tick = 0; tick < 8 && s.Shipment.Status != LogisticsShipmentStatus.Completed; tick++)
            {
                s.Freight.AdvanceTick(86400f, s.Hooks, EntityId.For(EntityKind.Person, EthanId), s.Diag);
                trace.Add(s.Shipment.Status);
            }
            return trace;
        }

        // The normal production path up to the return-day tick loop: journey
        // departed day 10, lot committed at the supplier on D+1 (day 11)
        // through the adapter — the same call the pickup drive makes —
        // team bound and authorized, journey returned on its due day, the
        // off-map hold lifted.
        private static void DriveFreightToReturnDay(Chain s)
        {
            OffMapJourneyRecord record = s.Journeys.DepartVehicle(
                WagonId, "town-center", "Millbrook Grain Co.", 0,
                "Millbrook flour pickup", null,
                new List<string> { AgreementId },
                10, null, null, s.Diag);
            Assert.IsNotNull(record, "Millbrook departure should succeed: " + string.Join("; ", s.Diag));
            record.PurchaseManifestUnits[MillbrookFlourFreightCustodyAdapter.FlourSkuId] = Sacks;

            // The D+1 pickup's first half, as the drive performs it: commit
            // the lot through the adapter (day 11), then apply the loading
            // writes — exactly WorkLoading's post-commit writes, no task.
            Assert.IsTrue(s.Adapter.CommitSourceLot(s.Shipment, s.Shipment.PlannedQuantityUnits, out string reason, 11),
                reason);
            s.Shipment.sourceLotDebitApplied = true;
            s.Shipment.loadApplied = true;
            s.Shipment.sourceCommittedAtSchedule = true;
            s.Shipment.remainingQuantityUnits = s.Shipment.PlannedQuantityUnits;
            s.Shipment.physicalLocationId = "wagon:" + WagonId;

            BindAndAuthorize(s);
            List<OffMapJourneyRecord> returned = s.Journeys.CompleteDueJourneys(Day, null, s.Diag);
            Assert.AreEqual(1, returned.Count, "the journey must complete exactly once on its due day");
            Assert.AreEqual(OffMapJourneyState.Returned, record.State);
            s.Hooks.IsVehicleOffMap = _ => false;
        }

        private static void InstallOven(Chain s)
        {
            Assert.IsNull(s.Bakery.AddOven("bakehouse", new List<string>
            {
                "oven-chamber-1", "kneading-table-1", "proofing-rack-1", "bake-peels-1",
            }, s.Diag), "oven installation should succeed");
        }

        private static void SeedNotionsAndFuel(Chain s)
        {
            Assert.IsNull(s.Bakery.FlourStock.ReceiveLot(new BakeryFlourLot
            {
                LotId = s.Ids.Allocate(EntityKind.Lot),
                FlourName = BakeryBreadCatalog.NotionsItemId,
                Units = 2,
                AcquiredDayIndex = 0,
                ImportOrderId = "test-notions-order",
                OriginName = "Test notions",
            }, s.Diag));
            Assert.IsNull(s.Bakery.FuelStock.ReceiveLot(new BakeryFuelLot
            {
                LotId = s.Ids.Allocate(EntityKind.Lot),
                FuelName = BakeryBreadCatalog.FuelItemId,
                Units = 2,
                AcquiredDayIndex = 0,
                ImportOrderId = "test-fuel-order",
                OriginName = "Test fuel",
                IsBootstrapEndowment = false,
            }, s.Diag));
        }

        private static void BookAccount(Chain s, int dueDay)
        {
            var account = new BakeryWholesaleAccount
            {
                BuyerBusinessId = StoreInstanceId,
                BuyerType = BusinessType.GeneralStore,
                ProductId = BakeryBreadCatalog.BreadLoafId,
                UnitsPerDelivery = 24,
                DeliveryCadenceDays = 1,
                PricePerUnitCents = 2,
                Freshness = BakeryWholesaleFreshness.FreshOrDayOld,
                NextDeliveryDayIndex = dueDay,
            };
            Assert.IsNull(s.Bakery.AddWholesaleAccount(account, s.Diag), "account booking should succeed");
        }

        private static BakeryWholesaleDailyInputs InputsFor(Chain s, int dayIndex) =>
            new BakeryWholesaleDailyInputs
            {
                Bakery = s.Bakery,
                ProductionDriver = s.ProductionDriver,
                DayIndex = dayIndex,
                BakerKitUsable = true,
                OvenDefinition = WorkstationCatalog.BakeOven,
                FindOvenComponent = OvenFinder(),
                IdRegistry = s.Ids,
                StorePort = s.StorePort,
                StoreCashPort = new Series2BusinessRuntimeCashPort(s.StoreRuntime),
                BakeryCashPort = new Series2BusinessRuntimeCashPort(s.BakeryRuntime),
                StoreBusinessId = StoreInstanceId,
                BakeryBusinessId = BakeryId,
                Intake = s.Intake,
                StoreCatalog = s.Catalog,
                StoreRuntimeState = s.StoreRuntime,
                BakeryRuntimeState = s.BakeryRuntime,
                StoreFlourUnitPriceCents = 29,
                MillbrookQuote = new MillbrookFlourQuote
                {
                    SacksAvailable = 0,
                    GoodsPricePerSackCents = null,
                    OneWayMiles = 50f,
                    FreightQuoteCents = 125,
                    FreightAvailable = false,
                    FreightUnavailableReason = "freight blocked: no Millbrook shipment has departed",
                },
                MillbrookSupplier = null,
                ImportFlourPricePerLbCents = 3,
                ImportTransitDays = 14,
                ImportOrderPathExists = false,
            };

        private static ShoppingLoopOptions ShoppingOptions(Chain s) =>
            new ShoppingLoopOptions
            {
                PersonLocationId = pid => s.PersonLocations.TryGetValue(pid, out string loc) ? loc : "home",
                SetPersonLocation = (pid, loc) => s.PersonLocations[pid] = loc,
                DepartureMinuteOfDay = 600,
                InStoreMinutes = 15,
                Diagnostics = s.Diag,
            };

        private static void FundHousehold(Chain s, int cents)
        {
            HouseholdLedger ledger = s.Ledgers.GetOrCreate(HouseholdId);
            Assert.IsNull(ledger.RecordInflow(0, cents, HouseholdIncomeSource.OwnerContribution,
                "test-capital", "test funding", "test"));
        }

        private static ShoppingTripResult BuyBread(Chain s, int units, int dayIndex)
        {
            HouseholdPurchasingNeed need = s.Needs.CreateNeed(HouseholdId, HouseholdItemCatalog.BreadId, dayIndex);
            need.UnitsNeeded = units;
            need.Urgency01 = 0.9f;
            need.ReasonSummary = "test need: bread for meals";
            return s.Loop.ExecuteNeed(need, dayIndex, ShoppingOptions(s));
        }

        private static void AssertKindsInOrder(CustodyTransferLog log, params string[] expectedKinds)
        {
            Assert.AreEqual(expectedKinds.Length, log.Count,
                "one record per transfer, in order; got: " + string.Join(", ", LogKinds(log)));
            for (int i = 0; i < expectedKinds.Length; i++)
            {
                Assert.AreEqual(expectedKinds[i], log.Records[i].CauseKind,
                    $"record {i} must be {expectedKinds[i]}; got: " + string.Join(", ", LogKinds(log)));
            }
        }

        private static IEnumerable<string> LogKinds(CustodyTransferLog log)
        {
            foreach (CustodyTransferRecord record in log.Records) yield return record.CauseKind;
        }

        private static CustodyTransferRecord RecordAt(CustodyTransferLog log, int index) => log.Records[index];

        // The flour bin holds flour AND notions lots: find the flour lot by
        // name instead of indexing Lots[0].
        private static BakeryFlourLot FlourLot(Chain s)
        {
            BakeryFlourLot found = null;
            int flourCount = 0;
            foreach (BakeryFlourLot lot in s.Bakery.FlourStock.Lots)
            {
                if (lot != null && string.Equals(lot.FlourName, BakeryBreadCatalog.FlourItemId, StringComparison.Ordinal))
                {
                    flourCount++;
                    found = lot;
                }
            }
            Assert.AreEqual(1, flourCount, "exactly one flour lot is expected in the bin");
            Assert.IsNotNull(found);
            return found;
        }

        [Test]
        public void Chain_MillbrookOrderToHouseholdReceipt()
        {
            var log = new CustodyTransferLog();
            Chain s = NewChain(log);
            InstallOven(s);
            SeedNotionsAndFuel(s);
            BookAccount(s, Day);

            // Leg 1 — Millbrook order: 16 sacks, 160 lb, 768c (case R).
            PlaceFreightOrder(s);
            Assert.AreEqual(20000 - Sacks * 48, s.BakeryCash.BalanceCents, "order debits the buyer once");
            Assert.AreEqual(Sacks * 48, s.Supplier.CashCents, "order credits the supplier once");
            Assert.AreEqual(230 - Sacks * 10, s.Supplier.GetInventoryUnits("bakery_flour_sack"));
            Assert.AreEqual(1, log.Count, "the order records once, at placement");
            Assert.AreEqual("custody-1", RecordAt(log, 0).RecordId);

            // Legs 2-3 — freight: commit day 11, receive day 12, 125c settles once (case R).
            BuildFreightShipment(s);
            int payerBefore = s.PayerRuntime.CurrentCashCents;
            int carrierBefore = s.CarrierRuntime.CurrentCashCents;
            DriveFreightToReturnDay(s);
            Assert.AreEqual(2, log.Count, "the commit records once, at the pickup");
            List<LogisticsShipmentStatus> trace = TickToCompleted(s);
            Assert.AreEqual(LogisticsShipmentStatus.Completed, s.Shipment.Status,
                "shipment did not complete; trace: " + string.Join(", ", trace));
            Assert.AreEqual(3, log.Count, "the receipt records once, at delivery");
            Assert.IsTrue(s.Shipment.deliveryApplied);
            Assert.IsTrue(s.Shipment.FreightRevenueCollected);
            Assert.AreEqual(payerBefore - FreightChargeCents, s.PayerRuntime.CurrentCashCents,
                "freight charge debited from the bakery exactly once");
            Assert.AreEqual(carrierBefore + FreightChargeCents, s.CarrierRuntime.CurrentCashCents,
                "freight charge credited to the carrier exactly once");
            Assert.AreEqual(160, FlourLot(s).Units, "the delivered lot holds the lot's 160 lb");
            Assert.AreEqual(LotId, FlourLot(s).ImportOrderId);
            Assert.IsTrue(s.Lot.Received, "the paid Millbrook lot is marked received");

            // Legs 4-5 — bake: 12 lb flour + 1 notion into the batch, 24 loaves out (case R).
            BakeryWholesaleDailyResult baked = BakeryWholesaleDailyDriver.RunDay(InputsFor(s, Day), s.Diag);
            Assert.AreEqual(1, baked.BatchesBaked, "one batch bakes; diag: " + string.Join(" | ", s.Diag));
            Assert.AreEqual(1, baked.TransfersCompleted);
            Assert.AreEqual(24, baked.UnitsStocked, "24 loaves stocked to the live staple_food pool");
            Assert.AreEqual(148, s.Bakery.FlourStock.UnitsOnHand(BakeryBreadCatalog.FlourItemId),
                "flour 160 -> 148 lb: the partial leg preserves the total (12 dispensed + 148 remaining, case M)");
            Assert.AreEqual(24, s.StoreRuntime.GetCategoryStock("staple_food").CurrentStockUnits);
            Assert.AreEqual(2048, s.BakeryRuntime.CurrentCashCents, "bakery 2,000 + 48c wholesale");
            Assert.AreEqual(4952, s.StoreRuntime.CurrentCashCents, "store 5,000 - 48c wholesale");
            Assert.AreEqual(6, log.Count, "dispense (flour, notions) and bake record once each");

            // Leg 6 — retail receipt: 20 feed sacks from the named-SKU lot into retail.
            var market = new ExternalMarketAuthority();
            var feedSupplier = new ExternalCounterparty
            {
                CounterpartyId = "test-feed-supplier",
                DisplayName = "Test Feed Supplier",
                InitialBudgetCents = 50000,
                TransitDays = 0,
            };
            feedSupplier.SupplyUnits["store_horse_feed_sack"] = 20;
            Assert.IsNull(market.RegisterCounterparty(feedSupplier));
            var procurement = new OffMapNamedSkuProcurementService(
                StoreInstanceId, market, new Series2BusinessRuntimeCashPort(s.StoreRuntime));
            NamedSkuOrder feedOrder = procurement.PlaceOrder(
                "store_horse_feed_sack", 20, 15, 0, feedSupplier.CounterpartyId, Day,
                NamedSkuPaymentMode.CashOnArrival, null);
            Assert.IsNotNull(feedOrder);
            Assert.AreEqual(1, procurement.ReceiveArrivals(feedOrder.ExpectedArrivalDayIndex, null).Count);
            Assert.AreEqual(4652, s.StoreRuntime.CurrentCashCents, "store 4,952 - 300c feed on arrival");
            NamedSkuLot sourceLot = procurement.Lots[0];
            PurchasedRetailLotReceipt.Result receipt = PurchasedRetailLotReceipt.Transfer(
                procurement, s.StoreRuntime, sourceLot, "animal_feed", "sack", Day, log);
            Assert.IsTrue(receipt.Received, receipt.Refusal);
            Assert.AreEqual(20, receipt.Units);
            Assert.AreEqual(0, sourceLot.RemainingUnits);
            Assert.AreEqual(20, s.StoreRuntime.GetRetailItemStockUnits("store_horse_feed_sack", "animal_feed"));
            Assert.AreEqual(7, log.Count, "the retail receipt records once");

            // Legs 7-8 — household purchase and receipt: the household needs
            // 30 loaves, the store holds 24 — a partial fill (case M).
            FundHousehold(s, 1000);
            ShoppingTripResult trip = BuyBread(s, 30, Day);
            Assert.IsTrue(trip.Success, "purchase succeeds; failure: " + trip.FailureReason + "; diag: " + string.Join(" | ", s.Diag));
            Assert.AreEqual(24, trip.UnitsAcquired, "the partial fill sells the 24 loaves on hand");
            Assert.AreEqual(120, trip.BuyerOutflowCents, "24 loaves x 5c");
            Assert.IsTrue(!string.IsNullOrWhiteSpace(trip.PartialFillReason),
                "the partial fill names its reason");
            Assert.AreEqual(0, s.StoreRuntime.GetCategoryStock("staple_food").CurrentStockUnits, "live pool 24 -> 0");
            Assert.AreEqual(880, s.Ledgers.GetOrCreate(HouseholdId).GetBalanceCents(), "household 1,000 - 120c");
            Assert.AreEqual(4772, s.StoreRuntime.CurrentCashCents, "store 4,652 + 120c retail");
            Assert.AreEqual(24, s.Inventories.GetOrCreate(HouseholdId).GetAvailableUnits(HouseholdItemCatalog.BreadId),
                "24 bread units in household inventory: bought = received, the partial total is preserved");
            Assert.AreEqual(9, log.Count, "one record per transfer, in order");

            // The chain in order, with matching lot keys and custodians (case E).
            AssertKindsInOrder(log,
                CustodyTransferLog.CauseMillbrookOrder,
                CustodyTransferLog.CauseFreightCommit,
                CustodyTransferLog.CauseFreightReceive,
                CustodyTransferLog.CauseBakeryDispense,
                CustodyTransferLog.CauseBakeryDispense,
                CustodyTransferLog.CauseBakeryBake,
                CustodyTransferLog.CauseRetailReceipt,
                CustodyTransferLog.CauseRetailPurchase,
                CustodyTransferLog.CauseHouseholdReceipt);

            var millbrookKey = new LotKey(CustodyTransferLog.MillbrookAuthority("millbrook_grain_co"), LotId);
            string flourLotId = FlourLot(s).LotId.ToString();
            var bakeryFlourKey = new LotKey(CustodyTransferLog.BakeryAuthority(BakeryId), flourLotId);

            CustodyTransferRecord order = RecordAt(log, 0);
            Assert.IsTrue(order.SourceLotKey.IsEmpty, "the order is a creation event: no source lot");
            Assert.AreEqual(millbrookKey, order.DestinationLotKey);
            Assert.AreEqual("supplier:millbrook_grain_co", order.FromCustodian);
            Assert.AreEqual("supplier:millbrook_grain_co", order.ToCustodian);
            Assert.AreEqual("millbrook_grain_co:" + OrderRequestId, order.CauseId);
            Assert.AreEqual(Sacks, order.Units);
            Assert.AreEqual(0, order.DayIndex);

            CustodyTransferRecord commit = RecordAt(log, 1);
            Assert.AreEqual(millbrookKey, commit.SourceLotKey);
            Assert.IsTrue(commit.DestinationLotKey.IsEmpty, "wagon custody is not a lot");
            Assert.AreEqual("supplier:millbrook_grain_co", commit.FromCustodian);
            Assert.AreEqual("wagon:" + WagonId, commit.ToCustodian);
            Assert.AreEqual(ShipmentId, commit.CauseId);
            Assert.AreEqual(Sacks, commit.Units);
            Assert.AreEqual(11, commit.DayIndex);

            CustodyTransferRecord receive = RecordAt(log, 2);
            Assert.AreEqual(millbrookKey, receive.SourceLotKey);
            Assert.AreEqual(bakeryFlourKey, receive.DestinationLotKey);
            Assert.AreEqual("wagon:" + WagonId, receive.FromCustodian);
            Assert.AreEqual("bakery:" + BakeryId, receive.ToCustodian);
            Assert.AreEqual(ShipmentId, receive.CauseId);
            Assert.AreEqual(160, receive.Units, "the receipt moves pounds, not sacks");
            Assert.AreEqual(Day, receive.DayIndex);

            CustodyTransferRecord dispenseFlour = RecordAt(log, 3);
            Assert.AreEqual(bakeryFlourKey, dispenseFlour.SourceLotKey, "the dispense names the Millbrook flour lot");
            Assert.IsTrue(dispenseFlour.DestinationLotKey.IsEmpty, "batch custody is not a lot");
            Assert.AreEqual("bakery:" + BakeryId, dispenseFlour.FromCustodian);
            Assert.AreEqual(12, dispenseFlour.Units);
            string batchId = dispenseFlour.ToCustodian.Substring("batch:".Length);
            Assert.IsTrue(!string.IsNullOrWhiteSpace(batchId), "the dispense names its batch custodian");

            CustodyTransferRecord dispenseNotions = RecordAt(log, 4);
            Assert.AreEqual("batch:" + batchId, dispenseNotions.ToCustodian, "flour and notions go to the same batch");
            Assert.AreEqual(1, dispenseNotions.Units);
            Assert.AreEqual(BakeryBreadCatalog.NotionsItemId, dispenseNotions.ItemId);

            string breadLotId = s.Bakery.BreadShelf.Count > 0
                ? s.Bakery.BreadShelf[0].LotId.ToString()
                : s.Bakery.WholesaleDeliveries[0].SellerLotIds[0];
            CustodyTransferRecord bake = RecordAt(log, 5);
            Assert.AreEqual(new LotKey(CustodyTransferLog.BakeryAuthority(BakeryId), "batch:" + batchId), bake.SourceLotKey);
            Assert.AreEqual(new LotKey(CustodyTransferLog.BakeryAuthority(BakeryId), breadLotId), bake.DestinationLotKey);
            Assert.AreEqual("batch:" + batchId, bake.FromCustodian);
            Assert.AreEqual("bakery:" + BakeryId, bake.ToCustodian);
            Assert.AreEqual(24, bake.Units, "12 lb bakes 24 loaves");
            Assert.AreEqual(batchId, bake.CauseId.Substring("bake:".Length), "the bake's key is its batch");

            CustodyTransferRecord retailReceipt = RecordAt(log, 6);
            Assert.AreEqual(new LotKey(CustodyTransferLog.NamedSkuAuthority(StoreInstanceId), sourceLot.LotId), retailReceipt.SourceLotKey);
            Assert.AreEqual(new LotKey(CustodyTransferLog.BusinessAuthority(StoreInstanceId), receipt.RetailLotId), retailReceipt.DestinationLotKey);
            Assert.AreEqual("supplier:test-feed-supplier", retailReceipt.FromCustodian);
            Assert.AreEqual("business:" + StoreInstanceId, retailReceipt.ToCustodian);
            Assert.AreEqual(20, retailReceipt.Units);
            Assert.AreEqual(receipt.RetailLotId, retailReceipt.CauseId);

            CustodyTransferRecord purchase = RecordAt(log, 7);
            Assert.IsTrue(purchase.SourceLotKey.IsEmpty, "the category-pool sale carries no retail lot id");
            Assert.AreEqual("business:" + StoreInstanceId, purchase.FromCustodian);
            Assert.AreEqual("person:" + ShopperPersonId, purchase.ToCustodian);
            Assert.AreEqual(24, purchase.Units);
            Assert.IsTrue(!string.IsNullOrWhiteSpace(purchase.CauseId), "the purchase is keyed by its receipt id");

            CustodyTransferRecord householdReceipt = RecordAt(log, 8);
            Assert.AreEqual(purchase.DestinationLotKey, householdReceipt.SourceLotKey,
                "the receipt's source lot is the purchase's destination lot — the chain links");
            Assert.IsTrue(householdReceipt.DestinationLotKey.IsEmpty,
                "the household lot id is not returned by the receipt path; the missing key is named in diagnostics");
            Assert.AreEqual("person:" + ShopperPersonId, householdReceipt.FromCustodian);
            Assert.AreEqual("household:" + HouseholdId, householdReceipt.ToCustodian);
            Assert.AreEqual(24, householdReceipt.Units);
            Assert.AreEqual(purchase.DestinationLotKey.LotId, householdReceipt.CauseId,
                "the receipt is keyed by the carried batch");

            // "Who holds this lot?" at every step (case E).
            Assert.AreEqual("bakery:" + BakeryId, log.LatestForLot(millbrookKey).ToCustodian,
                "after the full chain the latest Millbrook-lot record is the receipt into the Bakery");
            Assert.AreEqual("batch:bakery_001-batch-0", log.LatestForLot(bakeryFlourKey).ToCustodian,
                "the flour lot's latest record is the dispense into the dough batch");
            Assert.AreEqual("household:" + HouseholdId,
                log.LatestForLot(purchase.DestinationLotKey).ToCustodian,
                "the carried batch ends in household custody");
            Assert.AreEqual(2, log.Diagnostics.Count,
                "a clean chain names exactly two missing keys in diagnostics (retail-purchase source lot, household-receipt destination lot); diag: " + string.Join(" | ", log.Diagnostics));
            Assert.IsTrue(string.Join(" | ", log.Diagnostics).Contains("no source lot key"),
                "the retail-purchase missing source lot key is named: " + string.Join(" | ", log.Diagnostics));
            Assert.IsTrue(string.Join(" | ", log.Diagnostics).Contains("no destination lot key"),
                "the household-receipt missing destination lot key is named: " + string.Join(" | ", log.Diagnostics));
        }

        [Test]
        public void NoDoubleTransfer_DuplicateReceiptsRecordOnce()
        {
            var log = new CustodyTransferLog();
            Chain s = NewChain(log);
            PlaceFreightOrder(s);
            s.Shipment = new LogisticsShipmentState
            {
                shipmentId = ShipmentId,
                sourceBusinessInstanceId = s.Supplier.BusinessId,
                destinationBusinessInstanceId = s.Bakery.BusinessInstanceId,
                cargoLotId = s.Lot.LotId,
                cargoSkuId = MillbrookFlourFreightCustodyAdapter.FlourSkuId,
                plannedQuantityUnits = s.Lot.PackageUnits,
                requiresSourceLotCustody = true,
                physicalWagonAssetId = WagonId,
            };

            // Freight: commit twice and receive twice. Both repeats are
            // idempotent in the adapter — and the log records each transfer
            // once (case F).
            Assert.IsTrue(s.Adapter.CommitSourceLot(s.Shipment, Sacks, out string commitReason, Day), commitReason);
            Assert.IsTrue(s.Adapter.CommitSourceLot(s.Shipment, Sacks, out string recommitReason, Day), recommitReason);
            Assert.IsTrue(s.Adapter.ReceiveCargoLot(s.Shipment, Sacks, Day, out string receiveReason), receiveReason);
            Assert.IsTrue(s.Adapter.ReceiveCargoLot(s.Shipment, Sacks, Day, out string rereceiveReason), rereceiveReason);
            Assert.AreEqual(160, FlourLot(s).Units, "still exactly one bakery flour lot at 160 lb");
            Assert.AreEqual(3, log.Count, "order + one commit + one receive");
            AssertKindsInOrder(log,
                CustodyTransferLog.CauseMillbrookOrder,
                CustodyTransferLog.CauseFreightCommit,
                CustodyTransferLog.CauseFreightReceive);

            // Retail: Transfer twice. The second is refused as a duplicate
            // by Transfer itself — and the log holds one retail-receipt
            // record (case F).
            var market = new ExternalMarketAuthority();
            var feedSupplier = new ExternalCounterparty
            {
                CounterpartyId = "test-feed-supplier",
                DisplayName = "Test Feed Supplier",
                InitialBudgetCents = 50000,
                TransitDays = 0,
            };
            feedSupplier.SupplyUnits["store_horse_feed_sack"] = 20;
            Assert.IsNull(market.RegisterCounterparty(feedSupplier));
            s.StoreRuntime.BindBusinessIdentity(StoreInstanceId);
            s.StoreRuntime.AddCashCents(5000);
            s.StoreRuntime.EnsureCategoryStock("animal_feed", 0, 0);
            var procurement = new OffMapNamedSkuProcurementService(
                StoreInstanceId, market, new Series2BusinessRuntimeCashPort(s.StoreRuntime));
            NamedSkuOrder feedOrder = procurement.PlaceOrder(
                "store_horse_feed_sack", 20, 15, 0, feedSupplier.CounterpartyId, Day,
                NamedSkuPaymentMode.CashOnArrival, null);
            Assert.IsNotNull(feedOrder);
            Assert.AreEqual(1, procurement.ReceiveArrivals(feedOrder.ExpectedArrivalDayIndex, null).Count);
            NamedSkuLot sourceLot = procurement.Lots[0];
            PurchasedRetailLotReceipt.Result first = PurchasedRetailLotReceipt.Transfer(
                procurement, s.StoreRuntime, sourceLot, "animal_feed", "sack", Day, log);
            Assert.IsTrue(first.Received, first.Refusal);
            PurchasedRetailLotReceipt.Result duplicate = PurchasedRetailLotReceipt.Transfer(
                procurement, s.StoreRuntime, sourceLot, "animal_feed", "sack", Day, log);
            Assert.IsTrue(!duplicate.Received, "the duplicate receipt must be refused");
            Assert.IsTrue(duplicate.Duplicate, "the refusal names itself a duplicate");
            Assert.AreEqual(20, s.StoreRuntime.GetRetailItemStockUnits("store_horse_feed_sack", "animal_feed"),
                "the duplicate adds no stock");
            Assert.AreEqual(4, log.Count, "one retail-receipt record despite two Transfer calls");
            Assert.AreEqual(CustodyTransferLog.CauseRetailReceipt, RecordAt(log, 3).CauseKind);
            Assert.AreEqual(first.RetailLotId, RecordAt(log, 3).CauseId);
        }

        [Test]
        public void NullLog_TransfersCompleteUnchanged()
        {
            Chain s = NewChain(null);
            InstallOven(s);
            SeedNotionsAndFuel(s);
            BookAccount(s, Day);
            PlaceFreightOrder(s);
            BuildFreightShipment(s);
            DriveFreightToReturnDay(s);
            List<LogisticsShipmentStatus> trace = TickToCompleted(s);
            Assert.AreEqual(LogisticsShipmentStatus.Completed, s.Shipment.Status,
                "shipment completes with no log; trace: " + string.Join(", ", trace));
            Assert.AreEqual(160, FlourLot(s).Units);
            Assert.IsTrue(s.Shipment.FreightRevenueCollected, "the 125c settles with no log");

            BakeryWholesaleDailyResult baked = BakeryWholesaleDailyDriver.RunDay(InputsFor(s, Day), s.Diag);
            Assert.AreEqual(1, baked.BatchesBaked, "one batch bakes with no log; diag: " + string.Join(" | ", s.Diag));
            Assert.AreEqual(24, baked.UnitsStocked);
            Assert.AreEqual(148, s.Bakery.FlourStock.UnitsOnHand(BakeryBreadCatalog.FlourItemId));

            FundHousehold(s, 1000);
            ShoppingTripResult trip = BuyBread(s, 6, Day);
            Assert.IsTrue(trip.Success, "purchase succeeds with no log; failure: " + trip.FailureReason);
            Assert.AreEqual(6, trip.UnitsAcquired);
            Assert.AreEqual(6, s.Inventories.GetOrCreate(HouseholdId).GetAvailableUnits(HouseholdItemCatalog.BreadId));
        }

        [Test]
        public void OwnershipStaysOnLotAuthority_ReceiptBooksHouseholdNotShopper()
        {
            var log = new CustodyTransferLog();
            Chain s = NewChain(log);
            InstallOven(s);
            SeedNotionsAndFuel(s);
            BookAccount(s, Day);
            PlaceFreightOrder(s);
            BuildFreightShipment(s);
            DriveFreightToReturnDay(s);
            List<LogisticsShipmentStatus> trace = TickToCompleted(s);
            Assert.AreEqual(LogisticsShipmentStatus.Completed, s.Shipment.Status,
                "shipment did not complete; trace: " + string.Join(", ", trace));
            BakeryWholesaleDailyResult baked = BakeryWholesaleDailyDriver.RunDay(InputsFor(s, Day), s.Diag);
            Assert.AreEqual(1, baked.BatchesBaked, "one batch bakes; diag: " + string.Join(" | ", s.Diag));

            FundHousehold(s, 1000);
            ShoppingTripResult trip = BuyBread(s, 6, Day);
            Assert.IsTrue(trip.Success, "purchase succeeds; failure: " + trip.FailureReason);
            HouseholdInventory inventory = s.Inventories.GetOrCreate(HouseholdId);
            HouseholdItemLot breadLot = null;
            foreach (HouseholdItemLot lot in inventory.Lots)
            {
                if (lot != null && string.Equals(lot.ItemId, HouseholdItemCatalog.BreadId, StringComparison.Ordinal))
                    breadLot = lot;
            }
            Assert.IsNotNull(breadLot, "a bread lot exists in the household inventory");
            Assert.AreEqual(-1, breadLot.OwnerPersonId,
                "the loop receipt path books the received lot to the household (OwnerPersonId -1), never to the shopper — ownership is an owner decision, not something the log assigns");
            Assert.AreEqual(6, breadLot.QuantityUnits);

            CustodyTransferRecord receipt = log.LatestForCause(
                CustodyTransferLog.CauseHouseholdReceipt, log.Records[log.Count - 1].CauseId);
            Assert.IsNotNull(receipt, "the receipt is in the log");
            Assert.AreEqual("household:" + HouseholdId, receipt.ToCustodian,
                "the log records custody (household holds it), not ownership");
            Assert.AreEqual("person:" + ShopperPersonId, receipt.FromCustodian);
        }

        [Test]
        public void ServicePathCommit_RecordsWithoutDayAndNamesIt()
        {
            // The freight service's WorkLoading invokes the commit hook
            // through the delegate, which carries no day — the adapter
            // records with UnknownDayIndex and names the gap instead of
            // inventing a day.
            var log = new CustodyTransferLog();
            Chain s = NewChain(log);
            PlaceFreightOrder(s);
            s.Shipment = new LogisticsShipmentState
            {
                shipmentId = ShipmentId,
                sourceBusinessInstanceId = s.Supplier.BusinessId,
                destinationBusinessInstanceId = s.Bakery.BusinessInstanceId,
                cargoLotId = s.Lot.LotId,
                cargoSkuId = MillbrookFlourFreightCustodyAdapter.FlourSkuId,
                plannedQuantityUnits = s.Lot.PackageUnits,
                requiresSourceLotCustody = true,
                physicalWagonAssetId = WagonId,
            };
            Assert.IsTrue(s.Adapter.CommitSourceLot(s.Shipment, Sacks, out string reason), reason);
            Assert.AreEqual(2, log.Count, "order + commit");
            CustodyTransferRecord commit = RecordAt(log, 1);
            Assert.AreEqual(CustodyTransferLog.CauseFreightCommit, commit.CauseKind);
            Assert.AreEqual(CustodyTransferLog.UnknownDayIndex, commit.DayIndex,
                "the delegate path holds no day; the record says so instead of inventing one");
            Assert.IsTrue(log.Diagnostics.Count >= 1, "the missing day is named in diagnostics");
            Assert.IsTrue(string.Join(" | ", log.Diagnostics).Contains("day"),
                "the diagnostic names the day gap: " + string.Join(" | ", log.Diagnostics));
        }
    }
}
