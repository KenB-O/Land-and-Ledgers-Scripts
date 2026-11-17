using System;
using System.Collections.Generic;
using System.Reflection;
using LandLedgers.Economy.Blacksmith;
using LandLedgers.Economy.Financing;
using LandLedgers.Economy.Labor;
using LandLedgers.Population;
using LandLedgers.Primitives;
using LandLedgers.World;
using LandLedgers.World.Property;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;
using EntityId = LandLedgers.Primitives.EntityId;

namespace LandLedgers.EditorTests.Financing
{
    /// <summary>
    /// D-6 (Agent D, expanded campaign 2026-11-16): contents contract core —
    /// the B6 PropertyContentsSelection attached to NpcAcquisitionPlan,
    /// validated at plan time against the seller's furnishing register and
    /// re-validated at close, with the deal failing loudly and unwinding
    /// (nothing moved) on re-validation failure, never silently dropping an
    /// item. Conservation price == structure + contents is checked at plan
    /// time when a structure value is supplied (Agent F's split — D invents
    /// no structure value); the included-fixture value must sit outside the
    /// structure value, never double-counted.
    ///
    /// D-7 (Agent D, expanded campaign 2026-11-16): the closing transfer
    /// itself — included items move seller register → buyer register
    /// through the single transfer authority and install for the buyer
    /// through the normal install path (same building, same placement,
    /// capability carried forward); excluded movables are removed by
    /// the seller to custody; excluded fixtures stay held in place as
    /// the seller's property; stove fuel returns to the seller's reserve
    /// before the stove leaves the register; conservation is re-checked
    /// at close; and a transfer that cannot run (or fails mid-way)
    /// fails loudly with a full unwind — nothing moved on failure.
    /// The plan must name the building the selection covers
    /// (BuildingInstanceId binding, D-7).
    ///
    /// D-8 (Agent D, expanded campaign 2026-11-16): NPC contents
    /// negotiation — the buyer asks for a selection (wanted kinds in
    /// priority order, total capital, moving cost) through the B6 contract,
    /// the seller answers through the extended seller policy (accept /
    /// counter / walk, optionally with a different selection), and the
    /// agreed price respects the F6 anchor (structure-only plus contents)
    /// with the structure absorbing the discount, so D-6 conservation
    /// holds by construction. A refused or failed negotiation leaves cash
    /// and title untouched, because the negotiation never touches them:
    /// the caller builds the plan from the agreed result and closes it
    /// through the D-6/D-7 path.
    ///
    /// D-9 (Agent D, expanded campaign 2026-11-16): the player selection
    /// API — list eligible items, set mode, toggle items, compute the
    /// contents value, validate the selection — routed through B7's
    /// presence contract (the player must be present at the property, or
    /// act through an authorized agent, before any selection work
    /// happens; the confirmed selection is applied only through the
    /// contract's Execute, once Ready). The agreed price is the caller's
    /// input and the implied structure value is derived from it (agreed
    /// minus contents, never the F6 anchor — D-8 §16); the confirmed
    /// selection closes through the D-6/D-7 path with cash and title
    /// conserved. The player-flow attach in AcquisitionMarketManager
    /// (the presence-contract seam + BeginPlayerContentsSelection
    /// factory) is harness-excluded and source-reviewed only.
    ///
    /// Prices and the structure value here are TEST-LOCAL calibration only
    /// (the price func is the caller's real price data in production —
    /// Agent F's prices; no production price is invented in this file).
    /// The player-flow attach (AcquisitionDealState + DTO) lives in
    /// harness-excluded files and is source-reviewed only, with Codex Unity
    /// proof owed — these tests pin the harness-compiled NPC flow.
    /// </summary>
    [TestFixture]
    public sealed class NpcAcquisitionContentsDef011Tests
    {
        private const int SellerHouseholdId = 7;
        private const int BuyerHouseholdId = 9;
        private const int BuildingInstanceId = 101;
        private const int StructureValueCents = 200000;
        private const int Day = 100;

        /// <summary>Test-local replacement costs (calibration only — see the class doc).</summary>
        private static int PriceForKind(string kind)
        {
            switch (kind)
            {
                case "furniture.bed": return 10000;
                case "furniture.table": return 8000;
                case "furniture.chair": return 3000;
                case "furniture.cooking-stove": return 12000;
                case "furniture.shelf": return 2000;
                case "furniture.cabinet-small": return 5000;
                case "furniture.cabinet-large": return 7000;
                case "furniture.crate": return 1500;
                case "furniture.bedroll": return 1000;
                case "furniture.kitchen-counter": return 6000;
                case "furniture.barrel": return 1000;
                default: return 0;
            }
        }

        private sealed class Fixture
        {
            public EntityIdRegistry Ids = new EntityIdRegistry();
            public FinancialObligationAuthority Authority = new FinancialObligationAuthority();
            public CreditOfferWorkflow Workflow = new CreditOfferWorkflow();
            public CreditRegistry Registry = new CreditRegistry();
            public CreditCashBridge Cash = new CreditCashBridge();
            public TitleAuthority Titles = new TitleAuthority();
            public HousingAuthority Housing = new HousingAuthority();
            public SellerFinanceNegotiationBook Negotiations = new SellerFinanceNegotiationBook();
            public SellerFinanceClosingService ClosingService = new SellerFinanceClosingService();
            public NpcCreditDecisionEngine CreditEngine = new NpcCreditDecisionEngine();
            public NpcPropertyAcquisitionService Acquisition = new NpcPropertyAcquisitionService();
            public CreditEventLog Events = new CreditEventLog();
            public List<string> Diag = new List<string>();

            public Fixture()
            {
                Registry.AttachFinancialAuthority(Authority);
            }

            public HouseholdLedger RegisterHousehold(int householdId, int cashCents)
            {
                var ledger = new HouseholdLedger(householdId);
                Assert.IsNull(ledger.RecordInflow(0, cashCents, HouseholdIncomeSource.OwnerContribution,
                    "test-seed", "test seed capital", "test"));
                Assert.IsNull(Cash.Register("household:" + householdId,
                    new HouseholdCashStore(householdId, ledger)));
                return ledger;
            }

            /// <summary>A titled parcel with a house on it, held by the seller.</summary>
            public string RegisterFarmstead(string parcelId, string sellerName)
            {
                Assert.IsNotNull(Titles.RegisterParcel(parcelId, "farmstead", 40f, sellerName,
                    TitleBasis.Purchase, 0, Diag));
                Building building = Housing.RegisterBuilding("bld-" + parcelId, parcelId, "house", 0,
                    "test farmstead", Diag);
                Assert.IsNotNull(building);
                AccommodationSpace space = Housing.DefineSpace(building.BuildingId, 4, Diag);
                Assert.IsNotNull(space);
                return space.SpaceId;
            }

            public NpcAcquisitionPlan BasePlan(string parcelId, string spaceId, int priceCents)
            {
                return new NpcAcquisitionPlan
                {
                    PlanId = "plan-1",
                    BuyerHouseholdId = BuyerHouseholdId,
                    BuyerName = "household:" + BuyerHouseholdId,
                    SellerName = "household:" + SellerHouseholdId,
                    ParcelId = parcelId,
                    // D-7: the plan names the building the contents
                    // selection covers — the binding ValidatePlan enforces.
                    BuildingInstanceId = BuildingInstanceId,
                    SpaceId = spaceId,
                    OccupantPersonIds = new List<int> { 201, 202 },
                    AgreedPriceCents = priceCents,
                    CashDownCents = priceCents,
                };
            }
        }

        private static HouseholdFurnishingRegister MakeRegister(int householdId = SellerHouseholdId)
        {
            return new HouseholdFurnishingRegister { HouseholdId = householdId };
        }

        private static EquipmentAsset MakeAsset(string assetId, string kind, float condition = 1f, int ownerId = SellerHouseholdId)
        {
            return new EquipmentAsset
            {
                AssetId = assetId,
                Kind = kind,
                DisplayName = kind,
                Condition01 = condition,
                OwnerKind = "household",
                OwnerId = ownerId.ToString(),
            };
        }

        private static List<InteriorFloorAuthoring> GetInteriorFloors(BuildingFootprintAuthority authority)
        {
            FieldInfo field = typeof(BuildingFootprintAuthority).GetField("interiorFloors", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.NotNull(field);
            return (List<InteriorFloorAuthoring>)field.GetValue(authority);
        }

        private static void AddWalkableTestFloor(BuildingFootprintAuthority authority)
        {
            InteriorFloorAuthoring floor = new() { floorIndex = 0, floorHeightLocal = 0f };
            floor.walkableAreas.Add(new InteriorPolygonAuthoring
            {
                label = "Test Floor",
                vertices = new List<Vector3>
                {
                    new(-3f, 0f, -3f), new(3f, 0f, -3f), new(3f, 0f, 3f), new(-3f, 0f, 3f),
                },
            });
            GetInteriorFloors(authority).Add(floor);
        }

        private static BuildingFootprintAuthority MakeBuilding(string name)
        {
            GameObject building = new(name);
            BuildingFootprintAuthority authority = building.AddComponent<BuildingFootprintAuthority>();
            AddWalkableTestFloor(authority);
            return authority;
        }

        private static BuildingEquipmentPlacement MakePlacement(string name, Vector3 position)
        {
            GameObject item = new(name);
            item.transform.position = position;
            return item.AddComponent<BuildingEquipmentPlacement>();
        }

        private static BuildingEquipmentPlacement InstallAsset(
            HouseholdFurnishingRegister register,
            BuildingFootprintAuthority building,
            string assetId,
            int buildingInstanceId = BuildingInstanceId,
            FurnishingCapabilityKind capabilityKind = FurnishingCapabilityKind.None,
            string capabilityReferenceId = "")
        {
            BuildingEquipmentPlacement placement = MakePlacement(assetId, default);
            string error = register.TryInstall(assetId, placement, building, buildingInstanceId, 0,
                capabilityKind, capabilityReferenceId,
                FurnishingAcquisitionSource.ScenarioEndowment, "endowment-qa-town-1",
                out FurnishingPlacementBinding binding);
            Assert.IsNull(error, error);
            Assert.NotNull(binding);
            return placement;
        }

        /// <summary>The seller's furnished house: bed, table, chair, stove, shelf installed in building 101.</summary>
        private static HouseholdFurnishingRegister MakeFurnishedSellerRegister()
        {
            HouseholdFurnishingRegister register = MakeFurnishedSellerRegisterTracked(
                out Dictionary<string, BuildingEquipmentPlacement> ignoredPlacements,
                out BuildingFootprintAuthority ignoredBuilding);
            return register;
        }

        /// <summary>
        /// D-7: the seller's furnished house, with the scene placements
        /// tracked by placement id (the fake resolver's map) and the
        /// building authority returned. The bed carries its sleeping-place
        /// capability and the stove its stove-workstation capability, so
        /// the transfer can prove the capability carries forward to the
        /// buyer and the stove registry can derive the stove for the
        /// fuel-settlement test. The plain helper above delegates here;
        /// the capabilities do not affect the D-6 plan-time assertions
        /// (Validate / Revalidate / conservation never read them).
        /// </summary>
        private static HouseholdFurnishingRegister MakeFurnishedSellerRegisterTracked(
            out Dictionary<string, BuildingEquipmentPlacement> placementsById,
            out BuildingFootprintAuthority building)
        {
            var register = MakeRegister();
            Assert.IsNull(register.TryRegisterAsset(MakeAsset("bed-1", "furniture.bed")));
            Assert.IsNull(register.TryRegisterAsset(MakeAsset("table-1", "furniture.table")));
            Assert.IsNull(register.TryRegisterAsset(MakeAsset("chair-1", "furniture.chair")));
            Assert.IsNull(register.TryRegisterAsset(MakeAsset("stove-1", "furniture.cooking-stove")));
            Assert.IsNull(register.TryRegisterAsset(MakeAsset("shelf-1", "furniture.shelf")));
            building = MakeBuilding("seller-house");
            BuildingEquipmentPlacement bed = InstallAsset(register, building, "bed-1",
                BuildingInstanceId, FurnishingCapabilityKind.SleepingPlace, "sleeping-place-bed-1");
            BuildingEquipmentPlacement table = InstallAsset(register, building, "table-1");
            BuildingEquipmentPlacement chair = InstallAsset(register, building, "chair-1");
            BuildingEquipmentPlacement stove = InstallAsset(register, building, "stove-1",
                BuildingInstanceId, FurnishingCapabilityKind.StoveWorkstation, "stove");
            BuildingEquipmentPlacement shelf = InstallAsset(register, building, "shelf-1");
            placementsById = new Dictionary<string, BuildingEquipmentPlacement>();
            foreach (BuildingEquipmentPlacement placement in new[] { bed, table, chair, stove, shelf })
            {
                Assert.IsFalse(string.IsNullOrWhiteSpace(placement.PlacementId), "the install must mint a placement id.");
                placementsById[placement.PlacementId] = placement;
            }
            return register;
        }

        private static PropertyContentsSelection BuildAllFor(HouseholdFurnishingRegister register)
        {
            List<FurnishingContentsItem> items = PropertyContentsSelection.ListEligibleContents(register, BuildingInstanceId);
            return PropertyContentsSelection.BuildAll(items, "household", SellerHouseholdId.ToString(),
                BuildingInstanceId, Day, PriceForKind);
        }

        private static Fixture MakeDealFixture(int buyerCashCents = 300000, int sellerCashCents = 5000)
        {
            var f = new Fixture();
            f.RegisterHousehold(SellerHouseholdId, sellerCashCents);
            f.RegisterHousehold(BuyerHouseholdId, buyerCashCents);
            return f;
        }

        /// <summary>D-7: the test placement resolver — the fixture's placements, keyed by placement id.</summary>
        private sealed class FakePlacementResolver : IContentsPlacementResolver
        {
            private readonly Dictionary<string, BuildingEquipmentPlacement> byPlacementId;
            private readonly BuildingFootprintAuthority building;
            private readonly HashSet<string> unresolvablePlacementIds;

            public FakePlacementResolver(Dictionary<string, BuildingEquipmentPlacement> byPlacementId,
                BuildingFootprintAuthority building, HashSet<string> unresolvablePlacementIds = null)
            {
                this.byPlacementId = byPlacementId;
                this.building = building;
                this.unresolvablePlacementIds = unresolvablePlacementIds ?? new HashSet<string>();
            }

            public bool TryResolvePlacement(string placementId, int buildingInstanceId,
                out BuildingEquipmentPlacement placement, out BuildingFootprintAuthority buildingOut)
            {
                placement = null;
                buildingOut = null;
                if (buildingInstanceId != BuildingInstanceId) return false;
                if (string.IsNullOrWhiteSpace(placementId) || unresolvablePlacementIds.Contains(placementId)) return false;
                if (!byPlacementId.TryGetValue(placementId, out placement)) return false;
                buildingOut = building;
                return true;
            }
        }

        /// <summary>
        /// D-7: resolves the first N calls, then fails — a mid-transfer
        /// defect the pre-flight cannot see (the pre-flight already
        /// passed). The rollback is exercised for real: items transferred
        /// before the failure must come back.
        /// </summary>
        private sealed class CountingResolver : IContentsPlacementResolver
        {
            private readonly FakePlacementResolver inner;
            private readonly int succeedCalls;
            private int calls;

            public CountingResolver(FakePlacementResolver inner, int succeedCalls)
            {
                this.inner = inner;
                this.succeedCalls = succeedCalls;
            }

            public bool TryResolvePlacement(string placementId, int buildingInstanceId,
                out BuildingEquipmentPlacement placement, out BuildingFootprintAuthority building)
            {
                calls++;
                if (calls > succeedCalls)
                {
                    placement = null;
                    building = null;
                    return false;
                }
                return inner.TryResolvePlacement(placementId, buildingInstanceId, out placement, out building);
            }
        }

        /// <summary>D-7: one close call with the contents transfer inputs wired (buyer register + resolver + stove settlement).</summary>
        private static NpcAcquisitionResult CloseContentsDeal(Fixture f, NpcAcquisitionPlan plan,
            HouseholdFurnishingRegister seller, HouseholdFurnishingRegister buyer,
            IContentsPlacementResolver resolver, int? structureCents = StructureValueCents,
            HouseholdStoveRegistry sellerStoves = null, HouseholdState sellerHousehold = null)
        {
            return f.Acquisition.CloseAcquisition(
                f.Ids, plan, f.Authority, f.Registry, f.Cash, f.Titles, f.Housing,
                f.Negotiations, f.ClosingService, f.CreditEngine, f.Workflow, Day, f.Events, f.Diag,
                seller, structureCents, buyer, resolver, sellerStoves, sellerHousehold);
        }

        /// <summary>D-8: the seller side of a contents negotiation, configurable per test.</summary>
        private sealed class TestContentsSellerPolicy : INpcPropertySellerPolicy
        {
            public int ReservationBps = 9000; // of asking
            public bool AlwaysWalk;
            public int? CounterPriceCents; // explicit counter price; null = asking * CounterBps / 10000
            public int CounterBps = 9500;
            public PropertyContentsSelection CounterSelection; // null = price-only counter
            public bool WalkAfterCounter; // counter once, then walk whatever the buyer raises
            private bool countered;

            public int ReservationPriceCents(string parcelId, int askingPriceCents)
            {
                // Long math: asking * bps overflows int32 at these prices
                // (235000c * 10000 = 2.35 billion), which wraps the
                // reservation negative and makes the seller accept anything.
                return (int)((long)askingPriceCents * ReservationBps / 10000);
            }

            public NpcSellerResponse RespondToOffer(string parcelId, int askingPriceCents,
                int buyerOfferCents, int round, int maxRounds, List<string> diag)
            {
                return RespondToContentsOffer(parcelId, askingPriceCents, buyerOfferCents, null, round, maxRounds, diag);
            }

            public NpcSellerResponse RespondToContentsOffer(string parcelId, int askingPriceCents,
                int buyerOfferCents, PropertyContentsSelection requestedSelection,
                int round, int maxRounds, List<string> diag)
            {
                if (AlwaysWalk)
                    return new NpcSellerResponse { Kind = NpcSellerResponseKind.WalkAway, Reason = "the seller will not sell to this buyer." };
                int reservation = ReservationPriceCents(parcelId, askingPriceCents);
                if (countered)
                {
                    if (WalkAfterCounter)
                        return new NpcSellerResponse { Kind = NpcSellerResponseKind.WalkAway, Reason = "the seller will not go lower." };
                    if (buyerOfferCents >= reservation)
                        return new NpcSellerResponse { Kind = NpcSellerResponseKind.Accept, Reason = "meets the reservation." };
                    return new NpcSellerResponse { Kind = NpcSellerResponseKind.WalkAway, Reason = "the seller will not go lower." };
                }
                if (buyerOfferCents >= reservation)
                    return new NpcSellerResponse { Kind = NpcSellerResponseKind.Accept, Reason = "meets the reservation." };
                countered = true;
                return new NpcSellerResponse
                {
                    Kind = NpcSellerResponseKind.Counter,
                    // Long math, same overflow as the reservation above.
                    CounterPriceCents = CounterPriceCents ?? (int)((long)askingPriceCents * CounterBps / 10000),
                    CounterSelection = CounterSelection,
                    Reason = "first and only counter.",
                };
            }
        }

        /// <summary>D-8: one contents negotiation against the test house (structure 200000c, day 100).</summary>
        private static NpcContentsNegotiationResult NegotiateContents(
            Fixture f, TestContentsSellerPolicy seller, HouseholdFurnishingRegister register,
            NpcContentsBuyerRequest request, Func<string, int> priceFunc = null, int maxRounds = 3)
        {
            return f.Acquisition.NegotiateContents(seller, "parcel-farm", register, BuildingInstanceId,
                "household", SellerHouseholdId.ToString(), StructureValueCents, request,
                priceFunc ?? PriceForKind, maxRounds, Day, f.Diag);
        }

        private static NpcContentsBuyerRequest BuyerRequest(List<string> wantedKinds, int buyerMaxCents, int movingCostCents = 5000)
        {
            return new NpcContentsBuyerRequest
            {
                WantedKinds = wantedKinds,
                BuyerMaxCents = buyerMaxCents,
                MovingCostCents = movingCostCents,
                DayIndex = Day,
            };
        }

        [Test]
        public void Contents_All_Validates_AndCloses_CashAndTitleConserved()
        {
            var f = MakeDealFixture();
            string spaceId = f.RegisterFarmstead("parcel-farm", "household:" + SellerHouseholdId);
            HouseholdFurnishingRegister register = MakeFurnishedSellerRegisterTracked(
                out Dictionary<string, BuildingEquipmentPlacement> placements, out BuildingFootprintAuthority building);
            HouseholdFurnishingRegister buyerRegister = MakeRegister(BuyerHouseholdId);
            var resolver = new FakePlacementResolver(placements, building);
            PropertyContentsSelection contents = BuildAllFor(register);
            // Bed 10000 + table 8000 + chair 3000 + stove 12000 + shelf 2000 = 35000.
            Assert.AreEqual(35000, contents.TotalContentsValueCents);
            int price = StructureValueCents + contents.TotalContentsValueCents;
            NpcAcquisitionPlan plan = f.BasePlan("parcel-farm", spaceId, price);
            plan.Contents = contents;

            Assert.IsNull(f.Acquisition.ValidatePlan(plan, f.Titles, f.Negotiations, f.Cash, f.Diag, register, StructureValueCents));

            NpcAcquisitionResult result = CloseContentsDeal(f, plan, register, buyerRegister, resolver);
            Assert.IsTrue(result.Closed, result.FailureReason);
            Assert.AreEqual("household:" + BuyerHouseholdId, f.Titles.CurrentHolder("parcel-farm"));
            Assert.AreEqual(300000 - price, f.Cash.FindStore("household:" + BuyerHouseholdId).ReadBalanceCents());
            Assert.AreEqual(5000 + price, f.Cash.FindStore("household:" + SellerHouseholdId).ReadBalanceCents());
            Assert.IsTrue(result.CashConserved);
            bool revalidated = false;
            foreach (string line in f.Diag)
            {
                if (line.Contains("contents re-validated at close")) revalidated = true;
            }
            Assert.IsTrue(revalidated, "the close must record the contents re-validation.");

            // D-7: every item moved seller register → buyer register, in
            // the same building, at the same placement, capability
            // carried forward; the seller holds nothing.
            Assert.AreEqual(0, register.Assets.Count);
            Assert.AreEqual(0, register.Bindings.Count);
            Assert.AreEqual(0, register.Validate().Count);
            Assert.AreEqual(5, buyerRegister.Assets.Count);
            Assert.AreEqual(5, buyerRegister.Bindings.Count);
            Assert.AreEqual(0, buyerRegister.Validate().Count);
            foreach (string assetId in new[] { "bed-1", "table-1", "chair-1", "stove-1", "shelf-1" })
            {
                EquipmentAsset asset = buyerRegister.FindAsset(assetId);
                Assert.NotNull(asset, assetId);
                Assert.AreEqual("household", asset.OwnerKind);
                Assert.AreEqual(BuyerHouseholdId.ToString(), asset.OwnerId);
                Assert.AreEqual("building:" + BuildingInstanceId, asset.LocationId);
                FurnishingPlacementBinding binding = buyerRegister.LiveBindingFor(assetId);
                Assert.NotNull(binding, assetId);
                Assert.AreEqual(BuildingInstanceId, binding.BuildingInstanceId);
                Assert.AreEqual(FurnishingAcquisitionSource.Purchase, binding.AcquisitionSource);
                Assert.AreEqual("property-acquisition:plan-1", binding.AcquisitionSourceId);
            }
            FurnishingPlacementBinding bedBinding = buyerRegister.LiveBindingFor("bed-1");
            Assert.AreEqual(FurnishingCapabilityKind.SleepingPlace, bedBinding.CapabilityKind);
            Assert.AreEqual("sleeping-place-bed-1", bedBinding.CapabilityReferenceId);
            FurnishingPlacementBinding stoveBinding = buyerRegister.LiveBindingFor("stove-1");
            Assert.AreEqual(FurnishingCapabilityKind.StoveWorkstation, stoveBinding.CapabilityKind);
            Assert.AreEqual("stove", stoveBinding.CapabilityReferenceId);
            Assert.AreEqual(5, result.TransferredContentsAssetIds.Count);
            Assert.AreEqual(0, result.RemovedContentsAssetIds.Count);
            Assert.AreEqual(0, result.HeldInPlaceContentsAssetIds.Count);
        }

        [Test]
        public void Contents_Conservation_PriceMismatch_RefusedAtPlan()
        {
            var f = MakeDealFixture();
            f.RegisterFarmstead("parcel-farm", "household:" + SellerHouseholdId);
            HouseholdFurnishingRegister register = MakeFurnishedSellerRegister();
            PropertyContentsSelection contents = BuildAllFor(register);
            NpcAcquisitionPlan plan = f.BasePlan("parcel-farm", "space-x", StructureValueCents + contents.TotalContentsValueCents + 500);
            plan.Contents = contents;

            string problem = f.Acquisition.ValidatePlan(plan, f.Titles, f.Negotiations, f.Cash, f.Diag, register, StructureValueCents);
            Assert.NotNull(problem);
            Assert.IsTrue(problem.Contains("200000"), problem);
            Assert.IsTrue(problem.Contains("35000"), problem);
            Assert.IsTrue(problem.Contains("235500"), problem);
        }

        [Test]
        public void Contents_NoRegister_RefusedAtPlan()
        {
            var f = MakeDealFixture();
            f.RegisterFarmstead("parcel-farm", "household:" + SellerHouseholdId);
            HouseholdFurnishingRegister register = MakeFurnishedSellerRegister();
            PropertyContentsSelection contents = BuildAllFor(register);
            NpcAcquisitionPlan plan = f.BasePlan("parcel-farm", "space-x", StructureValueCents + contents.TotalContentsValueCents);
            plan.Contents = contents;

            string problem = f.Acquisition.ValidatePlan(plan, f.Titles, f.Negotiations, f.Cash, f.Diag, null, StructureValueCents);
            Assert.NotNull(problem);
            Assert.IsTrue(problem.Contains("no seller furnishing register"), problem);
        }

        [Test]
        public void Contents_WrongSeller_RefusedAtPlan()
        {
            var f = MakeDealFixture();
            f.RegisterHousehold(8, 5000);
            f.RegisterFarmstead("parcel-farm", "household:8");
            HouseholdFurnishingRegister register = MakeFurnishedSellerRegister();
            PropertyContentsSelection contents = BuildAllFor(register);
            NpcAcquisitionPlan plan = f.BasePlan("parcel-farm", "space-x", StructureValueCents + contents.TotalContentsValueCents);
            plan.SellerName = "household:8";
            plan.Contents = contents;

            string problem = f.Acquisition.ValidatePlan(plan, f.Titles, f.Negotiations, f.Cash, f.Diag, register, StructureValueCents);
            Assert.NotNull(problem);
            Assert.IsTrue(problem.Contains("household:8"), problem);
            Assert.IsTrue(problem.Contains("household 7"), problem);
        }

        [Test]
        public void Contents_UnpricedIncluded_RefusedAtPlan()
        {
            var f = MakeDealFixture();
            f.RegisterFarmstead("parcel-farm", "household:" + SellerHouseholdId);
            HouseholdFurnishingRegister register = MakeFurnishedSellerRegister();
            List<FurnishingContentsItem> items = PropertyContentsSelection.ListEligibleContents(register, BuildingInstanceId);
            // No replacement cost for the bed kind: the bed is unpriced, and
            // agreement-time validation with requirePriced refuses it loudly.
            PropertyContentsSelection contents = PropertyContentsSelection.BuildAll(items, "household",
                SellerHouseholdId.ToString(), BuildingInstanceId, Day, kind => kind == "furniture.bed" ? 0 : PriceForKind(kind));
            Assert.IsTrue(contents.UnpricedIncludedAssetIds.Contains("bed-1"));
            NpcAcquisitionPlan plan = f.BasePlan("parcel-farm", "space-x", StructureValueCents + contents.TotalContentsValueCents);
            plan.Contents = contents;

            string problem = f.Acquisition.ValidatePlan(plan, f.Titles, f.Negotiations, f.Cash, f.Diag, register, StructureValueCents);
            Assert.NotNull(problem);
            Assert.IsTrue(problem.Contains("bed-1"), problem);
            Assert.IsTrue(problem.Contains("no supplied replacement cost"), problem);
        }

        [Test]
        public void Contents_NotSellerOwned_RefusedAtPlan()
        {
            var f = MakeDealFixture();
            f.RegisterFarmstead("parcel-farm", "household:" + SellerHouseholdId);
            HouseholdFurnishingRegister register = MakeFurnishedSellerRegister();
            List<FurnishingContentsItem> items = PropertyContentsSelection.ListEligibleContents(register, BuildingInstanceId);
            // The selection names household 8 as the seller, but every item
            // is owned by household 7 — the seller has no authority.
            PropertyContentsSelection contents = PropertyContentsSelection.BuildAll(items, "household",
                "8", BuildingInstanceId, Day, PriceForKind);
            NpcAcquisitionPlan plan = f.BasePlan("parcel-farm", "space-x", StructureValueCents + contents.TotalContentsValueCents);
            plan.Contents = contents;

            string problem = f.Acquisition.ValidatePlan(plan, f.Titles, f.Negotiations, f.Cash, f.Diag, register, StructureValueCents);
            Assert.NotNull(problem);
            Assert.IsTrue(problem.Contains("not the seller"), problem);
        }

        [Test]
        public void Contents_ConditionChangedBeforeClose_FailsLoudly_NothingMoves()
        {
            var f = MakeDealFixture();
            string spaceId = f.RegisterFarmstead("parcel-farm", "household:" + SellerHouseholdId);
            HouseholdFurnishingRegister register = MakeFurnishedSellerRegister();
            PropertyContentsSelection contents = BuildAllFor(register);
            int price = StructureValueCents + contents.TotalContentsValueCents;
            NpcAcquisitionPlan plan = f.BasePlan("parcel-farm", spaceId, price);
            plan.Contents = contents;
            Assert.IsNull(f.Acquisition.ValidatePlan(plan, f.Titles, f.Negotiations, f.Cash, f.Diag, register, StructureValueCents));

            // Between agreement and close the bed is damaged: the agreed
            // value is stale, so the close must fail loudly, not silently
            // drop the bed from the contract.
            register.FindAsset("bed-1").Condition01 = 0.5f;

            NpcAcquisitionResult result = f.Acquisition.CloseAcquisition(
                f.Ids, plan, f.Authority, f.Registry, f.Cash, f.Titles, f.Housing,
                f.Negotiations, f.ClosingService, f.CreditEngine, f.Workflow, Day, f.Events, f.Diag,
                register, StructureValueCents);
            Assert.IsFalse(result.Closed);
            Assert.AreEqual(NpcAcquisitionFailureKind.ContentsRefused, result.FailureKind);
            Assert.IsTrue(result.FailureReason.Contains("bed-1"), result.FailureReason);
            Assert.IsTrue(result.FailureReason.Contains("condition changed"), result.FailureReason);
            Assert.IsTrue(result.UnwoundCleanly, "nothing moved before the re-validation, so the unwind is clean by construction.");
            Assert.AreEqual(300000, f.Cash.FindStore("household:" + BuyerHouseholdId).ReadBalanceCents());
            Assert.AreEqual(5000, f.Cash.FindStore("household:" + SellerHouseholdId).ReadBalanceCents());
            Assert.AreEqual("household:" + SellerHouseholdId, f.Titles.CurrentHolder("parcel-farm"));
        }

        [Test]
        public void Contents_IncludedItemRemovedBeforeClose_FailsLoudly()
        {
            var f = MakeDealFixture();
            string spaceId = f.RegisterFarmstead("parcel-farm", "household:" + SellerHouseholdId);
            HouseholdFurnishingRegister register = MakeFurnishedSellerRegister();
            PropertyContentsSelection contents = BuildAllFor(register);
            int price = StructureValueCents + contents.TotalContentsValueCents;
            NpcAcquisitionPlan plan = f.BasePlan("parcel-farm", spaceId, price);
            plan.Contents = contents;
            Assert.IsNull(f.Acquisition.ValidatePlan(plan, f.Titles, f.Negotiations, f.Cash, f.Diag, register, StructureValueCents));

            // Between agreement and close the seller removes the bed from
            // the building: the included item is gone, so the close fails.
            Assert.IsNull(register.TryRemove("bed-1"));

            NpcAcquisitionResult result = f.Acquisition.CloseAcquisition(
                f.Ids, plan, f.Authority, f.Registry, f.Cash, f.Titles, f.Housing,
                f.Negotiations, f.ClosingService, f.CreditEngine, f.Workflow, Day, f.Events, f.Diag,
                register, StructureValueCents);
            Assert.IsFalse(result.Closed);
            Assert.AreEqual(NpcAcquisitionFailureKind.ContentsRefused, result.FailureKind);
            Assert.IsTrue(result.FailureReason.Contains("bed-1"), result.FailureReason);
            Assert.IsTrue(result.UnwoundCleanly);
            Assert.AreEqual("household:" + SellerHouseholdId, f.Titles.CurrentHolder("parcel-farm"));
        }

        [Test]
        public void Contents_ExcludedItemDrift_IsNote_NotFailure()
        {
            var f = MakeDealFixture();
            string spaceId = f.RegisterFarmstead("parcel-farm", "household:" + SellerHouseholdId);
            HouseholdFurnishingRegister register = MakeFurnishedSellerRegisterTracked(
                out Dictionary<string, BuildingEquipmentPlacement> placements, out BuildingFootprintAuthority building);
            List<FurnishingContentsItem> items = PropertyContentsSelection.ListEligibleContents(register, BuildingInstanceId);
            // No Movable: fixtures only; the bed is excluded (RemovedBySeller).
            PropertyContentsSelection contents = PropertyContentsSelection.BuildNoMovable(items, "household",
                SellerHouseholdId.ToString(), BuildingInstanceId, Day, PriceForKind);
            int price = StructureValueCents + contents.TotalContentsValueCents;
            NpcAcquisitionPlan plan = f.BasePlan("parcel-farm", spaceId, price);
            plan.Contents = contents;
            Assert.IsNull(f.Acquisition.ValidatePlan(plan, f.Titles, f.Negotiations, f.Cash, f.Diag, register, StructureValueCents));

            // Between agreement and close the seller sells the excluded bed
            // elsewhere: the seller's own property leaving is a note, not a
            // breach — the close still succeeds. The transfer does not
            // touch the bed (it is no longer the seller's to remove), but
            // the other excluded movables are removed to seller custody
            // and the fixtures transfer to the buyer.
            register.FindAsset("bed-1").OwnerId = "8";

            HouseholdFurnishingRegister buyerRegister = MakeRegister(BuyerHouseholdId);
            var resolver = new FakePlacementResolver(placements, building);
            NpcAcquisitionResult result = CloseContentsDeal(f, plan, register, buyerRegister, resolver);
            Assert.IsTrue(result.Closed, result.FailureReason);
            bool noted = false;
            foreach (string line in f.Diag)
            {
                if (line.Contains("excluded") && line.Contains("bed-1")) noted = true;
            }
            Assert.IsTrue(noted, "the excluded bed leaving must be reported as a note.");
            Assert.AreEqual(2, result.TransferredContentsAssetIds.Count);
            Assert.IsTrue(result.TransferredContentsAssetIds.Contains("stove-1"));
            Assert.IsTrue(result.TransferredContentsAssetIds.Contains("shelf-1"));
            Assert.AreEqual(2, result.RemovedContentsAssetIds.Count);
            Assert.IsTrue(result.RemovedContentsAssetIds.Contains("table-1"));
            Assert.IsTrue(result.RemovedContentsAssetIds.Contains("chair-1"));
            Assert.NotNull(buyerRegister.FindAsset("stove-1"));
            Assert.NotNull(buyerRegister.LiveBindingFor("stove-1"));
            Assert.AreEqual("household:" + SellerHouseholdId, register.FindAsset("table-1").LocationId);
            Assert.IsNull(register.LiveBindingFor("table-1"));
            // The bed was never touched: still installed in the seller's
            // register (the seller's own sale elsewhere moves it, not
            // this transfer), still owned by household 8.
            Assert.AreEqual("8", register.FindAsset("bed-1").OwnerId);
            Assert.NotNull(register.LiveBindingFor("bed-1"));
        }

        [Test]
        public void Contents_NoMovable_FixturesOnly_ConservationHolds()
        {
            var f = MakeDealFixture();
            string spaceId = f.RegisterFarmstead("parcel-farm", "household:" + SellerHouseholdId);
            HouseholdFurnishingRegister register = MakeFurnishedSellerRegisterTracked(
                out Dictionary<string, BuildingEquipmentPlacement> placements, out BuildingFootprintAuthority building);
            List<FurnishingContentsItem> items = PropertyContentsSelection.ListEligibleContents(register, BuildingInstanceId);
            PropertyContentsSelection contents = PropertyContentsSelection.BuildNoMovable(items, "household",
                SellerHouseholdId.ToString(), BuildingInstanceId, Day, PriceForKind);
            // Stove 12000 + shelf 2000 = 14000; every movable is excluded.
            Assert.AreEqual(14000, contents.TotalContentsValueCents);
            Assert.AreEqual(14000, contents.IncludedFixtureValueCents);
            Assert.AreEqual(0, contents.IncludedMovableValueCents);
            int price = StructureValueCents + contents.TotalContentsValueCents;
            NpcAcquisitionPlan plan = f.BasePlan("parcel-farm", spaceId, price);
            plan.Contents = contents;

            Assert.IsNull(f.Acquisition.ValidatePlan(plan, f.Titles, f.Negotiations, f.Cash, f.Diag, register, StructureValueCents));
            HouseholdFurnishingRegister buyerRegister = MakeRegister(BuyerHouseholdId);
            var resolver = new FakePlacementResolver(placements, building);
            NpcAcquisitionResult result = CloseContentsDeal(f, plan, register, buyerRegister, resolver);
            Assert.IsTrue(result.Closed, result.FailureReason);
            // D-7: the fixtures moved to the buyer; every movable was
            // removed by the seller to custody; nothing is held in place.
            Assert.AreEqual(2, result.TransferredContentsAssetIds.Count);
            Assert.IsTrue(result.TransferredContentsAssetIds.Contains("stove-1"));
            Assert.IsTrue(result.TransferredContentsAssetIds.Contains("shelf-1"));
            Assert.AreEqual(3, result.RemovedContentsAssetIds.Count);
            Assert.AreEqual(0, result.HeldInPlaceContentsAssetIds.Count);
            Assert.AreEqual(2, buyerRegister.Assets.Count);
            Assert.AreEqual(2, buyerRegister.Bindings.Count);
            Assert.AreEqual(0, buyerRegister.Validate().Count);
            foreach (string assetId in new[] { "bed-1", "table-1", "chair-1" })
            {
                Assert.AreEqual("household:" + SellerHouseholdId, register.FindAsset(assetId).LocationId);
                Assert.IsNull(register.LiveBindingFor(assetId));
            }
        }

        [Test]
        public void Contents_Custom_OnlyBed_ConservationHolds()
        {
            var f = MakeDealFixture();
            string spaceId = f.RegisterFarmstead("parcel-farm", "household:" + SellerHouseholdId);
            HouseholdFurnishingRegister register = MakeFurnishedSellerRegisterTracked(
                out Dictionary<string, BuildingEquipmentPlacement> placements, out BuildingFootprintAuthority building);
            List<FurnishingContentsItem> items = PropertyContentsSelection.ListEligibleContents(register, BuildingInstanceId);
            PropertyContentsSelection contents = PropertyContentsSelection.BuildCustom(items,
                new List<string> { "bed-1" }, "household",
                SellerHouseholdId.ToString(), BuildingInstanceId, Day, PriceForKind);
            Assert.AreEqual(10000, contents.TotalContentsValueCents);
            Assert.AreEqual(0, contents.IncludedFixtureValueCents);
            Assert.AreEqual(10000, contents.IncludedMovableValueCents);
            int price = StructureValueCents + contents.TotalContentsValueCents;
            NpcAcquisitionPlan plan = f.BasePlan("parcel-farm", spaceId, price);
            plan.Contents = contents;

            Assert.IsNull(f.Acquisition.ValidatePlan(plan, f.Titles, f.Negotiations, f.Cash, f.Diag, register, StructureValueCents));
            HouseholdFurnishingRegister buyerRegister = MakeRegister(BuyerHouseholdId);
            var resolver = new FakePlacementResolver(placements, building);
            NpcAcquisitionResult result = CloseContentsDeal(f, plan, register, buyerRegister, resolver);
            Assert.IsTrue(result.Closed, result.FailureReason);
            // D-7: only the bed moved. The excluded fixtures (stove,
            // shelf) stay installed as the seller's property in the
            // building the buyer now owns — held in place, ownership and
            // binding untouched. The other movables were removed by the
            // seller to custody.
            Assert.AreEqual(1, result.TransferredContentsAssetIds.Count);
            Assert.IsTrue(result.TransferredContentsAssetIds.Contains("bed-1"));
            Assert.AreEqual(2, result.HeldInPlaceContentsAssetIds.Count);
            Assert.IsTrue(result.HeldInPlaceContentsAssetIds.Contains("stove-1"));
            Assert.IsTrue(result.HeldInPlaceContentsAssetIds.Contains("shelf-1"));
            Assert.AreEqual(2, result.RemovedContentsAssetIds.Count);
            Assert.IsTrue(result.RemovedContentsAssetIds.Contains("table-1"));
            Assert.IsTrue(result.RemovedContentsAssetIds.Contains("chair-1"));
            Assert.AreEqual(1, buyerRegister.Assets.Count);
            Assert.NotNull(buyerRegister.LiveBindingFor("bed-1"));
            foreach (string assetId in new[] { "stove-1", "shelf-1" })
            {
                EquipmentAsset asset = register.FindAsset(assetId);
                Assert.NotNull(asset, assetId);
                Assert.AreEqual(SellerHouseholdId.ToString(), asset.OwnerId);
                Assert.AreEqual("building:" + BuildingInstanceId, asset.LocationId);
                Assert.NotNull(register.LiveBindingFor(assetId));
            }
            BuildingEquipmentPlacement stovePlacement = placements[register.LiveBindingFor("stove-1").PlacementId];
            Assert.AreEqual(SellerHouseholdId.ToString(), stovePlacement.EconomicOwnerId);
            Assert.AreEqual(0, register.Validate().Count);
            Assert.AreEqual(0, buyerRegister.Validate().Count);
        }

        [Test]
        public void Contents_CustodyAsset_NotEligible()
        {
            HouseholdFurnishingRegister register = MakeFurnishedSellerRegister();
            // A crate in household custody (registered, never installed) is
            // not part of the property and stays with the seller without a line.
            Assert.IsNull(register.TryRegisterAsset(MakeAsset("crate-1", "furniture.crate")));
            List<FurnishingContentsItem> items = PropertyContentsSelection.ListEligibleContents(register, BuildingInstanceId);
            Assert.AreEqual(5, items.Count);
            PropertyContentsSelection contents = PropertyContentsSelection.BuildAll(items, "household",
                SellerHouseholdId.ToString(), BuildingInstanceId, Day, PriceForKind);
            Assert.AreEqual(5, contents.Lines.Count);
            Assert.AreEqual(35000, contents.TotalContentsValueCents);
            Assert.AreEqual(0, contents.Validate(register, requirePriced: true).Count);
        }

        [Test]
        public void Contents_FixtureValue_NotDoubleCounted()
        {
            HouseholdFurnishingRegister register = MakeFurnishedSellerRegister();
            PropertyContentsSelection contents = BuildAllFor(register);
            // Included fixtures: stove 12000 + shelf 2000 = 14000.
            Assert.AreEqual(14000, contents.IncludedFixtureValueCents);
            int price = StructureValueCents + contents.TotalContentsValueCents;
            // The structure value EXCLUDES included fixtures: balanced.
            Assert.IsNull(PropertyContentsSelection.CheckConservation(StructureValueCents, contents.TotalContentsValueCents, price));
            // A structure value that still carries the fixtures double-counts
            // them: off by exactly the included fixture value.
            string problem = PropertyContentsSelection.CheckConservation(
                StructureValueCents + contents.IncludedFixtureValueCents, contents.TotalContentsValueCents, price);
            Assert.NotNull(problem);
            Assert.IsTrue(problem.Contains("14000"), problem);
        }

        [Test]
        public void Contents_Null_PlanBehavesAsBefore()
        {
            var f = MakeDealFixture();
            string spaceId = f.RegisterFarmstead("parcel-farm", "household:" + SellerHouseholdId);
            NpcAcquisitionPlan plan = f.BasePlan("parcel-farm", spaceId, StructureValueCents);
            plan.Contents = null;

            Assert.IsNull(f.Acquisition.ValidatePlan(plan, f.Titles, f.Negotiations, f.Cash, f.Diag));
            NpcAcquisitionResult result = f.Acquisition.CloseAcquisition(
                f.Ids, plan, f.Authority, f.Registry, f.Cash, f.Titles, f.Housing,
                f.Negotiations, f.ClosingService, f.CreditEngine, f.Workflow, Day, f.Events, f.Diag);
            Assert.IsTrue(result.Closed, result.FailureReason);
            Assert.AreEqual("household:" + BuyerHouseholdId, f.Titles.CurrentHolder("parcel-farm"));
        }

        [Test]
        public void Contents_StructureValueMissing_ConservationSkippedWithNote()
        {
            var f = MakeDealFixture();
            f.RegisterFarmstead("parcel-farm", "household:" + SellerHouseholdId);
            HouseholdFurnishingRegister register = MakeFurnishedSellerRegister();
            PropertyContentsSelection contents = BuildAllFor(register);
            NpcAcquisitionPlan plan = f.BasePlan("parcel-farm", "space-x", StructureValueCents + contents.TotalContentsValueCents);
            plan.Contents = contents;

            Assert.IsNull(f.Acquisition.ValidatePlan(plan, f.Titles, f.Negotiations, f.Cash, f.Diag, register, null));
            bool noted = false;
            foreach (string line in f.Diag)
            {
                if (line.Contains("contents conservation") && line.Contains("not checked")) noted = true;
            }
            Assert.IsTrue(noted, "a skipped conservation check must be recorded in diagnostics, never silent.");
        }

        // ---- D-7: the closing transfer ----

        [Test]
        public void Transfer_StoveFuel_ReturnsToSellerReserve()
        {
            var f = MakeDealFixture();
            string spaceId = f.RegisterFarmstead("parcel-farm", "household:" + SellerHouseholdId);
            HouseholdFurnishingRegister register = MakeFurnishedSellerRegisterTracked(
                out Dictionary<string, BuildingEquipmentPlacement> placements, out BuildingFootprintAuthority building);
            // The seller's stove holds 6 fuel-wood units from a 10-unit reserve.
            var sellerHousehold = new HouseholdState { id = SellerHouseholdId };
            HouseholdReserveState reserve = sellerHousehold.GetReserve(HouseholdReserveCatalog.FuelWoodCategoryId);
            Assert.NotNull(reserve, "a household must have a fuel-wood reserve.");
            reserve.currentUnits = 10;
            var sellerStoves = new HouseholdStoveRegistry(SellerHouseholdId, register);
            sellerStoves.RebuildFromRegister();
            Assert.NotNull(sellerStoves.FindStove("stove-1"));
            Assert.IsNull(sellerStoves.TryFeedStove("stove-1", 6, sellerHousehold, f.Diag));
            Assert.AreEqual(4, reserve.currentUnits);
            Assert.AreEqual(6, sellerStoves.FuelWoodUnitsOnHand("stove-1"));

            HouseholdFurnishingRegister buyerRegister = MakeRegister(BuyerHouseholdId);
            var resolver = new FakePlacementResolver(placements, building);
            PropertyContentsSelection contents = BuildAllFor(register);
            int price = StructureValueCents + contents.TotalContentsValueCents;
            NpcAcquisitionPlan plan = f.BasePlan("parcel-farm", spaceId, price);
            plan.Contents = contents;

            NpcAcquisitionResult result = CloseContentsDeal(f, plan, register, buyerRegister, resolver,
                StructureValueCents, sellerStoves, sellerHousehold);
            Assert.IsTrue(result.Closed, result.FailureReason);
            // The fuel was never priced in the contents: it returns to
            // the seller's reserve, the stove transfers empty, and no
            // orphaned fuel is left in the seller's stove registry.
            Assert.AreEqual(10, reserve.currentUnits);
            sellerStoves.RebuildFromRegister();
            Assert.AreEqual(0, sellerStoves.Validate().Count);
            Assert.AreEqual(0, sellerStoves.FuelWoodUnitsOnHand("stove-1"));
            var buyerStoves = new HouseholdStoveRegistry(BuyerHouseholdId, buyerRegister);
            buyerStoves.RebuildFromRegister();
            Assert.NotNull(buyerStoves.FindStove("stove-1"));
            Assert.AreEqual(0, buyerStoves.FuelWoodUnitsOnHand("stove-1"));
        }

        [Test]
        public void Transfer_NoBuyerRegister_RefusedBeforeMoneyMoves()
        {
            var f = MakeDealFixture();
            string spaceId = f.RegisterFarmstead("parcel-farm", "household:" + SellerHouseholdId);
            HouseholdFurnishingRegister register = MakeFurnishedSellerRegisterTracked(
                out Dictionary<string, BuildingEquipmentPlacement> placements, out BuildingFootprintAuthority building);
            var resolver = new FakePlacementResolver(placements, building);
            PropertyContentsSelection contents = BuildAllFor(register);
            int price = StructureValueCents + contents.TotalContentsValueCents;
            NpcAcquisitionPlan plan = f.BasePlan("parcel-farm", spaceId, price);
            plan.Contents = contents;

            NpcAcquisitionResult result = CloseContentsDeal(f, plan, register, null, resolver);
            Assert.IsFalse(result.Closed);
            Assert.AreEqual(NpcAcquisitionFailureKind.ContentsRefused, result.FailureKind);
            Assert.IsTrue(result.FailureReason.Contains("no buyer furnishing register"), result.FailureReason);
            Assert.IsTrue(result.UnwoundCleanly);
            Assert.AreEqual(300000, f.Cash.FindStore("household:" + BuyerHouseholdId).ReadBalanceCents());
            Assert.AreEqual(5000, f.Cash.FindStore("household:" + SellerHouseholdId).ReadBalanceCents());
            Assert.AreEqual("household:" + SellerHouseholdId, f.Titles.CurrentHolder("parcel-farm"));
            Assert.AreEqual(5, register.Assets.Count);
            Assert.AreEqual(5, register.Bindings.Count);
        }

        [Test]
        public void Transfer_NoResolver_RefusedBeforeMoneyMoves()
        {
            var f = MakeDealFixture();
            string spaceId = f.RegisterFarmstead("parcel-farm", "household:" + SellerHouseholdId);
            HouseholdFurnishingRegister register = MakeFurnishedSellerRegisterTracked(
                out Dictionary<string, BuildingEquipmentPlacement> placements, out BuildingFootprintAuthority building);
            HouseholdFurnishingRegister buyerRegister = MakeRegister(BuyerHouseholdId);
            PropertyContentsSelection contents = BuildAllFor(register);
            int price = StructureValueCents + contents.TotalContentsValueCents;
            NpcAcquisitionPlan plan = f.BasePlan("parcel-farm", spaceId, price);
            plan.Contents = contents;

            NpcAcquisitionResult result = CloseContentsDeal(f, plan, register, buyerRegister, null);
            Assert.IsFalse(result.Closed);
            Assert.AreEqual(NpcAcquisitionFailureKind.ContentsRefused, result.FailureKind);
            Assert.IsTrue(result.FailureReason.Contains("no placement resolver"), result.FailureReason);
            Assert.IsTrue(result.UnwoundCleanly);
            Assert.AreEqual(300000, f.Cash.FindStore("household:" + BuyerHouseholdId).ReadBalanceCents());
            Assert.AreEqual(5, register.Assets.Count);
            Assert.AreEqual(0, buyerRegister.Assets.Count);
        }

        [Test]
        public void Transfer_ReservedAsset_RefusedBeforeMoneyMoves()
        {
            var f = MakeDealFixture();
            string spaceId = f.RegisterFarmstead("parcel-farm", "household:" + SellerHouseholdId);
            HouseholdFurnishingRegister register = MakeFurnishedSellerRegisterTracked(
                out Dictionary<string, BuildingEquipmentPlacement> placements, out BuildingFootprintAuthority building);
            HouseholdFurnishingRegister buyerRegister = MakeRegister(BuyerHouseholdId);
            var resolver = new FakePlacementResolver(placements, building);
            PropertyContentsSelection contents = BuildAllFor(register);
            int price = StructureValueCents + contents.TotalContentsValueCents;
            NpcAcquisitionPlan plan = f.BasePlan("parcel-farm", spaceId, price);
            plan.Contents = contents;
            Assert.IsNull(f.Acquisition.ValidatePlan(plan, f.Titles, f.Negotiations, f.Cash, f.Diag, register, StructureValueCents));

            // Between agreement and close the bed is reserved for a task:
            // the single transfer authority refuses a reserved asset, so
            // the pre-flight fails the deal before any money moves.
            Assert.IsNull(register.FindAsset("bed-1").Reserve("task-1", "meal prep"));

            NpcAcquisitionResult result = CloseContentsDeal(f, plan, register, buyerRegister, resolver);
            Assert.IsFalse(result.Closed);
            Assert.AreEqual(NpcAcquisitionFailureKind.ContentsRefused, result.FailureKind);
            Assert.IsTrue(result.FailureReason.Contains("bed-1"), result.FailureReason);
            Assert.IsTrue(result.FailureReason.Contains("reserved"), result.FailureReason);
            Assert.IsTrue(result.UnwoundCleanly);
            Assert.AreEqual(300000, f.Cash.FindStore("household:" + BuyerHouseholdId).ReadBalanceCents());
            Assert.AreEqual(5000, f.Cash.FindStore("household:" + SellerHouseholdId).ReadBalanceCents());
            Assert.AreEqual("household:" + SellerHouseholdId, f.Titles.CurrentHolder("parcel-farm"));
            Assert.AreEqual(5, register.Assets.Count);
            Assert.AreEqual(5, register.Bindings.Count);
            Assert.AreEqual(0, buyerRegister.Assets.Count);
        }

        [Test]
        public void Transfer_PlanBuildingMismatch_RefusedAtPlan()
        {
            var f = MakeDealFixture();
            f.RegisterFarmstead("parcel-farm", "household:" + SellerHouseholdId);
            HouseholdFurnishingRegister register = MakeFurnishedSellerRegister();
            PropertyContentsSelection contents = BuildAllFor(register);
            NpcAcquisitionPlan plan = f.BasePlan("parcel-farm", "space-x", StructureValueCents + contents.TotalContentsValueCents);
            plan.Contents = contents;
            plan.BuildingInstanceId = 102;

            string problem = f.Acquisition.ValidatePlan(plan, f.Titles, f.Negotiations, f.Cash, f.Diag, register, StructureValueCents);
            Assert.NotNull(problem);
            Assert.IsTrue(problem.Contains("102"), problem);
            Assert.IsTrue(problem.Contains("101"), problem);
        }

        [Test]
        public void Transfer_PlanBuildingUnbound_RefusedAtPlan()
        {
            var f = MakeDealFixture();
            f.RegisterFarmstead("parcel-farm", "household:" + SellerHouseholdId);
            HouseholdFurnishingRegister register = MakeFurnishedSellerRegister();
            PropertyContentsSelection contents = BuildAllFor(register);
            NpcAcquisitionPlan plan = f.BasePlan("parcel-farm", "space-x", StructureValueCents + contents.TotalContentsValueCents);
            plan.Contents = contents;
            plan.BuildingInstanceId = -1;

            string problem = f.Acquisition.ValidatePlan(plan, f.Titles, f.Negotiations, f.Cash, f.Diag, register, StructureValueCents);
            Assert.NotNull(problem);
            Assert.IsTrue(problem.Contains("no building instance"), problem);
        }

        [Test]
        public void Transfer_ConservationRecheckedAtClose()
        {
            var f = MakeDealFixture();
            string spaceId = f.RegisterFarmstead("parcel-farm", "household:" + SellerHouseholdId);
            HouseholdFurnishingRegister register = MakeFurnishedSellerRegisterTracked(
                out Dictionary<string, BuildingEquipmentPlacement> placements, out BuildingFootprintAuthority building);
            HouseholdFurnishingRegister buyerRegister = MakeRegister(BuyerHouseholdId);
            var resolver = new FakePlacementResolver(placements, building);
            PropertyContentsSelection contents = BuildAllFor(register);
            int price = StructureValueCents + contents.TotalContentsValueCents;
            NpcAcquisitionPlan plan = f.BasePlan("parcel-farm", spaceId, price);
            plan.Contents = contents;
            Assert.IsNull(f.Acquisition.ValidatePlan(plan, f.Titles, f.Negotiations, f.Cash, f.Diag, register, StructureValueCents));

            // The structure value at close (190000) no longer matches the
            // plan (200000): the close re-checks conservation and fails
            // loudly, before any money moves.
            NpcAcquisitionResult result = CloseContentsDeal(f, plan, register, buyerRegister, resolver, 190000);
            Assert.IsFalse(result.Closed);
            Assert.AreEqual(NpcAcquisitionFailureKind.ContentsRefused, result.FailureKind);
            Assert.IsTrue(result.FailureReason.Contains("190000"), result.FailureReason);
            Assert.IsTrue(result.UnwoundCleanly);
            Assert.AreEqual(300000, f.Cash.FindStore("household:" + BuyerHouseholdId).ReadBalanceCents());
            Assert.AreEqual(5, register.Assets.Count);
            Assert.AreEqual(0, buyerRegister.Assets.Count);
        }

        [Test]
        public void Transfer_PlacementUnresolvable_RefusedBeforeMoneyMoves()
        {
            var f = MakeDealFixture();
            string spaceId = f.RegisterFarmstead("parcel-farm", "household:" + SellerHouseholdId);
            HouseholdFurnishingRegister register = MakeFurnishedSellerRegisterTracked(
                out Dictionary<string, BuildingEquipmentPlacement> placements, out BuildingFootprintAuthority building);
            HouseholdFurnishingRegister buyerRegister = MakeRegister(BuyerHouseholdId);
            // The bed's placement component is gone (a retired scene
            // object): the pre-flight fails the deal before any money
            // moves — the transfer never invents a placement.
            string bedPlacementId = register.LiveBindingFor("bed-1").PlacementId;
            var resolver = new FakePlacementResolver(placements, building,
                new HashSet<string> { bedPlacementId });
            PropertyContentsSelection contents = BuildAllFor(register);
            int price = StructureValueCents + contents.TotalContentsValueCents;
            NpcAcquisitionPlan plan = f.BasePlan("parcel-farm", spaceId, price);
            plan.Contents = contents;

            NpcAcquisitionResult result = CloseContentsDeal(f, plan, register, buyerRegister, resolver);
            Assert.IsFalse(result.Closed);
            Assert.AreEqual(NpcAcquisitionFailureKind.ContentsRefused, result.FailureKind);
            Assert.IsTrue(result.FailureReason.Contains("bed-1"), result.FailureReason);
            Assert.IsTrue(result.UnwoundCleanly);
            Assert.AreEqual(300000, f.Cash.FindStore("household:" + BuyerHouseholdId).ReadBalanceCents());
            Assert.AreEqual(5, register.Assets.Count);
            Assert.AreEqual(0, buyerRegister.Assets.Count);
        }

        [Test]
        public void Transfer_Rollback_RestoresSellerRegister_OnMidTransferFailure()
        {
            HouseholdFurnishingRegister register = MakeFurnishedSellerRegisterTracked(
                out Dictionary<string, BuildingEquipmentPlacement> placements, out BuildingFootprintAuthority building);
            HouseholdFurnishingRegister buyerRegister = MakeRegister(BuyerHouseholdId);
            PropertyContentsSelection contents = BuildAllFor(register);
            // The internal pre-flight resolves 5 placements (calls 1-5);
            // the execution resolves the bed (call 6, succeeds — the bed
            // transfers) and then the table (call 7, fails). The rollback
            // must restore the bed, and nothing stands moved.
            var resolver = new CountingResolver(new FakePlacementResolver(placements, building), 6);
            var diag = new List<string>();
            PropertyContentsTransferResult transfer = PropertyContentsTransfer.TransferAtClose(
                contents, register, buyerRegister, resolver, "property-acquisition:plan-1", Day, null, null, diag);
            Assert.IsFalse(transfer.Succeeded);
            Assert.IsTrue(transfer.Problems.Count > 0);
            Assert.IsTrue(string.Join("; ", transfer.Problems).Contains("table-1"), string.Join("; ", transfer.Problems));
            Assert.AreEqual(0, transfer.RollbackProblems.Count);
            Assert.AreEqual(0, transfer.TransferredAssetIds.Count);
            Assert.AreEqual(0, transfer.RemovedBySellerAssetIds.Count);
            Assert.AreEqual(5, register.Assets.Count);
            Assert.AreEqual(5, register.Bindings.Count);
            Assert.AreEqual(0, register.Validate().Count);
            Assert.AreEqual(0, buyerRegister.Assets.Count);
            Assert.AreEqual(0, buyerRegister.Bindings.Count);
            Assert.AreEqual(0, buyerRegister.Validate().Count);
            foreach (string assetId in new[] { "bed-1", "table-1", "chair-1", "stove-1", "shelf-1" })
            {
                Assert.AreEqual(SellerHouseholdId.ToString(), register.FindAsset(assetId).OwnerId);
                Assert.AreEqual("building:" + BuildingInstanceId, register.FindAsset(assetId).LocationId);
                Assert.NotNull(register.LiveBindingFor(assetId));
            }
        }

        [Test]
        public void Transfer_MidTransferFailure_UnwindsCashAndTitle()
        {
            var f = MakeDealFixture();
            string spaceId = f.RegisterFarmstead("parcel-farm", "household:" + SellerHouseholdId);
            HouseholdFurnishingRegister register = MakeFurnishedSellerRegisterTracked(
                out Dictionary<string, BuildingEquipmentPlacement> placements, out BuildingFootprintAuthority building);
            HouseholdFurnishingRegister buyerRegister = MakeRegister(BuyerHouseholdId);
            PropertyContentsSelection contents = BuildAllFor(register);
            int price = StructureValueCents + contents.TotalContentsValueCents;
            NpcAcquisitionPlan plan = f.BasePlan("parcel-farm", spaceId, price);
            plan.Contents = contents;
            // Call count: the close's pre-flight resolves 5 placements
            // (calls 1-5), TransferAtClose's own pre-flight resolves 5
            // more (6-10), the execution resolves the bed (call 11,
            // succeeds — the bed transfers, and the cash leg has already
            // moved) and then the table (call 12, fails). The deal must
            // unwind the cash and roll the bed back.
            var resolver = new CountingResolver(new FakePlacementResolver(placements, building), 11);
            NpcAcquisitionResult result = CloseContentsDeal(f, plan, register, buyerRegister, resolver);
            Assert.IsFalse(result.Closed);
            Assert.AreEqual(NpcAcquisitionFailureKind.ContentsRefused, result.FailureKind);
            Assert.IsTrue(result.FailureReason.Contains("table-1"), result.FailureReason);
            Assert.IsTrue(result.UnwoundCleanly);
            Assert.AreEqual(300000, f.Cash.FindStore("household:" + BuyerHouseholdId).ReadBalanceCents());
            Assert.AreEqual(5000, f.Cash.FindStore("household:" + SellerHouseholdId).ReadBalanceCents());
            Assert.AreEqual("household:" + SellerHouseholdId, f.Titles.CurrentHolder("parcel-farm"));
            Assert.AreEqual(0, result.TransferredContentsAssetIds.Count);
            Assert.AreEqual(5, register.Assets.Count);
            Assert.AreEqual(5, register.Bindings.Count);
            Assert.AreEqual(0, register.Validate().Count);
            Assert.AreEqual(0, buyerRegister.Assets.Count);
        }

        // ---- D-8: NPC contents negotiation ----

        [Test]
        public void Negotiation_BuyerWantsBedAndTable_AcceptsOpeningOffer()
        {
            var f = MakeDealFixture();
            HouseholdFurnishingRegister register = MakeFurnishedSellerRegisterTracked(
                out Dictionary<string, BuildingEquipmentPlacement> placements, out BuildingFootprintAuthority building);
            var seller = new TestContentsSellerPolicy { ReservationBps = 9000 };
            NpcContentsNegotiationResult result = NegotiateContents(f, seller, register,
                BuyerRequest(new List<string> { "furniture.bed", "furniture.table" }, 300000));
            Assert.IsTrue(result.Agreed, result.FailureReason);
            // Fixtures 14000 + bed 10000 + table 8000 = 32000 contents;
            // asking 232000; the opening offer discounts the structure
            // 10%: 32000 + 180000 = 212000.
            Assert.AreEqual(212000, result.AgreedPriceCents);
            Assert.AreEqual(32000, result.AgreedSelection.TotalContentsValueCents);
            Assert.AreEqual(180000, result.ImpliedStructureValueCents);
            Assert.AreEqual(ContentsSelectionMode.Custom, result.AgreedSelection.Mode);
            // The agreed price respects the F6 anchor and D-6 conservation:
            // the structure absorbs the discount, the contents keep their
            // frozen agreed values.
            Assert.IsNull(PropertyContentsSelection.CheckConservation(180000, 32000, 212000));
            Assert.IsTrue(result.AgreedPriceCents < 232000, "the agreed price must stay below the anchor.");
            FurnishingContentsLine bed = result.AgreedSelection.Lines.Find(l => l.AssetId == "bed-1");
            Assert.NotNull(bed);
            Assert.IsTrue(bed.Included);
            FurnishingContentsLine chair = result.AgreedSelection.Lines.Find(l => l.AssetId == "chair-1");
            Assert.NotNull(chair);
            Assert.IsFalse(chair.Included);
            Assert.AreEqual(ExcludedItemDisposition.RemovedBySeller, chair.Disposition);
        }

        [Test]
        public void Negotiation_CapitalCapsSelection_SkipsUnaffordableKinds()
        {
            var f = MakeDealFixture();
            HouseholdFurnishingRegister register = MakeFurnishedSellerRegisterTracked(
                out Dictionary<string, BuildingEquipmentPlacement> placements, out BuildingFootprintAuthority building);
            var seller = new TestContentsSellerPolicy { ReservationBps = 9000 };
            // Capital 220000: structure 200000 + fixtures 14000 = 214000
            // floor. Bed 10000 -> 224000 (skip), table 8000 -> 222000
            // (skip), chair 3000 -> 217000 (fits).
            NpcContentsNegotiationResult result = NegotiateContents(f, seller, register,
                BuyerRequest(new List<string> { "furniture.bed", "furniture.table", "furniture.chair" }, 220000));
            Assert.IsTrue(result.Agreed, result.FailureReason);
            Assert.AreEqual(17000, result.AgreedSelection.TotalContentsValueCents);
            Assert.AreEqual(197000, result.AgreedPriceCents);
            Assert.AreEqual(180000, result.ImpliedStructureValueCents);
            Assert.IsTrue(result.AgreedSelection.Lines.Find(l => l.AssetId == "chair-1").Included);
            Assert.IsFalse(result.AgreedSelection.Lines.Find(l => l.AssetId == "bed-1").Included);
            Assert.IsFalse(result.AgreedSelection.Lines.Find(l => l.AssetId == "table-1").Included);
            Assert.IsTrue(result.Rounds.Exists(r => r.Contains("bed-1") && r.Contains("does not fit")),
                "the skipped bed must be recorded, not silently dropped.");
        }

        [Test]
        public void Negotiation_BuyerCannotAffordFixtures_WalksAwayBeforeBidding()
        {
            var f = MakeDealFixture();
            HouseholdFurnishingRegister register = MakeFurnishedSellerRegisterTracked(
                out Dictionary<string, BuildingEquipmentPlacement> placements, out BuildingFootprintAuthority building);
            var seller = new TestContentsSellerPolicy();
            NpcContentsNegotiationResult result = NegotiateContents(f, seller, register,
                BuyerRequest(new List<string>(), 205000));
            Assert.IsFalse(result.Agreed);
            Assert.IsTrue(result.FailureReason.Contains("214000"), result.FailureReason);
            Assert.AreEqual(0, result.Rounds.Count);
        }

        [Test]
        public void Negotiation_SellerCountersWithReducedSelection_BuyerAcceptsWithinCeiling()
        {
            var f = MakeDealFixture();
            HouseholdFurnishingRegister register = MakeFurnishedSellerRegisterTracked(
                out Dictionary<string, BuildingEquipmentPlacement> placements, out BuildingFootprintAuthority building);
            List<FurnishingContentsItem> items = PropertyContentsSelection.ListEligibleContents(register, BuildingInstanceId);
            PropertyContentsSelection noMovable = PropertyContentsSelection.BuildNoMovable(items, "household",
                SellerHouseholdId.ToString(), BuildingInstanceId, Day, PriceForKind);
            // The buyer opens at 215000 for everything; the seller counters
            // fixtures-only at the same 215000. The buyer accepts: 215000 is
            // inside their capital, and the contents premium over the
            // structure (15000c) stays within replacement (14000c) plus
            // moving (5000c).
            var seller = new TestContentsSellerPolicy
            {
                ReservationBps = 10000,
                CounterPriceCents = 215000,
                CounterSelection = noMovable,
            };
            NpcContentsNegotiationResult result = NegotiateContents(f, seller, register,
                BuyerRequest(new List<string>(), 300000));
            Assert.IsTrue(result.Agreed, result.FailureReason);
            Assert.AreEqual(215000, result.AgreedPriceCents);
            Assert.AreEqual(ContentsSelectionMode.NoMovable, result.AgreedSelection.Mode);
            Assert.AreEqual(14000, result.AgreedSelection.TotalContentsValueCents);
            Assert.AreEqual(201000, result.ImpliedStructureValueCents);
            Assert.IsNull(PropertyContentsSelection.CheckConservation(201000, 14000, 215000));
        }

        [Test]
        public void Negotiation_SellerCountersAboveReplacementPlusMoving_BuyerRefuses()
        {
            var f = MakeDealFixture();
            HouseholdFurnishingRegister register = MakeFurnishedSellerRegisterTracked(
                out Dictionary<string, BuildingEquipmentPlacement> placements, out BuildingFootprintAuthority building);
            // The buyer opens at 215000 for everything; the seller counters
            // at 245000. The contents premium over the structure would be
            // 45000c, above replacement (35000c) plus moving (5000c) — the
            // buyer would rather bring their own furniture. They raise to
            // the anchor and the seller walks.
            var seller = new TestContentsSellerPolicy
            {
                ReservationBps = 10000,
                CounterPriceCents = 245000,
                WalkAfterCounter = true,
            };
            NpcContentsNegotiationResult result = NegotiateContents(f, seller, register,
                BuyerRequest(new List<string>(), 300000));
            Assert.IsFalse(result.Agreed);
            Assert.IsTrue(result.FailureReason.Contains("walked away"), result.FailureReason);
            Assert.IsTrue(result.Rounds.Exists(r => r.Contains("raises to 235000c")),
                "the buyer must raise to the anchor before the seller walks.");
        }

        [Test]
        public void Negotiation_SellerCountersBelowContentsValue_FailsLoudly()
        {
            var f = MakeDealFixture();
            HouseholdFurnishingRegister register = MakeFurnishedSellerRegisterTracked(
                out Dictionary<string, BuildingEquipmentPlacement> placements, out BuildingFootprintAuthority building);
            // A counter at 30000c for contents worth 35000c would imply a
            // negative structure value: not a price, a loud failure.
            var seller = new TestContentsSellerPolicy { ReservationBps = 10000, CounterPriceCents = 30000 };
            NpcContentsNegotiationResult result = NegotiateContents(f, seller, register,
                BuyerRequest(new List<string>(), 300000));
            Assert.IsFalse(result.Agreed);
            Assert.IsTrue(result.FailureReason.Contains("below the contents value"), result.FailureReason);
        }

        [Test]
        public void Negotiation_SellerCountersWithWrongBuildingSelection_FailsLoudly()
        {
            var f = MakeDealFixture();
            HouseholdFurnishingRegister register = MakeFurnishedSellerRegisterTracked(
                out Dictionary<string, BuildingEquipmentPlacement> placements, out BuildingFootprintAuthority building);
            List<FurnishingContentsItem> items = PropertyContentsSelection.ListEligibleContents(register, BuildingInstanceId);
            PropertyContentsSelection wrongBuilding = PropertyContentsSelection.BuildAll(items, "household",
                SellerHouseholdId.ToString(), 999, Day, PriceForKind);
            var seller = new TestContentsSellerPolicy
            {
                ReservationBps = 10000,
                CounterPriceCents = 235000,
                CounterSelection = wrongBuilding,
            };
            NpcContentsNegotiationResult result = NegotiateContents(f, seller, register,
                BuyerRequest(new List<string>(), 300000));
            Assert.IsFalse(result.Agreed);
            Assert.IsTrue(result.FailureReason.Contains("building 999"), result.FailureReason);
        }

        [Test]
        public void Negotiation_UnpricedStove_ExcludedHeldInPlace()
        {
            var f = MakeDealFixture();
            HouseholdFurnishingRegister register = MakeFurnishedSellerRegisterTracked(
                out Dictionary<string, BuildingEquipmentPlacement> placements, out BuildingFootprintAuthority building);
            // F6-realistic pricing: the stove has no replacement cost, so
            // it is excluded (HeldInPlace) instead of breaking the deal.
            // Shelf + all movables included: contents 23000, asking 223000,
            // opening 23000 + 180000 = 203000.
            Func<string, int> noStovePrice = kind =>
                kind == "furniture.cooking-stove" ? 0 : PriceForKind(kind);
            var seller = new TestContentsSellerPolicy { ReservationBps = 9000 };
            NpcContentsNegotiationResult result = NegotiateContents(f, seller, register,
                BuyerRequest(new List<string>(), 300000), noStovePrice);
            Assert.IsTrue(result.Agreed, result.FailureReason);
            Assert.AreEqual(23000, result.AgreedSelection.TotalContentsValueCents);
            Assert.AreEqual(203000, result.AgreedPriceCents);
            Assert.AreEqual(180000, result.ImpliedStructureValueCents);
            Assert.AreEqual(ContentsSelectionMode.Custom, result.AgreedSelection.Mode);
            Assert.AreEqual(0, result.AgreedSelection.UnpricedIncludedAssetIds.Count);
            FurnishingContentsLine stove = result.AgreedSelection.Lines.Find(l => l.AssetId == "stove-1");
            Assert.NotNull(stove);
            Assert.IsFalse(stove.Included);
            Assert.AreEqual(ExcludedItemDisposition.HeldInPlace, stove.Disposition);
            Assert.IsTrue(result.Rounds.Exists(r => r.Contains("stove-1") && r.Contains("no replacement cost")),
                "the stove exclusion must be recorded, not silent.");
        }

        [Test]
        public void Negotiation_WreckedBed_ExcludedAsSalvageCandidate()
        {
            var f = MakeDealFixture();
            // The bed is wrecked (condition 0): B6 records it unpriced, so
            // the buyer excludes it as a salvage candidate, not a contents
            // item. Table + chair + fixtures included: contents 25000,
            // asking 225000, opening 25000 + 180000 = 205000.
            HouseholdFurnishingRegister register = MakeRegister();
            Assert.IsNull(register.TryRegisterAsset(MakeAsset("bed-1", "furniture.bed", 0f)));
            Assert.IsNull(register.TryRegisterAsset(MakeAsset("table-1", "furniture.table")));
            Assert.IsNull(register.TryRegisterAsset(MakeAsset("chair-1", "furniture.chair")));
            Assert.IsNull(register.TryRegisterAsset(MakeAsset("stove-1", "furniture.cooking-stove")));
            Assert.IsNull(register.TryRegisterAsset(MakeAsset("shelf-1", "furniture.shelf")));
            BuildingFootprintAuthority building = MakeBuilding("seller-house");
            InstallAsset(register, building, "bed-1");
            InstallAsset(register, building, "table-1");
            InstallAsset(register, building, "chair-1");
            InstallAsset(register, building, "stove-1");
            InstallAsset(register, building, "shelf-1");
            var seller = new TestContentsSellerPolicy { ReservationBps = 9000 };
            NpcContentsNegotiationResult result = NegotiateContents(f, seller, register,
                BuyerRequest(new List<string>(), 300000));
            Assert.IsTrue(result.Agreed, result.FailureReason);
            Assert.AreEqual(25000, result.AgreedSelection.TotalContentsValueCents);
            Assert.AreEqual(205000, result.AgreedPriceCents);
            Assert.AreEqual(180000, result.ImpliedStructureValueCents);
            FurnishingContentsLine bed = result.AgreedSelection.Lines.Find(l => l.AssetId == "bed-1");
            Assert.NotNull(bed);
            Assert.IsFalse(bed.Included);
            Assert.AreEqual(ExcludedItemDisposition.RemovedBySeller, bed.Disposition);
            Assert.IsTrue(result.Rounds.Exists(r => r.Contains("bed-1") && r.Contains("salvage")),
                "the wrecked bed must be recorded as a salvage candidate.");
        }

        [Test]
        public void Negotiation_NoContents_BareSaleAtStructureValue()
        {
            var f = MakeDealFixture();
            HouseholdFurnishingRegister register = MakeRegister(SellerHouseholdId);
            var seller = new TestContentsSellerPolicy();
            NpcContentsNegotiationResult result = NegotiateContents(f, seller, register,
                BuyerRequest(new List<string>(), 300000));
            Assert.IsTrue(result.Agreed);
            Assert.AreEqual(StructureValueCents, result.AgreedPriceCents);
            Assert.IsNull(result.AgreedSelection);
            Assert.AreEqual(StructureValueCents, result.ImpliedStructureValueCents);
        }

        [Test]
        public void Negotiation_UnclassifiableItem_FailsLoudly()
        {
            var f = MakeDealFixture();
            HouseholdFurnishingRegister register = MakeFurnishedSellerRegisterTracked(
                out Dictionary<string, BuildingEquipmentPlacement> placements, out BuildingFootprintAuthority building);
            Assert.IsNull(register.TryRegisterAsset(MakeAsset("whatsit-1", "furniture.whatsit")));
            InstallAsset(register, building, "whatsit-1");
            var seller = new TestContentsSellerPolicy();
            NpcContentsNegotiationResult result = NegotiateContents(f, seller, register,
                BuyerRequest(new List<string>(), 300000));
            Assert.IsFalse(result.Agreed);
            Assert.IsTrue(result.FailureReason.Contains("whatsit-1"), result.FailureReason);
        }

        [Test]
        public void Negotiation_FirmSeller_BuyerRaisesToAnchor()
        {
            var f = MakeDealFixture();
            HouseholdFurnishingRegister register = MakeFurnishedSellerRegisterTracked(
                out Dictionary<string, BuildingEquipmentPlacement> placements, out BuildingFootprintAuthority building);
            // The seller counters above the anchor at 245000; the buyer
            // refuses (above replacement + moving) and raises to the anchor
            // 235000, which the seller accepts: agreed at the anchor, with
            // the implied structure exactly the structure-only value.
            var seller = new TestContentsSellerPolicy { ReservationBps = 10000, CounterPriceCents = 245000 };
            NpcContentsNegotiationResult result = NegotiateContents(f, seller, register,
                BuyerRequest(new List<string>(), 300000));
            Assert.IsTrue(result.Agreed, result.FailureReason);
            Assert.AreEqual(235000, result.AgreedPriceCents);
            Assert.AreEqual(35000, result.AgreedSelection.TotalContentsValueCents);
            Assert.AreEqual(200000, result.ImpliedStructureValueCents);
            Assert.AreEqual(ContentsSelectionMode.All, result.AgreedSelection.Mode);
            Assert.IsTrue(result.Rounds.Exists(r => r.Contains("raises to 235000c")),
                "the buyer must raise to the anchor before the seller accepts.");
        }

        [Test]
        public void Negotiation_EndToEnd_NegotiatedPlan_ValidatesAndCloses()
        {
            var f = MakeDealFixture();
            string spaceId = f.RegisterFarmstead("parcel-farm", "household:" + SellerHouseholdId);
            HouseholdFurnishingRegister register = MakeFurnishedSellerRegisterTracked(
                out Dictionary<string, BuildingEquipmentPlacement> placements, out BuildingFootprintAuthority building);
            var seller = new TestContentsSellerPolicy { ReservationBps = 9000 };
            NpcContentsNegotiationResult negotiated = NegotiateContents(f, seller, register,
                BuyerRequest(new List<string> { "furniture.bed", "furniture.table" }, 300000));
            Assert.IsTrue(negotiated.Agreed, negotiated.FailureReason);
            // Bed + table + fixtures: contents 32000, agreed 212000,
            // implied structure 180000 — the plan is built from the
            // negotiation result, not from the anchor.
            NpcAcquisitionPlan plan = f.BasePlan("parcel-farm", spaceId, negotiated.AgreedPriceCents);
            plan.Contents = negotiated.AgreedSelection;
            Assert.IsNull(f.Acquisition.ValidatePlan(plan, f.Titles, f.Negotiations, f.Cash, f.Diag, register, negotiated.ImpliedStructureValueCents));
            HouseholdFurnishingRegister buyerRegister = MakeRegister(BuyerHouseholdId);
            var resolver = new FakePlacementResolver(placements, building);
            // The close re-checks conservation against the same structure
            // value the plan was validated with: the implied structure
            // (180000c), not the F6 anchor (200000c).
            NpcAcquisitionResult result = CloseContentsDeal(f, plan, register, buyerRegister, resolver, negotiated.ImpliedStructureValueCents);
            Assert.IsTrue(result.Closed, result.FailureReason);
            Assert.AreEqual("household:" + BuyerHouseholdId, f.Titles.CurrentHolder("parcel-farm"));
            Assert.AreEqual(300000 - 212000, f.Cash.FindStore("household:" + BuyerHouseholdId).ReadBalanceCents());
            Assert.AreEqual(5000 + 212000, f.Cash.FindStore("household:" + SellerHouseholdId).ReadBalanceCents());
            Assert.IsTrue(result.CashConserved);
            // Bed, table, stove, shelf transferred; the chair the buyer
            // did not want was removed by the seller.
            Assert.AreEqual(4, result.TransferredContentsAssetIds.Count);
            Assert.AreEqual(1, result.RemovedContentsAssetIds.Count);
            Assert.IsTrue(result.RemovedContentsAssetIds.Contains("chair-1"));
            Assert.AreEqual(0, result.HeldInPlaceContentsAssetIds.Count);
            Assert.AreEqual(4, buyerRegister.Assets.Count);
            Assert.AreEqual(0, register.Validate().Count);
            Assert.AreEqual(0, buyerRegister.Validate().Count);
        }

        [Test]
        public void Negotiation_FailedNegotiation_LeavesCashAndTitleUntouched()
        {
            var f = MakeDealFixture();
            f.RegisterFarmstead("parcel-farm", "household:" + SellerHouseholdId);
            HouseholdFurnishingRegister register = MakeFurnishedSellerRegisterTracked(
                out Dictionary<string, BuildingEquipmentPlacement> placements, out BuildingFootprintAuthority building);
            var seller = new TestContentsSellerPolicy { AlwaysWalk = true };
            NpcContentsNegotiationResult result = NegotiateContents(f, seller, register,
                BuyerRequest(new List<string>(), 300000));
            Assert.IsFalse(result.Agreed);
            // The negotiation is pure computation: no plan was built, no
            // cash moved, no title moved.
            Assert.AreEqual(300000, f.Cash.FindStore("household:" + BuyerHouseholdId).ReadBalanceCents());
            Assert.AreEqual(5000, f.Cash.FindStore("household:" + SellerHouseholdId).ReadBalanceCents());
            Assert.AreEqual("household:" + SellerHouseholdId, f.Titles.CurrentHolder("parcel-farm"));
        }

        // ---- D-9: player selection API ----

        private const string PlayerHouseholdId = "household-9";
        private const string HouseLocationId = "house-101";
        private const string FarmLocationId = "farmhouse";
        private const int AuthorizedAgentPersonId = 7;

        /// <summary>D-9: fake presence driver — models the seam, never the real director.</summary>
        private sealed class FakePresenceDriver : IPlayerFurnishingPresenceDriver
        {
            public EntityId PlayerPersonId { get; set; } = EntityId.For(EntityKind.Person, 1);
            public string CurrentLocationId { get; set; } = HouseLocationId;
            public bool IsTravelling { get; set; }
            public string TravelDestinationLocationId { get; set; }
            public bool IsOffMap { get; set; }
            public string ForcedCurrentTaskDefinitionId { get; set; }

            public string CurrentTaskDefinitionId => ForcedCurrentTaskDefinitionId;

            public Dictionary<string, int> Routes = new Dictionary<string, int>();
            public int TravelTotalMinutes { get; private set; }
            public int TravelElapsedMinutes { get; private set; }
            public int RouteCalls { get; private set; }
            public int TravelCalls { get; private set; }
            public int InterruptCalls { get; private set; }
            public int CancelTravelCalls { get; private set; }
            public bool TravelFails { get; set; }

            public bool TryRouteMinutes(string fromLocationId, string toLocationId, out int minutes, out string reason)
            {
                RouteCalls++;
                int routed;
                if (Routes.TryGetValue(fromLocationId + "|" + toLocationId, out routed))
                {
                    minutes = routed;
                    reason = null;
                    return true;
                }
                minutes = 0;
                reason = "no route from " + fromLocationId + " to " + toLocationId + ".";
                return false;
            }

            public bool TryTravelTo(string destinationLocationId, int minutes, out string reason)
            {
                TravelCalls++;
                if (TravelFails)
                {
                    reason = "move intent refused.";
                    return false;
                }
                IsTravelling = true;
                TravelDestinationLocationId = destinationLocationId;
                TravelTotalMinutes = minutes;
                TravelElapsedMinutes = 0;
                reason = null;
                return true;
            }

            public bool TryInterruptRoutineActivity(out string reason)
            {
                InterruptCalls++;
                if (FurnishingPresencePolicy.IsRoutineTaskDefinitionId(ForcedCurrentTaskDefinitionId))
                {
                    ForcedCurrentTaskDefinitionId = null;
                    reason = null;
                    return true;
                }
                reason = "no routine activity to interrupt.";
                return false;
            }

            public bool TryCancelTravel(out string reason)
            {
                CancelTravelCalls++;
                if (!IsTravelling)
                {
                    reason = "not travelling.";
                    return false;
                }
                IsTravelling = false;
                TravelDestinationLocationId = null;
                reason = null;
                return true;
            }

            /// <summary>Per-tick travel progress: the location changes only when the full minutes are advanced.</summary>
            public void AdvanceTravel(int minutes)
            {
                if (!IsTravelling) return;
                TravelElapsedMinutes += minutes;
                if (TravelElapsedMinutes < TravelTotalMinutes) return;
                IsTravelling = false;
                CurrentLocationId = TravelDestinationLocationId;
                TravelDestinationLocationId = null;
            }
        }

        /// <summary>D-9: fake agent authorization — answers from a set table, never assumes.</summary>
        private sealed class FakeAgentAuthorization : IFurnishingAgentAuthorization
        {
            public Dictionary<string, HashSet<int>> Authorized = new Dictionary<string, HashSet<int>>();

            public bool IsAuthorizedAgent(string householdId, int agentPersonId)
            {
                HashSet<int> agents;
                return Authorized.TryGetValue(householdId, out agents) && agents.Contains(agentPersonId);
            }
        }

        private static PlayerContentsSelectionApi MakeApi(
            HouseholdFurnishingRegister register, FakePresenceDriver driver,
            Func<string, int> priceFunc = null, IFurnishingAgentAuthorization authorization = null)
        {
            var contract = new PlayerFurnishingPresenceContract(driver, authorization);
            return new PlayerContentsSelectionApi(contract, register, BuildingInstanceId,
                "household", SellerHouseholdId.ToString(), priceFunc ?? PriceForKind,
                Day, PlayerHouseholdId, HouseLocationId);
        }

        private static FakePresenceDriver DriverAtHouse()
        {
            return new FakePresenceDriver { CurrentLocationId = HouseLocationId };
        }

        private static FakePresenceDriver DriverAtFarm(int routeMinutes = 90)
        {
            var driver = new FakePresenceDriver { CurrentLocationId = FarmLocationId };
            driver.Routes[FarmLocationId + "|" + HouseLocationId] = routeMinutes;
            return driver;
        }

        private static int PriceWithoutStove(string kind)
        {
            if (kind == "furniture.cooking-stove") return 0;
            return PriceForKind(kind);
        }

        [Test]
        public void D9_BeginSelection_PlayerAtProperty_ReadyAtOnce()
        {
            HouseholdFurnishingRegister register = MakeFurnishedSellerRegister();
            PlayerContentsSelectionApi api = MakeApi(register, DriverAtHouse());

            DirectedActionVerdict verdict = api.BeginSelection(0,
                out DirectedFurnishingAction action, out string reason);

            Assert.AreEqual(DirectedActionVerdict.Ready, verdict, reason);
            Assert.IsTrue(api.IsActive);
            Assert.NotNull(action);
            Assert.AreEqual(FurnishingActionKind.SelectContents, action.ActionKind);
            Assert.AreEqual(HouseLocationId, action.TargetLocationId);
            Assert.AreEqual(DirectedActionStatus.Ready, action.Status);
            Assert.AreSame(action, api.PendingAction);
        }

        [Test]
        public void D9_BeginSelection_PlayerElsewhere_TravellingThenReady()
        {
            HouseholdFurnishingRegister register = MakeFurnishedSellerRegister();
            var driver = DriverAtFarm();
            PlayerContentsSelectionApi api = MakeApi(register, driver);

            DirectedActionVerdict verdict = api.BeginSelection(0,
                out DirectedFurnishingAction action, out string reason);

            Assert.AreEqual(DirectedActionVerdict.Travelling, verdict, reason);
            Assert.IsFalse(api.IsActive);
            Assert.AreEqual(DirectedActionStatus.Travelling, action.Status);
            Assert.AreEqual(1, driver.TravelCalls);

            // The player has not arrived: listing refuses before presence.
            List<FurnishingContentsItem> listed = api.ListEligible(out string listReason);
            Assert.IsNull(listed);
            Assert.IsTrue(listReason.Contains("not active"), listReason);

            // Travel is real: the session stays inactive until the full
            // minutes are advanced.
            driver.AdvanceTravel(45);
            Assert.IsFalse(api.AdvancePending());
            Assert.IsFalse(api.IsActive);
            driver.AdvanceTravel(45);
            Assert.IsTrue(api.AdvancePending());
            Assert.IsTrue(api.IsActive);

            listed = api.ListEligible(out listReason);
            Assert.NotNull(listed);
            Assert.AreEqual(5, listed.Count);
        }

        [Test]
        public void D9_BeginSelection_NoRoute_RefusedNeverTeleports()
        {
            HouseholdFurnishingRegister register = MakeFurnishedSellerRegister();
            var driver = new FakePresenceDriver { CurrentLocationId = FarmLocationId };
            PlayerContentsSelectionApi api = MakeApi(register, driver);

            DirectedActionVerdict verdict = api.BeginSelection(0,
                out DirectedFurnishingAction action, out string reason);

            Assert.AreEqual(DirectedActionVerdict.Refused, verdict, reason);
            Assert.IsTrue(reason.Contains("no route"), reason);
            Assert.IsNull(action);
            Assert.IsNull(api.PendingAction);
            Assert.IsFalse(api.IsActive);
            Assert.AreEqual(0, driver.TravelCalls);
        }

        [Test]
        public void D9_BeginSelection_OffMap_DefersAndRetries()
        {
            HouseholdFurnishingRegister register = MakeFurnishedSellerRegister();
            var driver = DriverAtFarm();
            driver.IsOffMap = true;
            PlayerContentsSelectionApi api = MakeApi(register, driver);

            DirectedActionVerdict verdict = api.BeginSelection(0,
                out DirectedFurnishingAction action, out string reason);

            Assert.AreEqual(DirectedActionVerdict.Deferred, verdict, reason);
            Assert.IsFalse(api.IsActive);
            Assert.AreEqual(DirectedActionStatus.Deferred, action.Status);
            Assert.AreEqual(0, driver.TravelCalls);

            List<FurnishingContentsItem> listed = api.ListEligible(out string listReason);
            Assert.IsNull(listed);
            Assert.IsTrue(listReason.Contains("not active"), listReason);

            // Still off-map: the retry stays deferred, never executes remotely.
            DirectedActionVerdict retry = api.RetryDeferred(out string retryReason);
            Assert.AreEqual(DirectedActionVerdict.Deferred, retry, retryReason);
            Assert.IsFalse(api.IsActive);

            driver.IsOffMap = false;
            driver.CurrentLocationId = HouseLocationId;
            retry = api.RetryDeferred(out retryReason);
            Assert.AreEqual(DirectedActionVerdict.Ready, retry, retryReason);
            Assert.IsTrue(api.IsActive);
        }

        [Test]
        public void D9_BeginSelection_BusyNonRoutine_Refused()
        {
            HouseholdFurnishingRegister register = MakeFurnishedSellerRegister();
            var driver = DriverAtHouse();
            driver.ForcedCurrentTaskDefinitionId = "test.work";
            PlayerContentsSelectionApi api = MakeApi(register, driver);

            DirectedActionVerdict verdict = api.BeginSelection(0,
                out DirectedFurnishingAction action, out string reason);

            Assert.AreEqual(DirectedActionVerdict.Refused, verdict, reason);
            Assert.IsTrue(reason.Contains("cancel it first"), reason);
            Assert.IsNull(action);
            Assert.IsNull(api.PendingAction);
            Assert.IsFalse(api.IsActive);
        }

        [Test]
        public void D9_BeginSelection_RoutineTask_InterruptsAndReady()
        {
            HouseholdFurnishingRegister register = MakeFurnishedSellerRegister();
            var driver = DriverAtHouse();
            driver.ForcedCurrentTaskDefinitionId = Pass2TaskCatalogs.EatMealId;
            PlayerContentsSelectionApi api = MakeApi(register, driver);

            DirectedActionVerdict verdict = api.BeginSelection(0,
                out DirectedFurnishingAction action, out string reason);

            Assert.AreEqual(DirectedActionVerdict.Ready, verdict, reason);
            Assert.IsTrue(api.IsActive);
            Assert.AreEqual(1, driver.InterruptCalls);
            Assert.IsNull(driver.ForcedCurrentTaskDefinitionId);
        }

        [Test]
        public void D9_BeginSelection_AuthorizedAgent_ReadyWithoutTravel()
        {
            HouseholdFurnishingRegister register = MakeFurnishedSellerRegister();
            var driver = DriverAtFarm();
            var authorization = new FakeAgentAuthorization();
            authorization.Authorized[PlayerHouseholdId] = new HashSet<int> { AuthorizedAgentPersonId };
            PlayerContentsSelectionApi api = MakeApi(register, driver, null, authorization);

            DirectedActionVerdict verdict = api.BeginSelection(AuthorizedAgentPersonId,
                out DirectedFurnishingAction action, out string reason);

            Assert.AreEqual(DirectedActionVerdict.Ready, verdict, reason);
            Assert.IsTrue(api.IsActive);
            Assert.IsTrue(action.ViaAuthorizedAgent);
            Assert.AreEqual(0, driver.TravelCalls);
        }

        [Test]
        public void D9_BeginSelection_UnauthorizedAgent_Refused()
        {
            HouseholdFurnishingRegister register = MakeFurnishedSellerRegister();
            var driver = DriverAtFarm();
            var authorization = new FakeAgentAuthorization();
            PlayerContentsSelectionApi api = MakeApi(register, driver, null, authorization);

            DirectedActionVerdict verdict = api.BeginSelection(AuthorizedAgentPersonId,
                out DirectedFurnishingAction action, out string reason);

            Assert.AreEqual(DirectedActionVerdict.Refused, verdict, reason);
            Assert.IsTrue(reason.Contains("not authorized"), reason);
            Assert.IsNull(action);

            // An agent id with no authorization source bound is never
            // assumed authorized.
            PlayerContentsSelectionApi noSource = MakeApi(register, driver);
            verdict = noSource.BeginSelection(AuthorizedAgentPersonId,
                out action, out reason);
            Assert.AreEqual(DirectedActionVerdict.Refused, verdict, reason);
            Assert.IsTrue(reason.Contains("no authorized-agent source"), reason);
        }

        [Test]
        public void D9_BeginSelection_SecondBeginWhileOpen_Refused()
        {
            HouseholdFurnishingRegister register = MakeFurnishedSellerRegister();
            PlayerContentsSelectionApi api = MakeApi(register, DriverAtHouse());
            Assert.AreEqual(DirectedActionVerdict.Ready,
                api.BeginSelection(0, out _, out _));

            DirectedActionVerdict verdict = api.BeginSelection(0,
                out DirectedFurnishingAction action, out string reason);

            Assert.AreEqual(DirectedActionVerdict.Refused, verdict, reason);
            Assert.IsTrue(reason.Contains("already open"), reason);
            Assert.IsNull(action);
        }

        [Test]
        public void D9_ListEligible_InstalledItemsOnly()
        {
            HouseholdFurnishingRegister register = MakeFurnishedSellerRegister();
            Assert.IsNull(register.TryRegisterAsset(MakeAsset("crate-1", "furniture.crate")));
            PlayerContentsSelectionApi api = MakeApi(register, DriverAtHouse());
            Assert.AreEqual(DirectedActionVerdict.Ready,
                api.BeginSelection(0, out _, out _));

            List<FurnishingContentsItem> listed = api.ListEligible(out string reason);

            Assert.NotNull(listed, reason);
            Assert.AreEqual(5, listed.Count);
            // The crate sits in household custody, not installed in the
            // building: it is not part of the property and never listed.
            foreach (FurnishingContentsItem item in listed)
                Assert.AreNotEqual("crate-1", item.AssetId);
            FurnishingContentsItem bed = listed.Find(i => i.AssetId == "bed-1");
            Assert.NotNull(bed);
            Assert.AreEqual(FurnishingFixtureKind.Movable, bed.Kind);
            FurnishingContentsItem stove = listed.Find(i => i.AssetId == "stove-1");
            Assert.NotNull(stove);
            Assert.AreEqual(FurnishingFixtureKind.Fixture, stove.Kind);
            FurnishingContentsItem shelf = listed.Find(i => i.AssetId == "shelf-1");
            Assert.NotNull(shelf);
            Assert.AreEqual(FurnishingFixtureKind.Fixture, shelf.Kind);
        }

        [Test]
        public void D9_ListEligible_BeforeBegin_Refused()
        {
            HouseholdFurnishingRegister register = MakeFurnishedSellerRegister();
            PlayerContentsSelectionApi api = MakeApi(register, DriverAtHouse());

            List<FurnishingContentsItem> listed = api.ListEligible(out string reason);

            Assert.IsNull(listed);
            Assert.IsTrue(reason.Contains("not active"), reason);
        }

        [Test]
        public void D9_SetMode_All_ValuesExact()
        {
            HouseholdFurnishingRegister register = MakeFurnishedSellerRegister();
            PlayerContentsSelectionApi api = MakeApi(register, DriverAtHouse());
            Assert.AreEqual(DirectedActionVerdict.Ready,
                api.BeginSelection(0, out _, out _));

            Assert.IsTrue(api.SetMode(ContentsSelectionMode.All, out string reason), reason);

            // Bed 10000 + table 8000 + chair 3000 + stove 12000 + shelf 2000 = 35000.
            Assert.AreEqual(35000, api.TotalContentsValueCents);
            Assert.AreEqual(14000, api.IncludedFixtureValueCents);
            Assert.AreEqual(21000, api.IncludedMovableValueCents);
            Assert.AreEqual(ContentsSelectionMode.All, api.DraftSelection.Mode);
            Assert.AreEqual(5, api.DraftSelection.Lines.Count);
            Assert.IsTrue(api.ValidateSelection(out List<string> problems),
                string.Join("; ", problems));
        }

        [Test]
        public void D9_SetMode_NoMovable_FixturesOnly()
        {
            HouseholdFurnishingRegister register = MakeFurnishedSellerRegister();
            PlayerContentsSelectionApi api = MakeApi(register, DriverAtHouse());
            Assert.AreEqual(DirectedActionVerdict.Ready,
                api.BeginSelection(0, out _, out _));

            Assert.IsTrue(api.SetMode(ContentsSelectionMode.NoMovable, out string reason), reason);

            Assert.AreEqual(14000, api.TotalContentsValueCents);
            Assert.AreEqual(14000, api.IncludedFixtureValueCents);
            Assert.AreEqual(0, api.IncludedMovableValueCents);
            foreach (FurnishingContentsLine line in api.DraftSelection.Lines)
            {
                if (line.Kind == FurnishingFixtureKind.Movable)
                {
                    Assert.IsFalse(line.Included);
                    Assert.AreEqual(ExcludedItemDisposition.RemovedBySeller, line.Disposition);
                }
            }
            Assert.IsTrue(api.ValidateSelection(out List<string> problems),
                string.Join("; ", problems));
        }

        [Test]
        public void D9_ToggleItem_CustomFlow()
        {
            HouseholdFurnishingRegister register = MakeFurnishedSellerRegister();
            PlayerContentsSelectionApi api = MakeApi(register, DriverAtHouse());
            Assert.AreEqual(DirectedActionVerdict.Ready,
                api.BeginSelection(0, out _, out _));

            // Toggling needs a draft first.
            Assert.IsFalse(api.ToggleItem("bed-1", true, out string reason));
            Assert.IsTrue(reason.Contains("SetMode"), reason);

            Assert.IsTrue(api.SetMode(ContentsSelectionMode.Custom, out reason), reason);
            Assert.AreEqual(0, api.TotalContentsValueCents);

            Assert.IsTrue(api.ToggleItem("bed-1", true, out reason), reason);
            Assert.AreEqual(10000, api.TotalContentsValueCents);
            Assert.IsTrue(api.ToggleItem("table-1", true, out reason), reason);
            Assert.AreEqual(18000, api.TotalContentsValueCents);
            Assert.IsTrue(api.ToggleItem("bed-1", false, out reason), reason);
            Assert.AreEqual(8000, api.TotalContentsValueCents);
            // Toggling an item already in the requested state is a no-op success.
            Assert.IsTrue(api.ToggleItem("bed-1", false, out reason), reason);
            Assert.AreEqual(8000, api.TotalContentsValueCents);

            Assert.IsFalse(api.ToggleItem("not-an-item", true, out reason));
            Assert.IsTrue(reason.Contains("not an eligible item"), reason);
            Assert.IsFalse(api.ToggleItem("crate-1", true, out reason));
            Assert.IsTrue(reason.Contains("not an eligible item"), reason);
        }

        [Test]
        public void D9_ToggleItem_NonCustomMode_Refused()
        {
            HouseholdFurnishingRegister register = MakeFurnishedSellerRegister();
            PlayerContentsSelectionApi api = MakeApi(register, DriverAtHouse());
            Assert.AreEqual(DirectedActionVerdict.Ready,
                api.BeginSelection(0, out _, out _));
            Assert.IsTrue(api.SetMode(ContentsSelectionMode.All, out string reason), reason);

            Assert.IsFalse(api.ToggleItem("bed-1", false, out reason));
            Assert.IsTrue(reason.Contains("switch to Custom"), reason);
            Assert.AreEqual(35000, api.TotalContentsValueCents);
        }

        [Test]
        public void D9_SetMode_AllThenCustom_PreservesIncludedSet()
        {
            HouseholdFurnishingRegister register = MakeFurnishedSellerRegister();
            PlayerContentsSelectionApi api = MakeApi(register, DriverAtHouse());
            Assert.AreEqual(DirectedActionVerdict.Ready,
                api.BeginSelection(0, out _, out _));
            Assert.IsTrue(api.SetMode(ContentsSelectionMode.All, out string reason), reason);

            // All-then-Custom keeps every item included, so the player
            // can remove a few instead of re-adding them all.
            Assert.IsTrue(api.SetMode(ContentsSelectionMode.Custom, out reason), reason);
            Assert.AreEqual(35000, api.TotalContentsValueCents);
            Assert.IsTrue(api.ToggleItem("chair-1", false, out reason), reason);
            Assert.AreEqual(32000, api.TotalContentsValueCents);
        }

        [Test]
        public void D9_ValidateSelection_UnpricedIncluded_Refused()
        {
            HouseholdFurnishingRegister register = MakeFurnishedSellerRegister();
            PlayerContentsSelectionApi api = MakeApi(register, DriverAtHouse(), PriceWithoutStove);
            Assert.AreEqual(DirectedActionVerdict.Ready,
                api.BeginSelection(0, out _, out _));
            Assert.IsTrue(api.SetMode(ContentsSelectionMode.All, out string reason), reason);
            Assert.IsTrue(api.DraftSelection.UnpricedIncludedAssetIds.Contains("stove-1"));

            Assert.IsFalse(api.ValidateSelection(out List<string> problems));
            bool namesStove = false;
            foreach (string problem in problems)
            {
                if (problem.Contains("stove-1") && problem.Contains("no supplied replacement cost")) namesStove = true;
            }
            Assert.IsTrue(namesStove, string.Join("; ", problems));

            // The remedy is choosing: exclude the unpriced stove and the
            // selection validates.
            Assert.IsTrue(api.SetMode(ContentsSelectionMode.Custom, out reason), reason);
            Assert.IsTrue(api.ToggleItem("stove-1", false, out reason), reason);
            Assert.AreEqual(23000, api.TotalContentsValueCents);
            Assert.IsTrue(api.ValidateSelection(out problems), string.Join("; ", problems));
        }

        [Test]
        public void D9_SetMode_BareBuilding_Refused()
        {
            HouseholdFurnishingRegister register = MakeRegister();
            PlayerContentsSelectionApi api = MakeApi(register, DriverAtHouse());
            Assert.AreEqual(DirectedActionVerdict.Ready,
                api.BeginSelection(0, out _, out _));

            Assert.IsFalse(api.SetMode(ContentsSelectionMode.All, out string reason));
            Assert.IsTrue(reason.Contains("bare sale"), reason);
        }

        [Test]
        public void D9_SetMode_UnclassifiableItem_Refused()
        {
            HouseholdFurnishingRegister register = MakeFurnishedSellerRegister();
            BuildingFootprintAuthority building = MakeBuilding("seller-house");
            Assert.IsNull(register.TryRegisterAsset(MakeAsset("mystery-1", "furniture.mystery-widget")));
            InstallAsset(register, building, "mystery-1");
            PlayerContentsSelectionApi api = MakeApi(register, DriverAtHouse());
            Assert.AreEqual(DirectedActionVerdict.Ready,
                api.BeginSelection(0, out _, out _));

            Assert.IsFalse(api.SetMode(ContentsSelectionMode.All, out string reason));
            Assert.IsTrue(reason.Contains("mystery-1"), reason);
            Assert.IsTrue(reason.Contains("could not be classified"), reason);
        }

        [Test]
        public void D9_ImpliedStructure_AndConservation()
        {
            HouseholdFurnishingRegister register = MakeFurnishedSellerRegister();
            PlayerContentsSelectionApi api = MakeApi(register, DriverAtHouse());
            Assert.AreEqual(DirectedActionVerdict.Ready,
                api.BeginSelection(0, out _, out _));
            Assert.IsTrue(api.SetMode(ContentsSelectionMode.All, out string reason), reason);

            // The implied structure value comes from the AGREED price,
            // never the F6 anchor: 235000 agreed minus 35000 contents.
            Assert.IsTrue(api.TryImpliedStructureValueCents(235000,
                out int implied, out reason), reason);
            Assert.AreEqual(200000, implied);
            Assert.IsNull(api.CheckConservation(235000, implied));

            // A different structure value does NOT conserve: validate
            // and close must use the same implied value (D-8 §16).
            string mismatch = api.CheckConservation(235000, 235000);
            Assert.IsNotNull(mismatch);
            Assert.IsTrue(mismatch.Contains("235000"), mismatch);

            // A price that does not cover the contents has no implied
            // structure value at all.
            Assert.IsFalse(api.TryImpliedStructureValueCents(30000, out _, out reason));
            Assert.IsTrue(reason.Contains("does not cover"), reason);
            Assert.IsFalse(api.TryImpliedStructureValueCents(-1, out _, out reason));
            Assert.IsTrue(reason.Contains("negative"), reason);
        }

        [Test]
        public void D9_ConfirmSelection_ExecutorAppliesSelection()
        {
            HouseholdFurnishingRegister register = MakeFurnishedSellerRegister();
            PlayerContentsSelectionApi api = MakeApi(register, DriverAtHouse());
            Assert.AreEqual(DirectedActionVerdict.Ready,
                api.BeginSelection(0, out DirectedFurnishingAction action, out _));
            Assert.IsTrue(api.SetMode(ContentsSelectionMode.All, out string reason), reason);

            PropertyContentsSelection applied = null;
            int executorCalls = 0;
            bool confirmed = api.ConfirmSelection(a =>
            {
                executorCalls++;
                applied = api.DraftSelection;
                return null;
            }, out reason);

            Assert.IsTrue(confirmed, reason);
            Assert.AreEqual(1, executorCalls);
            Assert.AreSame(api.DraftSelection, applied);
            Assert.AreSame(api.DraftSelection, api.ConfirmedSelection);
            Assert.IsTrue(api.IsConfirmed);
            Assert.AreEqual(DirectedActionStatus.Done, action.Status);

            // The session is complete: a new selection opens fresh.
            DirectedActionVerdict second = api.BeginSelection(0,
                out DirectedFurnishingAction secondAction, out reason);
            Assert.AreEqual(DirectedActionVerdict.Ready, second, reason);
            Assert.AreNotEqual(action.ActionId, secondAction.ActionId);
        }

        [Test]
        public void D9_ConfirmSelection_FailingExecutor_NothingConfirmed()
        {
            HouseholdFurnishingRegister register = MakeFurnishedSellerRegister();
            PlayerContentsSelectionApi api = MakeApi(register, DriverAtHouse());
            Assert.AreEqual(DirectedActionVerdict.Ready,
                api.BeginSelection(0, out DirectedFurnishingAction action, out _));
            Assert.IsTrue(api.SetMode(ContentsSelectionMode.All, out string reason), reason);

            bool confirmed = api.ConfirmSelection(a => "the deal is gone.", out reason);

            Assert.IsFalse(confirmed);
            Assert.AreEqual("the deal is gone.", reason);
            Assert.IsNull(api.ConfirmedSelection);
            Assert.IsFalse(api.IsConfirmed);
            Assert.AreEqual(DirectedActionStatus.Failed, action.Status);
            Assert.AreEqual("the deal is gone.", action.RefusalReason);
        }

        [Test]
        public void D9_ConfirmSelection_NotReady_ExecutorNotCalled()
        {
            HouseholdFurnishingRegister register = MakeFurnishedSellerRegister();
            var driver = DriverAtFarm();
            PlayerContentsSelectionApi api = MakeApi(register, driver);
            Assert.AreEqual(DirectedActionVerdict.Travelling,
                api.BeginSelection(0, out _, out _));
            Assert.IsFalse(api.IsActive);

            int executorCalls = 0;
            bool confirmed = api.ConfirmSelection(a =>
            {
                executorCalls++;
                return null;
            }, out string reason);

            Assert.IsFalse(confirmed);
            Assert.AreEqual(0, executorCalls);
            Assert.IsTrue(reason.Contains("not active"), reason);
        }

        [Test]
        public void D9_CancelPending_ClearsSession()
        {
            HouseholdFurnishingRegister register = MakeFurnishedSellerRegister();
            var driver = DriverAtFarm();
            PlayerContentsSelectionApi api = MakeApi(register, driver);
            Assert.AreEqual(DirectedActionVerdict.Travelling,
                api.BeginSelection(0, out _, out _));
            Assert.AreEqual(1, driver.TravelCalls);

            Assert.IsTrue(api.CancelPending(out string reason), reason);
            Assert.IsNull(api.PendingAction);
            Assert.IsFalse(api.IsActive);
            Assert.IsNull(api.DraftSelection);
            Assert.AreEqual(1, driver.CancelTravelCalls);

            // A new session opens fresh — the cancelled trip is not resumed.
            DirectedActionVerdict verdict = api.BeginSelection(0,
                out DirectedFurnishingAction action, out reason);
            Assert.AreEqual(DirectedActionVerdict.Travelling, verdict, reason);
            Assert.AreEqual(2, driver.TravelCalls);
            Assert.AreNotEqual(null, action);
        }

        [Test]
        public void D9_ConfirmedSelection_ClosesThroughD6D7_CashAndTitleConserved()
        {
            var f = MakeDealFixture();
            string spaceId = f.RegisterFarmstead("parcel-farm", "household:" + SellerHouseholdId);
            HouseholdFurnishingRegister register = MakeFurnishedSellerRegisterTracked(
                out Dictionary<string, BuildingEquipmentPlacement> placements, out BuildingFootprintAuthority building);
            PlayerContentsSelectionApi api = MakeApi(register, DriverAtHouse());
            Assert.AreEqual(DirectedActionVerdict.Ready,
                api.BeginSelection(0, out _, out _));
            Assert.IsTrue(api.SetMode(ContentsSelectionMode.Custom, out string reason), reason);
            Assert.IsTrue(api.ToggleItem("bed-1", true, out reason), reason);
            Assert.IsTrue(api.ToggleItem("table-1", true, out reason), reason);
            Assert.AreEqual(18000, api.TotalContentsValueCents);
            Assert.IsTrue(api.ValidateSelection(out List<string> problems),
                string.Join("; ", problems));

            // The agreed price is the caller's input; the implied
            // structure value is derived from it (D-8 §16).
            int agreed = 218000;
            Assert.IsTrue(api.TryImpliedStructureValueCents(agreed,
                out int implied, out reason), reason);
            Assert.AreEqual(200000, implied);
            Assert.IsNull(api.CheckConservation(agreed, implied));

            PropertyContentsSelection applied = null;
            Assert.IsTrue(api.ConfirmSelection(a =>
            {
                applied = api.DraftSelection;
                return null;
            }, out reason), reason);
            Assert.AreSame(api.ConfirmedSelection, applied);

            NpcAcquisitionPlan plan = f.BasePlan("parcel-farm", spaceId, agreed);
            plan.Contents = applied;
            Assert.IsNull(f.Acquisition.ValidatePlan(plan, f.Titles, f.Negotiations, f.Cash, f.Diag, register, implied));

            HouseholdFurnishingRegister buyerRegister = MakeRegister(BuyerHouseholdId);
            var resolver = new FakePlacementResolver(placements, building);
            NpcAcquisitionResult result = CloseContentsDeal(f, plan, register, buyerRegister, resolver, implied);

            Assert.IsTrue(result.Closed, result.FailureReason);
            Assert.AreEqual("household:" + BuyerHouseholdId, f.Titles.CurrentHolder("parcel-farm"));
            Assert.AreEqual(300000 - agreed, f.Cash.FindStore("household:" + BuyerHouseholdId).ReadBalanceCents());
            Assert.AreEqual(5000 + agreed, f.Cash.FindStore("household:" + SellerHouseholdId).ReadBalanceCents());
            Assert.IsTrue(result.CashConserved);
            // Bed and table moved; the chair was removed by the seller;
            // the stove and shelf stay held in place as the seller's.
            Assert.AreEqual(2, result.TransferredContentsAssetIds.Count);
            Assert.IsTrue(result.TransferredContentsAssetIds.Contains("bed-1"));
            Assert.IsTrue(result.TransferredContentsAssetIds.Contains("table-1"));
            Assert.AreEqual(1, result.RemovedContentsAssetIds.Count);
            Assert.IsTrue(result.RemovedContentsAssetIds.Contains("chair-1"));
            Assert.AreEqual(2, result.HeldInPlaceContentsAssetIds.Count);
            Assert.IsTrue(result.HeldInPlaceContentsAssetIds.Contains("stove-1"));
            Assert.IsTrue(result.HeldInPlaceContentsAssetIds.Contains("shelf-1"));
            Assert.AreEqual(2, buyerRegister.Assets.Count);
            Assert.NotNull(buyerRegister.LiveBindingFor("bed-1"));
            Assert.NotNull(buyerRegister.LiveBindingFor("table-1"));
            Assert.AreEqual(0, register.Validate().Count);
            Assert.AreEqual(0, buyerRegister.Validate().Count);
        }

        [Test]
        public void D9_Constructor_NullInputs_ThrowLoudly()
        {
            HouseholdFurnishingRegister register = MakeFurnishedSellerRegister();
            var driver = DriverAtHouse();
            var contract = new PlayerFurnishingPresenceContract(driver);

            Assert.Throws<ArgumentNullException>(() => new PlayerContentsSelectionApi(
                null, register, BuildingInstanceId, "household", "7", PriceForKind, Day, PlayerHouseholdId, HouseLocationId));
            Assert.Throws<ArgumentNullException>(() => new PlayerContentsSelectionApi(
                contract, null, BuildingInstanceId, "household", "7", PriceForKind, Day, PlayerHouseholdId, HouseLocationId));
            Assert.Throws<ArgumentNullException>(() => new PlayerContentsSelectionApi(
                contract, register, BuildingInstanceId, "household", "7", null, Day, PlayerHouseholdId, HouseLocationId));
            Assert.Throws<ArgumentException>(() => new PlayerContentsSelectionApi(
                contract, register, -1, "household", "7", PriceForKind, Day, PlayerHouseholdId, HouseLocationId));
            Assert.Throws<ArgumentException>(() => new PlayerContentsSelectionApi(
                contract, register, BuildingInstanceId, "household", "", PriceForKind, Day, PlayerHouseholdId, HouseLocationId));
            Assert.Throws<ArgumentException>(() => new PlayerContentsSelectionApi(
                contract, register, BuildingInstanceId, "household", "7", PriceForKind, Day, "", HouseLocationId));
            Assert.Throws<ArgumentException>(() => new PlayerContentsSelectionApi(
                contract, register, BuildingInstanceId, "household", "7", PriceForKind, Day, PlayerHouseholdId, ""));
        }
    }
}
