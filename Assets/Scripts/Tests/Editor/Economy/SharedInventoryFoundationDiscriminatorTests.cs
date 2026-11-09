// Priority 9 Slice 0 — discriminator pins proving the current inventory
// seams (tests only, no production code). Each pin asserts the CURRENT
// behaviour at HEAD 0fd5a95; a pin that fails is a §1 misreading in the
// plan, not a production defect to fix here.
//
// F0-1 mass has no authority · F0-2 custody mid-trip · F0-3 two carry caps
// · F0-4 category pool vs identified lots · F0-5 owner == custodian
// · F0-6 three flour unit systems · F0-7 Unknown refusal pattern.
//
// Two plan-wording notes, both verified against source at HEAD:
// (a) F0-2 "ReceiptId matches the lot's SourceLabel": the lot's
// SourceLabel is the batch Provenance + " → carried home", and the
// Provenance embeds "receipt <id>" — so the receipt id is CARRIED in
// the SourceLabel (SourceLabel.Contains), not equal to it. This is the
// P5 Slice 7 SourceLabel-only carriage, already pinned that way in
// HouseholdShoppingEndToEndTests.cs:730 and
// HouseholdShoppingLoopTests.cs:352.
// (b) F0-3's SimulationDrivers.HorseShoppingCarryLimitSacks is a
// PRIVATE const in Orchestration/Systems/SimulationDrivers.cs:80, in a
// file the Roslyn harness does not compile, so no harness test can
// reference it. Its value (50 sacks) is verified by source inspection
// (also :3863-3866) and recorded here; the executable pin is the
// independence half — the shopping loop reads the instance
// ShoppingLoopOptions.CarryCapacityUnits, and flipping only that field
// flips the carry-vs-delivery decision.
// (c) F0-1 "reflection-free source reads": asserting that a member does
// NOT exist on a type can only be done at runtime via reflection (the
// precedent is PhysicalStorageZoneTests, which already uses
// System.Reflection). The pin names the three types and the lookup that
// finds nothing.
using System;
using System.Collections.Generic;
using System.Reflection;
using LandLedgers.Economy;
using LandLedgers.Economy.Businesses.Bakery;
using LandLedgers.Economy.Businesses.GeneralStore;
using LandLedgers.Economy.Core;
using LandLedgers.Economy.Logistics;
using LandLedgers.Economy.Storage;
using LandLedgers.Population;
using LandLedgers.Primitives;
using EntityId = LandLedgers.Primitives.EntityId;
using LandLedgers.Tasks;
using LandLedgers.Time;
using LandLedgers.World.Journeys;
using NUnit.Framework;
using UnityEngine;

namespace LandLedgers.EditorTests.Economy
{
    [TestFixture]
    public sealed class SharedInventoryFoundationDiscriminatorTests
    {
        // ---------- F0-2 / F0-3 shared shopping fixture ----------
        // Minimal S6-1 shape (single-person household, 14 loaves) over a
        // REAL BusinessRuntimeState with one 24-loaf identified retail lot,
        // through the REAL GeneralStoreSupplierAdapter — the same port shape
        // SinglePersonShoppingTests.LiveStatePort uses. The leg-by-leg loop
        // API (PlanTrip → BeginOutboundLeg → ArriveAtStoreAndSettle →
        // ReceiveAtHome) lets the test stop between purchase and receipt.

        private sealed class LiveStatePort : IGeneralStoreTradingPort, IItemLevelGeneralStoreTradingPort, IIdentifiedRetailStockReceivingPort
        {
            public BusinessRuntimeState State;
            public string StoreBusinessId { get; set; } = "general-store-1";
            public string StoreName { get; set; } = "General Store";
            public string LocationId { get; set; } = "store";
            public List<GeneralStoreCategoryOffer> OfferList = new List<GeneralStoreCategoryOffer>();
            public StoreTradingCapability Capability { get; set; } = new StoreTradingCapability();
            public int CashBalance;
            public int CreditSettlements;
            public bool OffersDeliveryValue;
            public int DeliveryFeeValue;

            public LiveStatePort(BusinessRuntimeState state)
            {
                State = state;
                OfferList.Add(new GeneralStoreCategoryOffer
                {
                    CategoryId = "staple_food",
                    OfferedItemIds = new List<string> { HouseholdItemCatalog.BreadId },
                    StoreUnitsPerItemUnit = 1,
                    HouseholdUnitsPerStoreUnit = 1,
                    UsesItemLevelInventory = true,
                });
            }

            public IReadOnlyList<GeneralStoreCategoryOffer> CategoryOffers => OfferList;
            public StoreTradingCapability TradingCapability => Capability;
            public int CurrentCashCents => CashBalance;
            public bool OffersDelivery => OffersDeliveryValue;
            public bool OffersTradeCredit => false;
            public int TradeCreditLimitCents => 0;

            private GeneralStoreCategoryOffer FindOffer(string itemId)
            {
                if (string.IsNullOrWhiteSpace(itemId)) return null;
                foreach (GeneralStoreCategoryOffer offer in OfferList)
                {
                    if (offer?.OfferedItemIds == null) continue;
                    foreach (string id in offer.OfferedItemIds)
                    {
                        if (string.Equals(id, itemId, StringComparison.OrdinalIgnoreCase)) return offer;
                    }
                }

                return null;
            }

            public int CategoryStockUnits(string categoryId)
            {
                CategoryStockState stock = State != null ? State.GetCategoryStock(categoryId) : null;
                return stock != null ? stock.CurrentStockUnits : 0;
            }

            public int CategoryTargetStockUnits(string categoryId)
            {
                CategoryStockState stock = State != null ? State.GetCategoryStock(categoryId) : null;
                return stock != null ? stock.TargetStockUnits : 0;
            }

            public int CategoryPricePerUnitCents(string categoryId) => 0;

            public bool TryConsumeStoreUnits(string categoryId, int units, out int consumedUnits)
            {
                consumedUnits = 0;
                return false;
            }

            public void RecordCashSettlement(string categoryId, int unitsSold, int revenueCents)
            {
                CashBalance += Math.Max(0, revenueCents);
            }

            public void RecordCreditSettlement(string categoryId, int unitsSold, int amountCents, string obligationId)
            {
                CreditSettlements++;
            }

            public string SpendCash(int amountCents, string purpose) => null;
            public string ReceiveStock(string categoryId, int units, string provenanceLabel) => null;
            public int CategoryReceivableUnits(string categoryId) => int.MaxValue;
            public int DeliveryFeeCents(string itemId, int units) => Math.Max(0, DeliveryFeeValue);
            public bool UsesItemLevelInventory(string itemId) => FindOffer(itemId)?.UsesItemLevelInventory == true;

            public int ItemStockUnits(string itemId)
            {
                GeneralStoreCategoryOffer offer = FindOffer(itemId);
                if (offer == null) return 0;
                return offer.UsesItemLevelInventory
                    ? (State?.GetRetailItemStockUnits(itemId, offer.CategoryId) ?? 0) / Math.Max(1, offer.StoreUnitsPerItemUnit)
                    : CategoryStockUnits(offer.CategoryId) / Math.Max(1, offer.StoreUnitsPerItemUnit);
            }

            public int ItemPricePerUnitCents(string itemId)
            {
                GeneralStoreCategoryOffer offer = FindOffer(itemId);
                if (offer == null) return 0;
                int storeUnitPrice = string.Equals(itemId, HouseholdItemCatalog.BreadId, StringComparison.OrdinalIgnoreCase) ? 5 : 0;
                return storeUnitPrice * Math.Max(1, offer.StoreUnitsPerItemUnit);
            }

            public bool TryConsumeItemStockUnits(string itemId, int units, out int consumedUnits)
            {
                consumedUnits = 0;
                GeneralStoreCategoryOffer offer = FindOffer(itemId);
                if (offer == null || State == null) return false;
                return State.TryConsumeRetailItemStockUnits(itemId, offer.CategoryId, units, out consumedUnits);
            }

            public void RecordItemCashSettlement(string categoryId, int unitsSold, int revenueCents)
            {
                CashBalance += Math.Max(0, revenueCents);
            }

            public string ReceiveRetailItemLot(string lotId, string itemId, string categoryId, string unitLabel,
                int units, string provenance, int dayIndex)
            {
                if (State == null) return "Store runtime unavailable.";
                return State.TryAddRetailItemLot(lotId, itemId, categoryId, unitLabel, units,
                    StoreBusinessId, StoreBusinessId, provenance, dayIndex);
            }
        }

        private sealed class ShoppingFixture
        {
            public PopulationState Population = new PopulationState();
            public HouseholdMembershipRegistry Membership = new HouseholdMembershipRegistry();
            public HouseholdLedgerRegistry Ledgers = new HouseholdLedgerRegistry();
            public HouseholdInventoryRegistry Inventories = new HouseholdInventoryRegistry();
            public HouseholdNeedRegistry Needs = new HouseholdNeedRegistry();
            public SupplierDirectory Directory = new SupplierDirectory();
            public JourneyModel Journeys = new JourneyModel();
            public TaskAuthority Tasks = new TaskAuthority();
            public WorkTimeBudgetStore Budgets = new WorkTimeBudgetStore();
            public PersonScheduleTracker Tracker = new PersonScheduleTracker();
            public ShopperSellerKnowledge Knowledge = new ShopperSellerKnowledge();
            public BusinessRuntimeState StoreState = new BusinessRuntimeState();
            public LiveStatePort Port;
            public HouseholdShoppingLoop Loop;
            public List<string> Diag = new List<string>();
            public Dictionary<int, string> Locations = new Dictionary<int, string>();

            public const int HouseholdId = 7;
            public const int ShopperId = 1;

            public ShoppingFixture(bool offersDelivery, int deliveryFeeCents)
            {
                Population.people.Add(new PersonState
                {
                    id = ShopperId, firstName = "Tomas", lastName = "Morrow", age = 34,
                    ageBand = AgeBand.Adult18Plus, isFemale = false,
                });
                Population.households.Add(new HouseholdState { id = HouseholdId });
                Membership.Register(new HouseholdMembership
                {
                    MembershipId = ShopperId, PersonId = ShopperId, HouseholdId = HouseholdId, StartDayIndex = 0,
                });
                Journeys.RegisterLocation(new JourneyLocation("home", JourneyLocationKind.Farmstead, "Morrow Farm", 0f, 0f));
                Journeys.RegisterLocation(new JourneyLocation("store", JourneyLocationKind.Store, "General Store", 2f, 0f));
                Journeys.AddEdge("home", "store", 2.0f, "river road");

                StoreState.BindBusinessIdentity("general-store-1");
                StoreState.AddCashCents(5000);
                StoreState.EnsureCategoryStock("staple_food", 0, 1000);
                Assert.IsNull(StoreState.TryAddRetailItemLot("retail-bread-24", "bread", "staple_food", "loaf",
                    24, "general-store-1", "general-store-1", "test bakery lot", 0),
                    "fixture store must hold one 24-loaf identified retail lot");
                Port = new LiveStatePort(StoreState)
                {
                    OffersDeliveryValue = offersDelivery,
                    DeliveryFeeValue = deliveryFeeCents,
                };
                GeneralStoreSupplierAdapter.RegisterIn(Directory, Port);
                Knowledge.LearnSupplier(ShopperId, "general-store-1", "lives in the same town", 0);
                Locations[ShopperId] = "home";

                HouseholdShoppingLoop.RegisterTaskDefinitions(Tasks);
                Loop = new HouseholdShoppingLoop(
                    Population, Membership, Ledgers, Inventories, Needs, Directory, Journeys,
                    Tasks, Budgets, Tracker, Knowledge,
                    obligations: null, entityIds: null,
                    personEntityId: pid => EntityId.For(EntityKind.Person, pid),
                    spendingPolicyProvider: null);

                Ledgers.GetOrCreate(HouseholdId).RecordInflow(0, 10000,
                    HouseholdIncomeSource.OwnerContribution, "test-capital", "test funding", "test");
            }

            public ShoppingLoopOptions Options(int carryCapacityUnits)
            {
                var fixture = this;
                return new ShoppingLoopOptions
                {
                    PersonLocationId = pid => fixture.Locations.TryGetValue(pid, out string l) ? l : "home",
                    SetPersonLocation = (pid, loc) => fixture.Locations[pid] = loc,
                    DepartureMinuteOfDay = 600,
                    InStoreMinutes = 15,
                    CarryCapacityUnits = carryCapacityUnits,
                    Diagnostics = Diag,
                };
            }

            public HouseholdPurchasingNeed OpenBreadNeed(int units)
            {
                HouseholdPurchasingNeed need = Needs.CreateNeed(HouseholdId, HouseholdItemCatalog.BreadId, 0);
                need.UnitsNeeded = units;
                need.Urgency01 = 0.9f;
                need.IsNecessity = true;
                return need;
            }

            public int BreadUnits() =>
                Inventories.GetOrCreate(HouseholdId).GetAvailableUnits(HouseholdItemCatalog.BreadId);
        }

        // ---------- F0-1: mass has no authority ----------

        [Test]
        public void F0_1_MassHasNoAuthority()
        {
            // Ground truth: the five item ids exist in the catalog with
            // their unit labels — and nothing else physical.
            Assert.AreEqual("loaf", HouseholdItemCatalog.Get("bread").UnitLabel);
            Assert.AreEqual("lb", HouseholdItemCatalog.Get("flour").UnitLabel);
            Assert.AreEqual("lb", HouseholdItemCatalog.Get("potatoes").UnitLabel);
            Assert.AreEqual("lb", HouseholdItemCatalog.Get("salt").UnitLabel);
            Assert.AreEqual("gal", HouseholdItemCatalog.Get("lamp_oil").UnitLabel);

            // No mass / volume / dimension member on any of the three lot
            // authorities. (Reflection is the only way to assert absence;
            // see the file header note.)
            foreach (Type type in new[]
            {
                typeof(HouseholdItemDefinition),
                typeof(HouseholdItemLot),
                typeof(BusinessRetailItemLotState),
            })
            {
                var found = new List<string>();
                foreach (MemberInfo member in type.GetMembers(
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
                {
                    string name = member.Name.ToLowerInvariant();
                    if (name.Contains("mass") || name.Contains("volume") || name.Contains("dimension")
                        || name.Contains("weight") || name.Contains("kilogram") || name.Contains("liter")
                        || name.Contains("litre"))
                    {
                        found.Add(member.Name);
                    }
                }

                Assert.AreEqual(0, found.Count,
                    type.Name + " must expose no mass/volume/dimension member, found: " +
                    string.Join(", ", found));
            }

            // Freight answers with a caller-supplied per-shipment default,
            // not per-item truth: two specs for the same item id ("bread")
            // carry different masses with no refusal anywhere.
            var defaultSpec = new FreightCargoSpec();
            Assert.AreEqual(1f, defaultSpec.MassKgPerUnit, 0.0001f);
            Assert.AreEqual(0.01f, defaultSpec.VolumeM3PerUnit, 0.0001f);
            var callerSpec = new FreightCargoSpec { MassKgPerUnit = 2.5f, VolumeM3PerUnit = 0.03f };
            Assert.AreEqual(2.5f, callerSpec.MassKgPerUnit, 0.0001f);
            Assert.IsTrue(Math.Abs(defaultSpec.MassKgPerUnit - callerSpec.MassKgPerUnit) > 0.0001f,
                "the same item id can carry 1 kg/unit on one shipment and 2.5 kg/unit on another — " +
                "mass is per-shipment caller data, not per-item truth");
        }

        // ---------- F0-2: custody mid-trip ----------

        [Test]
        public void F0_2_CustodyMidTrip()
        {
            var f = new ShoppingFixture(offersDelivery: false, deliveryFeeCents: 0);
            HouseholdPurchasingNeed need = f.OpenBreadNeed(14);
            ShoppingLoopOptions options = f.Options(carryCapacityUnits: 50);

            PlannedShoppingTrip trip = f.Loop.PlanTrip(need, 0, options);
            Assert.AreEqual(PlannedShoppingTrip.TripState.Planned, trip.State, trip.FailureReason);
            Assert.AreEqual(ShoppingFixture.ShopperId, trip.ShopperPersonId);
            Assert.IsNull(f.Loop.BeginOutboundLeg(trip, 0, f.Diag), "outbound leg must start");

            // Purchase at the counter — then STOP, before any receipt.
            int arrivalMinute = 600 + trip.Legs.OutboundMinutes;
            Assert.IsNull(f.Loop.ArriveAtStoreAndSettle(trip, 0, arrivalMinute, f.Diag),
                "settlement must succeed; diag: " + string.Join(" | ", f.Diag));
            Assert.IsNotNull(trip.Batch, "settlement must create a custody batch");

            // The household inventory holds 0 loaves: no teleport.
            Assert.AreEqual(0, f.BreadUnits(), "no loaves in the household inventory mid-trip");
            // The retail lot is consumed: 24 - 14 = 10 loaves left.
            Assert.AreEqual(10, f.StoreState.GetRetailItemStockUnits("bread"),
                "the identified retail lot must fall by exactly the 14 sold");
            Assert.AreEqual(1, f.StoreState.RetailItemLots.Count);
            // The only record of the 14 loaves is the custody batch.
            Assert.AreEqual(1, f.Loop.ActiveCustodyBatches.Count, "exactly one in-transit batch");
            CarriedGoodsBatch batch = f.Loop.ActiveCustodyBatches[0];
            Assert.AreSame(trip.Batch, batch);
            Assert.AreEqual(HouseholdItemCatalog.BreadId, batch.ItemId);
            Assert.AreEqual(14, batch.Units);
            Assert.AreEqual(GoodsCustodyState.InHand, batch.Custody);
            Assert.AreEqual(ShoppingFixture.ShopperId, batch.HolderPersonId);
            Assert.AreEqual(trip.ShopperPersonId, batch.HolderPersonId);
            Assert.IsTrue(!string.IsNullOrWhiteSpace(batch.ReceiptId), "the batch must carry a receipt id");

            // Physical receipt at home: the lot lands, carrying the receipt
            // id in its SourceLabel (SourceLabel-only carriage, P5 Slice 7).
            Assert.IsNull(f.Loop.ReceiveAtHome(trip, 0, f.Diag), "receipt must succeed");
            Assert.AreEqual(14, f.BreadUnits(), "all 14 loaves land in the household inventory at receipt");
            Assert.AreEqual(0, f.Loop.ActiveCustodyBatches.Count, "custody ends at receipt");
            HouseholdItemLot lot = null;
            foreach (HouseholdItemLot candidate in f.Inventories.GetOrCreate(ShoppingFixture.HouseholdId).Lots)
            {
                if (candidate != null && string.Equals(candidate.ItemId, HouseholdItemCatalog.BreadId, StringComparison.Ordinal))
                {
                    lot = candidate;
                }
            }

            Assert.IsNotNull(lot, "a bread lot must exist after receipt");
            Assert.AreEqual(14, lot.QuantityUnits);
            Assert.IsTrue(lot.SourceLabel.Contains(batch.ReceiptId),
                "the lot's SourceLabel must carry the batch receipt id '" + batch.ReceiptId +
                "', got: '" + lot.SourceLabel + "'");
        }

        // ---------- F0-3: two carry caps, same number, different meanings ----------

        [Test]
        public void F0_3_TwoCarryCaps()
        {
            // The shopping-loop cap is instance data on the options, in
            // units, default 50.
            Assert.AreEqual(50, new ShoppingLoopOptions().CarryCapacityUnits,
                "ShoppingLoopOptions.CarryCapacityUnits default must be 50 units");

            // Same 14-loaf need, same store, delivery offered at 10c:
            // with the cap at 50 the goods stay in hand; with the cap at
            // 10 the same need goes by arranged delivery. The only input
            // that changed is the instance CarryCapacityUnits — the two
            // caps share no source.
            var inHand = new ShoppingFixture(offersDelivery: true, deliveryFeeCents: 10);
            PlannedShoppingTrip trip50 = DriveToSettlement(inHand, 50);
            Assert.AreEqual(GoodsCustodyState.InHand, trip50.Batch.Custody,
                "14 loaves fit the 50-unit carry cap, so no delivery is arranged");

            var delivered = new ShoppingFixture(offersDelivery: true, deliveryFeeCents: 10);
            PlannedShoppingTrip trip10 = DriveToSettlement(delivered, 10);
            Assert.AreEqual(GoodsCustodyState.ArrangedDelivery, trip10.Batch.Custody,
                "14 loaves exceed the 10-unit carry cap, so arranged delivery is used");

            // The horse-feed cap is a different number in a different
            // system: SimulationDrivers.HorseShoppingCarryLimitSacks = 50
            // SACKS (private const, Orchestration/Systems/SimulationDrivers.cs:80,
            // used at :3863-3866 for Ethan's horse-feed shopping) — 50 by
            // coincidence with the 50 UNITS above, not by derivation. It is
            // unreachable from this harness (private const in a
            // harness-excluded file), so its value is pinned by source
            // inspection, not by this test; the independence above is the
            // executable half of the pin.
        }

        private static PlannedShoppingTrip DriveToSettlement(ShoppingFixture f, int carryCapacityUnits)
        {
            HouseholdPurchasingNeed need = f.OpenBreadNeed(14);
            ShoppingLoopOptions options = f.Options(carryCapacityUnits);
            PlannedShoppingTrip trip = f.Loop.PlanTrip(need, 0, options);
            Assert.AreEqual(PlannedShoppingTrip.TripState.Planned, trip.State, trip.FailureReason);
            Assert.IsNull(f.Loop.BeginOutboundLeg(trip, 0, f.Diag), "outbound leg must start");
            Assert.IsNull(f.Loop.ArriveAtStoreAndSettle(trip, 0, 600 + trip.Legs.OutboundMinutes, f.Diag),
                "settlement must succeed; diag: " + string.Join(" | ", f.Diag));
            Assert.IsNotNull(trip.Batch, "settlement must create a custody batch");
            return trip;
        }

        // ---------- F0-4: category pool vs identified lots ----------

        [Test]
        public void F0_4_CategoryPoolVsIdentifiedLots()
        {
            var state = new BusinessRuntimeState();
            state.BindBusinessIdentity("general-store-1");
            state.EnsureCategoryStock("staple_food", 0, 1000);
            Assert.IsNull(state.TryAddRetailItemLot("retail-bread-24", "bread", "staple_food", "loaf",
                24, "general-store-1", "general-store-1", "bakery lot test-bakery-lot", 2),
                "one 24-loaf identified lot must land");

            // Both views read 24 — the same physical goods, twice.
            Assert.AreEqual(24, state.GetRetailItemStockUnits("bread"),
                "the identified-lot view holds the 24 loaves");
            Assert.AreEqual(24, state.GetRetailItemStockUnits("bread", "staple_food"));
            Assert.AreEqual(24, state.GetCategoryStock("staple_food").CurrentStockUnits,
                "receipt also lands in the category balance");

            // Category-only consumption refuses: identified units are
            // excluded from the unallocated pool.
            Assert.IsTrue(!state.TryConsumeCategoryStockUnits("staple_food", 1, out int consumed),
                "category-only consumption must refuse when every unit is identified");
            Assert.AreEqual(0, consumed);

            // Summing both views gives 48 for 24 physical loaves — the
            // double-count the foundation must never perform.
            int summedViews = state.GetRetailItemStockUnits("bread")
                + state.GetCategoryStock("staple_food").CurrentStockUnits;
            Assert.AreEqual(48, summedViews, "the two views sum to 48");
            Assert.IsTrue(summedViews != 24,
                "48 is NOT a valid stock figure: only 24 loaves physically exist");

            // Control: the item-level path consumes lot and category together.
            Assert.IsTrue(state.TryConsumeRetailItemStockUnits("bread", "staple_food", 1, out int itemConsumed));
            Assert.AreEqual(1, itemConsumed);
            Assert.AreEqual(23, state.GetRetailItemStockUnits("bread"));
            Assert.AreEqual(23, state.GetCategoryStock("staple_food").CurrentStockUnits);
        }

        // ---------- F0-5: owner == custodian ----------

        [Test]
        public void F0_5_OwnerEqualsCustodian()
        {
            var state = new BusinessRuntimeState();
            state.BindBusinessIdentity("general-store-1");
            state.EnsureCategoryStock("staple_food", 0, 1000);

            // A lot owned by someone else cannot be received.
            string ownerRefusal = state.TryAddRetailItemLot("lot-other-owner", "bread", "staple_food", "loaf",
                10, "other-business", "general-store-1", "third-party lot", 0);
            Assert.IsTrue(!string.IsNullOrEmpty(ownerRefusal), "a foreign-owned lot must be refused");
            Assert.IsTrue(ownerRefusal.Contains("must match"), "refusal must name the owner/custodian rule, got: " + ownerRefusal);

            // A lot held by someone else cannot be received either.
            string custodianRefusal = state.TryAddRetailItemLot("lot-other-custodian", "bread", "staple_food", "loaf",
                10, "general-store-1", "other-business", "third-party-held lot", 0);
            Assert.IsTrue(!string.IsNullOrEmpty(custodianRefusal), "a third-party-held lot must be refused");

            // No lot landed from either refusal.
            Assert.AreEqual(0, state.RetailItemLots.Count);

            // Control: owner == custodian == receiving business lands.
            Assert.IsNull(state.TryAddRetailItemLot("lot-own", "bread", "staple_food", "loaf",
                10, "general-store-1", "general-store-1", "own lot", 0));
            Assert.AreEqual(1, state.RetailItemLots.Count);
            Assert.AreEqual("general-store-1", state.RetailItemLots[0].OwnerBusinessId);
            Assert.AreEqual("general-store-1", state.RetailItemLots[0].CustodianBusinessId);
        }

        // ---------- F0-6: three flour unit systems ----------

        [Test]
        public void F0_6_ThreeFlourUnitSystems()
        {
            // Millbrook row values copied from the authored asset via the
            // MillbrookFlourFreightCustodyTests fixture (10 lb / 18 bakery
            // units / 48c per sack, 230 lb starting stock).
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
            var supplier = new OffMapAidBusinessRuntime(definition);
            var bakeryCash = new FakeBusinessCashPort(20000);

            Assert.IsNull(supplier.TryPlaceOrder("p9s0-order", "bakery_001", "Glen Pierce", 2,
                bakeryCash, 0, out OffMapAidCargoLot lot), "a 2-sack order must place");

            // One order, three counts: 2 sacks, 20 lb, 36 bakery units.
            Assert.AreEqual(2, lot.PackageUnits, "packages (sacks)");
            Assert.AreEqual(20, lot.InventoryUnits, "inventory pounds");
            Assert.AreEqual(36, lot.BakeryUnits, "bakery-unit quotation");
            Assert.AreEqual("lb", lot.InventoryUnit);
            Assert.AreEqual(96, lot.TotalCostCents, "2 sacks x 48c");
            Assert.AreEqual(210, supplier.GetInventoryUnits("bakery_flour_sack"));
            Assert.AreEqual(19904, bakeryCash.BalanceCents);
            Assert.AreEqual(96, supplier.CashCents);

            // Through the custody adapter the Bakery bin receives 20 lb —
            // not 36 (bakery units are never pounds) and not 2 (sacks).
            var bakery = new BakeryShopRuntime("bakery_001");
            var ids = new EntityIdRegistry();
            var adapter = new MillbrookFlourFreightCustodyAdapter(supplier, bakery, ids);
            var shipment = new LogisticsShipmentState
            {
                shipmentId = "test-millbrook-to-bakery-p9s0",
                sourceBusinessInstanceId = supplier.BusinessId,
                destinationBusinessInstanceId = bakery.BusinessInstanceId,
                cargoLotId = lot.LotId,
                cargoSkuId = MillbrookFlourFreightCustodyAdapter.FlourSkuId,
                plannedQuantityUnits = lot.PackageUnits,
                requiresSourceLotCustody = true,
            };
            Assert.IsTrue(adapter.CommitSourceLot(shipment, 2, out string commitReason), commitReason);
            Assert.IsTrue(adapter.ReceiveCargoLot(shipment, 2, 1, out string receiptReason), receiptReason);
            Assert.AreEqual(1, bakery.FlourStock.Lots.Count);
            Assert.AreEqual(20, bakery.FlourStock.Lots[0].Units,
                "the Bakery bin receives inventory pounds (20 lb), never the 36 bakery units or the 2 sacks");
            Assert.AreEqual(BakeryBreadCatalog.FlourItemId, bakery.FlourStock.Lots[0].FlourName);
            Assert.AreEqual(lot.LotId, bakery.FlourStock.Lots[0].ImportOrderId);
            Assert.IsTrue(lot.SourceCommitted);
            Assert.IsTrue(lot.Received);
        }

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

        // ---------- F0-7: Unknown refusal pattern exists ----------

        [Test]
        public void F0_7_UnknownPatternExists()
        {
            // A mass-bounded zone refuses a lot with unknown mass rather
            // than assuming a size.
            PhysicalStorageZone massBounded = MakeZone(
                StorageCapacityLimit.Known(100f), StorageCapacityLimit.Unknown);
            Assert.IsTrue(!massBounded.TryPlace(
                Lot("business-inventory", "lot-1", StorageLoadMeasurement.Unknown), out string refusal),
                "unknown-mass placement on a mass-bounded zone must be refused");
            Assert.IsTrue(refusal.Contains("incoming lot mass is unknown"),
                "refusal must name the unknown mass, got: " + refusal);
            Assert.AreEqual(0, massBounded.Placements.Count);

            // The same placement is accepted when the zone bounds no axis
            // at all (there is no item-count axis in source — the plan's
            // "bounds only item count" leg has no such axis to test, so the
            // no-axis leg is the executable form of it).
            PhysicalStorageZone unbounded = MakeZone(
                StorageCapacityLimit.Unknown, StorageCapacityLimit.Unknown);
            Assert.IsTrue(unbounded.TryPlace(
                Lot("business-inventory", "lot-1", StorageLoadMeasurement.Unknown), out string acceptRefusal),
                acceptRefusal);
            Assert.AreEqual(1, unbounded.Placements.Count);

            // Control: a known-mass lot places on the mass-bounded zone.
            Assert.IsTrue(massBounded.TryPlace(
                Lot("business-inventory", "lot-2", StorageLoadMeasurement.MassOnly(10f)), out string knownRefusal),
                knownRefusal);
            Assert.AreEqual(1, massBounded.Placements.Count);
        }

        private static PhysicalStorageZone MakeZone(StorageCapacityLimit mass, StorageCapacityLimit volume) =>
            new PhysicalStorageZone(new PhysicalStorageZoneDefinition(
                "zone-1", "site-1", StorageExposure.Outdoor, mass, volume),
                (authority, lotId) => !string.IsNullOrWhiteSpace(authority) && !string.IsNullOrWhiteSpace(lotId));

        private static StoredLotReference Lot(string authority, string lotId, StorageLoadMeasurement load) =>
            new StoredLotReference(authority, lotId, load);
    }
}
