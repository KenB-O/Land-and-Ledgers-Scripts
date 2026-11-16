using System.Collections.Generic;
using System.Reflection;
using LandLedgers.Economy.Blacksmith;
using LandLedgers.Economy.Financing;
using LandLedgers.Population;
using LandLedgers.Primitives;
using LandLedgers.World;
using LandLedgers.World.Property;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

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

        private static void InstallAsset(
            HouseholdFurnishingRegister register,
            BuildingFootprintAuthority building,
            string assetId,
            int buildingInstanceId = BuildingInstanceId)
        {
            BuildingEquipmentPlacement placement = MakePlacement(assetId, default);
            string error = register.TryInstall(assetId, placement, building, buildingInstanceId, 0,
                FurnishingCapabilityKind.None, string.Empty,
                FurnishingAcquisitionSource.ScenarioEndowment, "endowment-qa-town-1",
                out FurnishingPlacementBinding binding);
            Assert.IsNull(error, error);
            Assert.NotNull(binding);
        }

        /// <summary>The seller's furnished house: bed, table, chair, stove, shelf installed in building 101.</summary>
        private static HouseholdFurnishingRegister MakeFurnishedSellerRegister()
        {
            var register = MakeRegister();
            Assert.IsNull(register.TryRegisterAsset(MakeAsset("bed-1", "furniture.bed")));
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

        [Test]
        public void Contents_All_Validates_AndCloses_CashAndTitleConserved()
        {
            var f = MakeDealFixture();
            string spaceId = f.RegisterFarmstead("parcel-farm", "household:" + SellerHouseholdId);
            HouseholdFurnishingRegister register = MakeFurnishedSellerRegister();
            PropertyContentsSelection contents = BuildAllFor(register);
            // Bed 10000 + table 8000 + chair 3000 + stove 12000 + shelf 2000 = 35000.
            Assert.AreEqual(35000, contents.TotalContentsValueCents);
            int price = StructureValueCents + contents.TotalContentsValueCents;
            NpcAcquisitionPlan plan = f.BasePlan("parcel-farm", spaceId, price);
            plan.Contents = contents;

            Assert.IsNull(f.Acquisition.ValidatePlan(plan, f.Titles, f.Negotiations, f.Cash, f.Diag, register, StructureValueCents));

            NpcAcquisitionResult result = f.Acquisition.CloseAcquisition(
                f.Ids, plan, f.Authority, f.Registry, f.Cash, f.Titles, f.Housing,
                f.Negotiations, f.ClosingService, f.CreditEngine, f.Workflow, Day, f.Events, f.Diag,
                register, StructureValueCents);
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
            HouseholdFurnishingRegister register = MakeFurnishedSellerRegister();
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
            // breach — the close still succeeds.
            register.FindAsset("bed-1").OwnerId = "8";

            NpcAcquisitionResult result = f.Acquisition.CloseAcquisition(
                f.Ids, plan, f.Authority, f.Registry, f.Cash, f.Titles, f.Housing,
                f.Negotiations, f.ClosingService, f.CreditEngine, f.Workflow, Day, f.Events, f.Diag,
                register, StructureValueCents);
            Assert.IsTrue(result.Closed, result.FailureReason);
            bool noted = false;
            foreach (string line in f.Diag)
            {
                if (line.Contains("excluded") && line.Contains("bed-1")) noted = true;
            }
            Assert.IsTrue(noted, "the excluded bed leaving must be reported as a note.");
        }

        [Test]
        public void Contents_NoMovable_FixturesOnly_ConservationHolds()
        {
            var f = MakeDealFixture();
            string spaceId = f.RegisterFarmstead("parcel-farm", "household:" + SellerHouseholdId);
            HouseholdFurnishingRegister register = MakeFurnishedSellerRegister();
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
            NpcAcquisitionResult result = f.Acquisition.CloseAcquisition(
                f.Ids, plan, f.Authority, f.Registry, f.Cash, f.Titles, f.Housing,
                f.Negotiations, f.ClosingService, f.CreditEngine, f.Workflow, Day, f.Events, f.Diag,
                register, StructureValueCents);
            Assert.IsTrue(result.Closed, result.FailureReason);
        }

        [Test]
        public void Contents_Custom_OnlyBed_ConservationHolds()
        {
            var f = MakeDealFixture();
            string spaceId = f.RegisterFarmstead("parcel-farm", "household:" + SellerHouseholdId);
            HouseholdFurnishingRegister register = MakeFurnishedSellerRegister();
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
            NpcAcquisitionResult result = f.Acquisition.CloseAcquisition(
                f.Ids, plan, f.Authority, f.Registry, f.Cash, f.Titles, f.Housing,
                f.Negotiations, f.ClosingService, f.CreditEngine, f.Workflow, Day, f.Events, f.Diag,
                register, StructureValueCents);
            Assert.IsTrue(result.Closed, result.FailureReason);
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
    }
}
