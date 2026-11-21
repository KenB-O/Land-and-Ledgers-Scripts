// Copyright (c) Land & Ledgers contributors. Licensed under the MIT License.
// Lane 10b (I-R1–I-R8, expanded campaign 2026-11-20): furniture
// placement rules — the install path scores placements by the §4.1
// table instead of taking the first match in scene order.
//
// The room geometry is synthetic test data, stated here, not a claim
// about any authored building: a 10 x 10 m room (walls at x/z = +/-5,
// door at (0, 4) just inside the front wall) for the scoring tests,
// and the shared business-interior frame (south wall z = -5, east
// wall x = 6 — the frame the authored store/bakery/dealer/lumberyard
// fixtures all sit in) for the fixture-validation tests. Counter
// dimensions in the counter tests are explicit test inputs (the
// carpentry catalog has no counter kind and no counter dimensions —
// I-R7 — so the test supplies the footprint the rule needs rather
// than pretending the catalog has one).

using System;
using System.Collections.Generic;
using LandLedgers.Economy.Blacksmith;
using LandLedgers.Economy.GeneralStore;
using LandLedgers.Economy.GenericBusiness;
using LandLedgers.Economy.Retail;
using LandLedgers.Economy.Storage;
using LandLedgers.FirstLedger;
using LandLedgers.Population;
using LandLedgers.Primitives;
using LandLedgers.Tasks;
using LandLedgers.Time;
using LandLedgers.World;
using LandLedgers.World.Journeys;
using NUnit.Framework;

namespace LandLedgers.Editor.Economy
{
    [TestFixture]
    public class Lane10bFurniturePlacementTests
    {
        private const int HouseholdId = 7;
        private const int BuildingInstanceId = 12;
        private const int DayIndex = 42;

        // --- shared room geometry (synthetic, stated above) ---

        private static List<FurnitureFloorPoint> RoomPolygon()
        {
            return new List<FurnitureFloorPoint>
            {
                new FurnitureFloorPoint(-5f, -5f),
                new FurnitureFloorPoint(5f, -5f),
                new FurnitureFloorPoint(5f, 5f),
                new FurnitureFloorPoint(-5f, 5f),
            };
        }

        private static List<FurnitureFloorPoint> BusinessFramePolygon()
        {
            return new List<FurnitureFloorPoint>
            {
                new FurnitureFloorPoint(-6f, -5f),
                new FurnitureFloorPoint(6f, -5f),
                new FurnitureFloorPoint(6f, 5f),
                new FurnitureFloorPoint(-6f, 5f),
            };
        }

        private FurniturePlacementContext RoomContext(
            Dictionary<string, FurniturePlacementPose> poses,
            List<FurnitureApproachPoint> approachPoints = null)
        {
            return FurniturePlacementContext.Build(
                RoomPolygon(), hasDoor: true, doorX: 0f, doorZ: 4f,
                approachPoints ?? new List<FurnitureApproachPoint>(), poses);
        }

        private BuildingFootprintAuthority building;
        private List<BuildingEquipmentPlacement> placements;
        private HouseholdFurnishingRegister register;

        [SetUp]
        public void SetUp()
        {
            building = new BuildingFootprintAuthority();
            placements = new List<BuildingEquipmentPlacement>();
            register = new HouseholdFurnishingRegister { HouseholdId = HouseholdId };
        }

        private bool PlacementIsBound(string placementId)
        {
            if (string.IsNullOrWhiteSpace(placementId) || register?.Bindings == null) return false;
            foreach (FurnishingPlacementBinding binding in register.Bindings)
            {
                if (binding != null && string.Equals(binding.BindingId, placementId, StringComparison.Ordinal)) return true;
            }
            return false;
        }

        private BuildingEquipmentPlacement MakePlacement(string placementId, string definitionId)
        {
            var placement = new BuildingEquipmentPlacement();
            placement.AssociateWithBuilding(building);
            placement.Configure(placementId, definitionId, definitionId, 0);
            placements.Add(placement);
            return placement;
        }

        // --- I-R1: scored selection ---

        [Test]
        public void Shelf_PrefersWallSlotOverMidRoomSlot_EvenWhenMidRoomIsFirstInSceneOrder()
        {
            MakePlacement("plc-mid", FurnitureItemSpecs.ShelfId);
            MakePlacement("plc-wall", FurnitureItemSpecs.ShelfId);
            var poses = new Dictionary<string, FurniturePlacementPose>
            {
                { "plc-mid", new FurniturePlacementPose(0f, 0f, 0f) },
                { "plc-wall", new FurniturePlacementPose(0f, -4.85f, 0f) },
            };
            FurniturePlacementContext context = RoomContext(poses);

            bool found = FurnitureInstallBinding.TryFindFreePlacement(
                FurnitureItemSpecs.ShelfId, building, placements, PlacementIsBound,
                context, out BuildingEquipmentPlacement placement, out string refusal);

            Assert.IsTrue(found, "a wall slot exists, so selection must succeed: " + refusal);
            Assert.AreEqual("plc-wall", placement.PlacementId,
                "the shelf takes the wall-adjacent slot, not the mid-room slot that comes first in scene order");
        }

        [Test]
        public void FirstMatch_StillWinsWithoutAContext()
        {
            MakePlacement("plc-mid", FurnitureItemSpecs.ShelfId);
            MakePlacement("plc-wall", FurnitureItemSpecs.ShelfId);

            bool found = FurnitureInstallBinding.TryFindFreePlacement(
                FurnitureItemSpecs.ShelfId, building, placements, PlacementIsBound,
                out BuildingEquipmentPlacement placement, out _);

            Assert.IsTrue(found);
            Assert.AreEqual("plc-mid", placement.PlacementId,
                "without geometry the selection stays first-match, exactly today's behaviour");
        }

        [Test]
        public void Shelf_WithOnlyAMidRoomSlot_IsRefusedAndStaysInCustody()
        {
            MakePlacement("plc-mid", FurnitureItemSpecs.ShelfId);
            var poses = new Dictionary<string, FurniturePlacementPose>
            {
                { "plc-mid", new FurniturePlacementPose(0f, 0f, 0f) },
            };

            bool found = FurnitureInstallBinding.TryFindFreePlacement(
                FurnitureItemSpecs.ShelfId, building, placements, PlacementIsBound,
                RoomContext(poses), out _, out string refusal);

            Assert.IsFalse(found, "a fixture shelf has no wall-adjacent slot, so nothing may install mid-room");
            StringAssert.Contains("wall", refusal);
        }

        [Test]
        public void KindMatchedSlot_BeatsGenericSlot_WhenGenericComesFirst()
        {
            MakePlacement("plc-generic", FurnitureInstallBinding.UnspecifiedDefinitionId);
            MakePlacement("plc-matched", FurnitureItemSpecs.ShelfId);
            var poses = new Dictionary<string, FurniturePlacementPose>
            {
                { "plc-generic", new FurniturePlacementPose(-2f, -4.85f, 0f) },
                { "plc-matched", new FurniturePlacementPose(2f, -4.85f, 0f) },
            };

            bool found = FurnitureInstallBinding.TryFindFreePlacement(
                FurnitureItemSpecs.ShelfId, building, placements, PlacementIsBound,
                RoomContext(poses), out BuildingEquipmentPlacement placement, out string refusal);

            Assert.IsTrue(found, refusal);
            Assert.AreEqual("plc-matched", placement.PlacementId,
                "both slots are wall-adjacent, so the kind-matched slot wins and the generic slot stays free");
        }

        [Test]
        public void GenericSlot_IsStillUsed_WhenNoKindMatchedSlotExists()
        {
            MakePlacement("plc-generic", FurnitureInstallBinding.UnspecifiedDefinitionId);
            var poses = new Dictionary<string, FurniturePlacementPose>
            {
                { "plc-generic", new FurniturePlacementPose(0f, -4.85f, 0f) },
            };

            bool found = FurnitureInstallBinding.TryFindFreePlacement(
                FurnitureItemSpecs.ShelfId, building, placements, PlacementIsBound,
                RoomContext(poses), out BuildingEquipmentPlacement placement, out _);

            Assert.IsTrue(found);
            Assert.AreEqual("plc-generic", placement.PlacementId,
                "a generic slot is spent only when no kind-matched slot qualifies — but it is spent then");
        }

        [Test]
        public void Crate_PrefersNearDoorSlotOverFarDoorSlot()
        {
            MakePlacement("plc-far", FurnitureItemSpecs.CrateId);
            MakePlacement("plc-near", FurnitureItemSpecs.CrateId);
            var poses = new Dictionary<string, FurniturePlacementPose>
            {
                { "plc-far", new FurniturePlacementPose(0f, -4f, 0f) },
                { "plc-near", new FurniturePlacementPose(0f, 3f, 0f) },
            };

            bool found = FurnitureInstallBinding.TryFindFreePlacement(
                FurnitureItemSpecs.CrateId, building, placements, PlacementIsBound,
                RoomContext(poses), out BuildingEquipmentPlacement placement, out string refusal);

            Assert.IsTrue(found, refusal);
            Assert.AreEqual("plc-near", placement.PlacementId,
                "crates stage by the entrance: the near-door slot wins over the far-door slot that comes first");
        }

        [Test]
        public void PlacementCoveringDoorway_IsRefused()
        {
            MakePlacement("plc-door", FurnitureItemSpecs.CrateId);
            var poses = new Dictionary<string, FurniturePlacementPose>
            {
                { "plc-door", new FurniturePlacementPose(0f, 4f, 0f) },
            };

            bool found = FurnitureInstallBinding.TryFindFreePlacement(
                FurnitureItemSpecs.CrateId, building, placements, PlacementIsBound,
                RoomContext(poses), out _, out string refusal);

            Assert.IsFalse(found, "a placement on the doorway must never install");
            StringAssert.Contains("doorway", refusal);
        }

        [Test]
        public void PlacementCoveringActivityApproach_IsRefusedAndNamesTheApproach()
        {
            MakePlacement("plc-stove", FurnitureItemSpecs.ShelfId);
            var poses = new Dictionary<string, FurniturePlacementPose>
            {
                { "plc-stove", new FurniturePlacementPose(2f, -3f, 0f) },
            };
            var approachPoints = new List<FurnitureApproachPoint>
            {
                new FurnitureApproachPoint("stove-h1-a-stove", 2f, -3f),
            };

            bool found = FurnitureInstallBinding.TryFindFreePlacement(
                FurnitureItemSpecs.ShelfId, building, placements, PlacementIsBound,
                RoomContext(poses, approachPoints), out _, out string refusal);

            Assert.IsFalse(found, "a placement covering an activity approach must never install");
            StringAssert.Contains("stove-h1-a-stove", refusal);
        }

        // --- I-R2: the runtime wall-edge port ---

        [Test]
        public void AlignToNearestWall_MatchesEditorGeometry()
        {
            bool aligned = FurniturePlacementScoring.TryAlignToNearestWall(
                RoomPolygon(), 0f, 0f, 1.0f, 0.3f,
                out float alignedX, out float alignedZ, out float yawDegrees, out string error);

            Assert.IsTrue(aligned, error);
            Assert.AreEqual(0f, alignedX, 0.001f, "centred along the nearest (south) wall edge");
            Assert.AreEqual(-4.8f, alignedZ, 0.001f, "half the depth + 0.05 m inside the wall");
            Assert.AreEqual(0f, yawDegrees, 0.001f);
        }

        [Test]
        public void AlignToNearestWall_RefusesAnEdgeShorterThanTheWidth()
        {
            bool aligned = FurniturePlacementScoring.TryAlignToNearestWall(
                RoomPolygon(), 0f, 0f, 10.5f, 0.3f,
                out _, out _, out _, out string error);

            Assert.IsFalse(aligned, "no 10.5 m wall-facing width fits a 10 m edge");
            StringAssert.Contains("shorter", error);
        }

        // --- I-R3: installed-placement validation ---

        [Test]
        public void ValidateInstalledPlacement_ShelfMidRoom_ReportsWallWarning()
        {
            string diagnostic = FurniturePlacementScoring.ValidateInstalledPlacement(
                FurnitureItemSpecs.ShelfId, new FurniturePlacementPose(0f, 0f, 0f),
                1.0f, 0.3f, RoomContext(new Dictionary<string, FurniturePlacementPose>()));

            Assert.IsNotNull(diagnostic, "a shelf installed mid-room must produce a diagnostic");
            StringAssert.Contains("wall", diagnostic);
        }

        [Test]
        public void ValidateInstalledPlacement_ShelfAtWall_Passes()
        {
            string diagnostic = FurniturePlacementScoring.ValidateInstalledPlacement(
                FurnitureItemSpecs.ShelfId, new FurniturePlacementPose(0f, -4.85f, 0f),
                1.0f, 0.3f, RoomContext(new Dictionary<string, FurniturePlacementPose>()));

            Assert.IsNull(diagnostic, "a shelf against the wall passes the placement table");
        }

        [Test]
        public void ValidateBusinessFixturePlacement_AuthoredBakeryFixtures_Pass()
        {
            // The authored bakery layout (I-07), in the shared business
            // frame: oven + work counter against the -Z wall, approach
            // points 1.2 m in front. Dimensions are explicit test
            // inputs for the rule (the fixture record carries no
            // furniture spec row); the poses are the authored ones.
            FurniturePlacementContext context = FurniturePlacementContext.Build(
                BusinessFramePolygon(), hasDoor: true, doorX: 0f, doorZ: 2.35f,
                new List<FurnitureApproachPoint>
                {
                    new FurnitureApproachPoint("bakery-oven-approach", 3.3f, -3.5f),
                    new FurnitureApproachPoint("bakery-work-surface", 1.3f, -3.7f),
                },
                new Dictionary<string, FurniturePlacementPose>());

            Assert.IsNull(FurniturePlacementScoring.ValidateBusinessFixturePlacement(
                "oven-chamber", new FurniturePlacementPose(4.5f, -4.7f, 0f), 2.0f, 0.8f, context),
                "the authored oven against the -Z wall passes");
            Assert.IsNull(FurniturePlacementScoring.ValidateBusinessFixturePlacement(
                "fixture-work-surface", new FurniturePlacementPose(1.3f, -4.9f, 0f), 1.5f, 0.6f, context),
                "the authored work counter against the -Z wall passes");
            Assert.IsNull(FurniturePlacementScoring.ValidateBusinessFixturePlacement(
                "fixture-counter", new FurniturePlacementPose(-0.75f, -4.9f, 0f), 1.5f, 0.6f, context),
                "the new bakery retail counter (I-R6) in the free -Z stretch passes the one-end-to-wall rule");
            Assert.IsNull(FurniturePlacementScoring.ValidateBusinessFixturePlacement(
                "fixture-counter", new FurniturePlacementPose(-2.8f, -4.9f, 0f), 1.5f, 0.6f, context),
                "the new fuel-dealer counter (M-08) against the -Z wall passes the one-end-to-wall rule");
        }

        [Test]
        public void ValidateBusinessFixturePlacement_CounterMidRoom_ReportsShortEndRule()
        {
            FurniturePlacementContext context = FurniturePlacementContext.Build(
                BusinessFramePolygon(), hasDoor: true, doorX: 0f, doorZ: 2.35f,
                new List<FurnitureApproachPoint>(),
                new Dictionary<string, FurniturePlacementPose>());

            string diagnostic = FurniturePlacementScoring.ValidateBusinessFixturePlacement(
                "fixture-counter", new FurniturePlacementPose(0f, 0f, 0f), 1.5f, 0.6f, context);

            Assert.IsNotNull(diagnostic, "a counter floating mid-room must produce a diagnostic");
            StringAssert.Contains("short end", diagnostic);
        }

        // --- I-R7: the counter rule (decision owed, rule implemented as fixture validation) ---

        [Test]
        public void CounterRule_OneShortEndToWall_PassesAndMidRoomRefuses()
        {
            // The counter kind has no carpentry method and no spec row
            // (I-R7: owner decision owed) — the rule itself is scored
            // here with explicit test dimensions, and lands as fixture
            // validation for the counters that actually exist.
            FurniturePlacementContext context = RoomContext(new Dictionary<string, FurniturePlacementPose>());

            bool atWall = FurniturePlacementScoring.TryScoreCandidate(
                FurnishingClassification.KitchenCounterKind,
                new FurniturePlacementPose(-4.25f, 0f, 0f), 1.5f, 0.6f,
                kindMatched: true, context, out _, out string wallRefusal);
            bool midRoom = FurniturePlacementScoring.TryScoreCandidate(
                FurnishingClassification.KitchenCounterKind,
                new FurniturePlacementPose(0f, 0f, 0f), 1.5f, 0.6f,
                kindMatched: true, context, out _, out string midRefusal);

            Assert.IsTrue(atWall, "one short end on the west wall satisfies the counter rule: " + wallRefusal);
            Assert.IsFalse(midRoom, "a counter with neither short end to a wall is refused");
            StringAssert.Contains("wall", midRefusal);
        }

        // --- I-R4: bakery stage → approach point ---

        [Test]
        public void BakeryStageApproachPoints_RealStagesOnly()
        {
            Assert.IsTrue(FurniturePlacementScoring.TryBakeryStageApproachPoint(1, out string prepare, out bool prepareArrival));
            Assert.AreEqual("bakery-work-surface", prepare);
            Assert.IsTrue(prepareArrival, "prepare is active labour at the work surface");

            Assert.IsTrue(FurniturePlacementScoring.TryBakeryStageApproachPoint(3, out string heat, out bool heatArrival));
            Assert.AreEqual("bakery-oven-approach", heat);
            Assert.IsTrue(heatArrival);
            Assert.IsTrue(FurniturePlacementScoring.TryBakeryStageApproachPoint(4, out string load, out bool loadArrival));
            Assert.AreEqual("bakery-oven-approach", load);
            Assert.IsTrue(loadArrival);
            Assert.IsTrue(FurniturePlacementScoring.TryBakeryStageApproachPoint(6, out string unload, out bool unloadArrival));
            Assert.AreEqual("bakery-oven-approach", unload);
            Assert.IsTrue(unloadArrival);

            Assert.IsTrue(FurniturePlacementScoring.TryBakeryStageApproachPoint(2, out _, out bool proofArrival));
            Assert.IsFalse(proofArrival, "proof is passive — the rack holds the batch, the baker is released");
            Assert.IsTrue(FurniturePlacementScoring.TryBakeryStageApproachPoint(5, out _, out bool bakeArrival));
            Assert.IsFalse(bakeArrival, "bake is passive — the oven holds the batch");
            Assert.IsTrue(FurniturePlacementScoring.TryBakeryStageApproachPoint(7, out _, out bool coolArrival));
            Assert.IsFalse(coolArrival, "cool is passive");

            Assert.IsTrue(FurniturePlacementScoring.TryBakeryStageApproachPoint(0, out _, out bool receiveArrival));
            Assert.IsFalse(receiveArrival, "receive dispenses stock into batch custody — no approach point");
            Assert.IsTrue(FurniturePlacementScoring.TryBakeryStageApproachPoint(8, out string store, out bool storeArrival));
            Assert.IsNull(store, "the staged bread shelf has no activity location authored — the gap stays open, not faked");
            Assert.IsFalse(storeArrival);

            Assert.IsFalse(FurniturePlacementScoring.TryBakeryStageApproachPoint(99, out _, out _),
                "an unknown stage index is not a stage");
        }

        // --- I-R5 / I-R6: retail counter service points ---

        [Test]
        public void RetailCounterActivityIds_AllThreeSellersHaveOne()
        {
            Assert.IsTrue(FurniturePlacementScoring.TryRetailCounterActivityId(
                LandLedgers.Economy.BusinessType.GeneralStore, out string store));
            Assert.AreEqual("general-store-counter", store);
            Assert.IsTrue(FurniturePlacementScoring.TryRetailCounterActivityId(
                LandLedgers.Economy.BusinessType.Bakery, out string bakery));
            Assert.AreEqual("bakery-retail-counter", bakery);
            Assert.IsTrue(FurniturePlacementScoring.TryRetailCounterActivityId(
                LandLedgers.Economy.BusinessType.FuelDealer, out string dealer));
            Assert.AreEqual("fuel-dealer-counter", dealer);
            Assert.IsFalse(FurniturePlacementScoring.TryRetailCounterActivityId(
                LandLedgers.Economy.BusinessType.Blacksmith, out _),
                "a trade with no retail counter gets no invented service point");
        }

        // --- I-R8: capability-preferring selection ---

        [Test]
        public void SelectPreferredCandidate_PrefersHighestCapability_OwnerBreaksTies()
        {
            var candidates = new List<int> { 11, 12, 13 };
            var capability = new Dictionary<int, double> { { 11, 0.2 }, { 12, 0.8 }, { 13, 0.5 } };

            int selected = FurniturePlacementScoring.SelectPreferredCandidate(
                candidates, personId => true, personId => capability[personId]);

            Assert.AreEqual(12, selected, "the highest baking capability bakes");
        }

        [Test]
        public void SelectPreferredCandidate_TieKeepsFirstListed_NoviceStillEligible()
        {
            var candidates = new List<int> { 11, 12 };
            var capability = new Dictionary<int, double> { { 11, 0.4 }, { 12, 0.4 } };

            int selected = FurniturePlacementScoring.SelectPreferredCandidate(
                candidates, personId => true, personId => capability[personId]);

            Assert.AreEqual(11, selected, "a tie keeps the first-listed candidate — owner-first stays the tiebreak");

            int noviceOnly = FurniturePlacementScoring.SelectPreferredCandidate(
                new List<int> { 12 }, personId => true, personId => 0.05);
            Assert.AreEqual(12, noviceOnly, "no hard gates: a novice is picked when nobody better is available");
        }

        [Test]
        public void SelectPreferredCandidate_SkipsIneligible_AndReturnsMinusOneWhenNoneEligible()
        {
            int selected = FurniturePlacementScoring.SelectPreferredCandidate(
                new List<int> { 11, 12 }, personId => personId == 12, personId => 0.9);
            Assert.AreEqual(12, selected);

            int none = FurniturePlacementScoring.SelectPreferredCandidate(
                new List<int> { 11, 12 }, personId => false, personId => 0.9);
            Assert.AreEqual(-1, none);
        }

        // --- I-R1 through the install path (Lane 9f driver) ---

        private TaskAuthority tasks;
        private WorkTimeBudgetStore budgets;
        private EntityId owner;
        private List<EntityId> members;
        private List<FurniturePurchaseOrder> orders;
        private List<string> diag;

        private void SetUpDriver()
        {
            tasks = new TaskAuthority();
            FurnishingTaskDefinitions.RegisterAll(tasks);
            budgets = new WorkTimeBudgetStore();
            budgets.EnsureDay(DayIndex);
            owner = EntityId.For(EntityKind.Household, HouseholdId);
            members = new List<EntityId> { EntityId.For(EntityKind.Person, 701) };
            orders = new List<FurniturePurchaseOrder>();
            diag = new List<string>();
        }

        private bool DriverPlacementIsBound(string placementId)
        {
            if (string.IsNullOrWhiteSpace(placementId) || register?.Bindings == null) return false;
            foreach (FurnishingPlacementBinding binding in register.Bindings)
            {
                if (binding != null && string.Equals(binding.BindingId, placementId, StringComparison.Ordinal)) return true;
            }
            return false;
        }

        private EquipmentAsset MakeShelfAsset(string assetId)
        {
            var asset = new EquipmentAsset
            {
                AssetId = assetId,
                Kind = FurnitureItemSpecs.ShelfId,
                DisplayName = FurnitureItemSpecs.ShelfId,
                Condition01 = 1f,
                OwnerKind = "household",
                OwnerId = HouseholdId.ToString(),
                LocationId = "household:" + HouseholdId,
                MadeByBusinessId = "carpentry-1",
            };
            Assert.IsNull(register.TryRegisterAsset(asset), "test asset registration should succeed");
            return asset;
        }

        [Test]
        public void InstallPath_WithContext_InstallsShelfAtWallSlotNotFirstMatch()
        {
            SetUpDriver();
            MakeShelfAsset("shelf-1");
            BuildingEquipmentPlacement midPlacement = MakePlacement("plc-mid", FurnitureItemSpecs.ShelfId);
            BuildingEquipmentPlacement wallPlacement = MakePlacement("plc-wall", FurnitureItemSpecs.ShelfId);
            var poses = new Dictionary<string, FurniturePlacementPose>
            {
                { "plc-mid", new FurniturePlacementPose(0f, 0f, 0f) },
                { "plc-wall", new FurniturePlacementPose(0f, -4.85f, 0f) },
            };
            FurniturePlacementContext context = RoomContext(poses);

            FurnishingInstallTaskDriver.DriveDayResult result = FurnishingInstallTaskDriver.DriveDay(
                tasks, budgets, owner, members, HouseholdId, BuildingInstanceId,
                register, building, placements, DriverPlacementIsBound, orders,
                (personId, buildingId) => true, DayIndex, diag, context);

            Assert.AreEqual(1, result.InstalledAssets, "the shelf installs: " + string.Join(" | ", diag));
            FurnishingPlacementBinding binding = register.LiveBindingFor("shelf-1");
            Assert.IsNotNull(binding, "the shelf has a live binding");
            // TryInstall configures the chosen placement with the binding
            // id, so the binding's PlacementId is the binding id, not the
            // authored "plc-wall" (Lane 9f's own note) — the install proves
            // itself by referencing the authored wall placement component,
            // which still carries the shelf definition in the home building.
            Assert.AreEqual(wallPlacement.PlacementId, binding.PlacementId,
                "the install path calls the wall alignment instead of first-match: the wall slot wins");
            Assert.AreNotEqual(midPlacement.PlacementId, binding.PlacementId,
                "the mid-room slot was not configured — it was never installed into");
            Assert.AreEqual(FurnitureItemSpecs.ShelfId, wallPlacement.DefinitionId);
            Assert.AreEqual(building, wallPlacement.BuildingAuthority);
        }

        [Test]
        public void InstallPath_WithContext_ShelfWithOnlyMidRoomSlot_StaysInCustody()
        {
            SetUpDriver();
            MakeShelfAsset("shelf-1");
            MakePlacement("plc-mid", FurnitureItemSpecs.ShelfId);
            var poses = new Dictionary<string, FurniturePlacementPose>
            {
                { "plc-mid", new FurniturePlacementPose(0f, 0f, 0f) },
            };
            FurniturePlacementContext context = RoomContext(poses);

            FurnishingInstallTaskDriver.DriveDayResult result = FurnishingInstallTaskDriver.DriveDay(
                tasks, budgets, owner, members, HouseholdId, BuildingInstanceId,
                register, building, placements, DriverPlacementIsBound, orders,
                (personId, buildingId) => true, DayIndex, diag, context);

            Assert.AreEqual(0, result.InstalledAssets);
            Assert.AreEqual(1, result.BlockedAssets, "no wall-adjacent slot: the piece stays in custody, named");
            Assert.IsNull(register.LiveBindingFor("shelf-1"));
            Assert.IsTrue(diag.Exists(line => line != null && line.IndexOf("wall", StringComparison.OrdinalIgnoreCase) >= 0),
                "the blocker names the wall rule: " + string.Join(" | ", diag));
        }

        // --- I-R5: the clerk-arrival gate (loop side) ---

        private sealed class TestStore : IItemResolvingSupplier
        {
            public string SupplierBusinessId { get; set; } = "store-1";
            public string SupplierName { get; set; } = "General Store";
            public string LocationId { get; set; } = "store";
            public Dictionary<string, int> Stock = new Dictionary<string, int>();
            public Dictionary<string, int> Prices = new Dictionary<string, int>();
            public int CashReceived;
            public string LastReceiptIdValue = string.Empty;
            public int ReceiptSequence;

            public bool HasCategory(string categoryId) => true;
            public int StockUnits(string categoryId) => 0;
            public int PricePerUnitCents(string categoryId) => 0;
            public int Sell(string categoryId, int requestedUnits, int dayIndex, List<string> diagnostics) => 0;
            public bool OffersItem(string itemId) => Stock.ContainsKey(itemId);
            public string CategoryForItem(string itemId) => "staple_food";
            public int ItemStockUnits(string itemId) => Stock.TryGetValue(itemId, out int s) ? s : 0;
            public int ItemPricePerUnitCents(string itemId) => Prices.TryGetValue(itemId, out int p) ? p : 0;
            public string CheckTradingCapability(int absoluteDayIndex, int minuteOfDay) => null;
            public string LastReceiptId => LastReceiptIdValue;

            public int SellItem(string itemId, int requestedUnits, int dayIndex, ShoppingSettlement settlement, List<string> diagnostics)
            {
                int have = ItemStockUnits(itemId);
                int sold = System.Math.Min(System.Math.Max(0, requestedUnits), have);
                if (sold > 0)
                {
                    Stock[itemId] = have - sold;
                    LastReceiptIdValue = $"rcpt-{++ReceiptSequence}";
                    if (settlement != null && settlement.Mode == ShoppingSettlementMode.CashFromLedger)
                        CashReceived += settlement.AmountCents;
                }
                return sold;
            }
        }

        private sealed class ShoppingFixture
        {
            public readonly PopulationState Population = new PopulationState();
            public readonly HouseholdMembershipRegistry Membership = new HouseholdMembershipRegistry();
            public readonly HouseholdLedgerRegistry Ledgers = new HouseholdLedgerRegistry();
            public readonly HouseholdInventoryRegistry Inventories = new HouseholdInventoryRegistry();
            public readonly HouseholdNeedRegistry Needs = new HouseholdNeedRegistry();
            public readonly SupplierDirectory Directory = new SupplierDirectory();
            public readonly JourneyModel Journeys = new JourneyModel();
            public readonly TaskAuthority Tasks = new TaskAuthority();
            public readonly WorkTimeBudgetStore Budgets = new WorkTimeBudgetStore();
            public readonly PersonScheduleTracker Tracker = new PersonScheduleTracker();
            public readonly ShopperSellerKnowledge Knowledge = new ShopperSellerKnowledge();
            public readonly SimpleDueEventScheduler DueEvents = new SimpleDueEventScheduler();
            public readonly TestStore Store = new TestStore();
            public readonly HouseholdShoppingLoop Loop;
            public readonly ShoppingTripDriver Driver;
            public readonly Dictionary<int, string> Locations = new Dictionary<int, string>();
            public readonly List<string> Diag = new List<string>();
            public bool ClerkAtCounter;

            public const int HouseholdId = 7;
            public const int ShopperId = 1;
            public const int ClerkId = 3;
            public const int Day = 100;

            public ShoppingFixture()
            {
                Population.people.Add(new PersonState { id = 1, firstName = "Tomas", lastName = "Morrow", age = 34, ageBand = AgeBand.Adult18Plus });
                Population.people.Add(new PersonState { id = 2, firstName = "Petra", lastName = "Morrow", age = 8, ageBand = AgeBand.Child0To9 });
                Population.people.Add(new PersonState { id = 3, firstName = "Clerk", lastName = "Test", age = 30, ageBand = AgeBand.Adult18Plus });
                Population.households.Add(new HouseholdState { id = HouseholdId, householdName = "Morrow" });
                Membership.Register(new HouseholdMembership { MembershipId = 1, PersonId = 1, HouseholdId = HouseholdId, StartDayIndex = 0 });
                Membership.Register(new HouseholdMembership { MembershipId = 2, PersonId = 2, HouseholdId = HouseholdId, StartDayIndex = 0 });
                Budgets.EnsureDay(Day);

                Journeys.RegisterLocation(new JourneyLocation("home", JourneyLocationKind.Farmstead, "Morrow Farm", 0f, 0f));
                Journeys.RegisterLocation(new JourneyLocation("store", JourneyLocationKind.Store, "General Store", 2f, 0f));
                Journeys.AddEdge("home", "store", 2.0f, "river road");

                Store.Stock[HouseholdItemCatalog.FlourId] = 100;
                Store.Prices[HouseholdItemCatalog.FlourId] = 120;
                Directory.Register(Store);
                Knowledge.LearnSupplier(1, "store-1", "lives in the same town", 0);

                Locations[1] = "home";
                Locations[2] = "home";
                Locations[3] = "home";

                HouseholdShoppingLoop.RegisterTaskDefinitions(Tasks);
                var options = new ShoppingLoopOptions
                {
                    PersonLocationId = pid => Locations.TryGetValue(pid, out string l) ? l : "home",
                    SetPersonLocation = (pid, l) => Locations[pid] = l,
                    DepartureMinuteOfDay = 600,
                    InStoreMinutes = 15,
                    AllowDelivery = false,
                    Diagnostics = Diag,
                };
                Loop = new HouseholdShoppingLoop(
                    Population, Membership, Ledgers, Inventories, Needs, Directory, Journeys,
                    Tasks, Budgets, Tracker, Knowledge,
                    obligations: null, entityIds: null,
                    personEntityId: pid => EntityId.For(EntityKind.Person, pid),
                    spendingPolicyProvider: null);
                LoopOptions = options;
                Driver = new ShoppingTripDriver(Loop,
                    startPhysicalRoute: (personId, destination) =>
                    {
                        Locations[personId] = destination;
                        return true;
                    },
                    hasPhysicallyArrived: (personId, destination) =>
                        Locations.TryGetValue(personId, out string location)
                        && string.Equals(location, destination, StringComparison.Ordinal));
                Ledgers.GetOrCreate(HouseholdId).RecordInflow(0, 50000,
                    HouseholdIncomeSource.WageEmployment, "emp-1", "seed wages", "test");

                // Clerk + service wiring (the S1 pattern): the queue and
                // the single clerk candidate for the test store, plus the
                // Lane 10b arrival gate, driven by the fixture flag.
                GeneralStoreTaskCatalog.RegisterAll(Tasks);
                var queue = new CustomerQueue(new LostSaleLog());
                Queue = queue;
                Loop.ResolveCustomerQueue = businessId => businessId == "store-1" ? queue : null;
                Loop.ResolveServiceClerkPersonId = businessId => businessId == "store-1" ? ClerkId : -1;
                Loop.RequirePhysicalCounterService = true;
                Loop.HasArrivedAtCounter = (clerkPersonId, businessId) => ClerkAtCounter;
            }

            public CustomerQueue Queue { get; }
            public ShoppingLoopOptions LoopOptions;

            public HouseholdPurchasingNeed OpenNeed(string itemId, int units)
            {
                HouseholdPurchasingNeed need = Needs.CreateNeed(HouseholdId, itemId, Day);
                need.UnitsNeeded = units;
                need.IsNecessity = true;
                return need;
            }

            public PlannedShoppingTrip DriveToArrival(out float arrivalGameSeconds)
            {
                Driver.PlanForHousehold(HouseholdId, Needs, Day, LoopOptions, Diag);
                Assert.AreEqual(1, Driver.ActiveTrips.Count);
                PlannedShoppingTrip trip = Driver.ActiveTrips[0];
                float departure = Day * 86400f + trip.DepartureMinuteOfDay * 60f;
                Driver.AdvanceTick(departure, Day, DueEvents, Diag);
                DueEvent outbound = null;
                foreach (DueEvent ev in DueEvents.PopDue(departure + 86400f))
                {
                    if (ev.PayloadRef == trip.Legs.Outbound.TaskId.ToString())
                    {
                        outbound = ev;
                        break;
                    }
                }
                Assert.IsNotNull(outbound, "outbound leg due event not scheduled");
                Driver.HandleDueEvent(outbound, Day, outbound.DueGameSeconds, DueEvents, Diag);
                arrivalGameSeconds = outbound.DueGameSeconds;
                return trip;
            }

            public void Step(float fromGameSeconds, float minutes)
            {
                float now = fromGameSeconds + minutes * 60f;
                Driver.AdvanceTick(now, Day, DueEvents, Diag);
                foreach (DueEvent ev in DueEvents.PopDue(now))
                    Driver.HandleDueEvent(ev, Day, ev.DueGameSeconds, DueEvents, Diag);
            }
        }

        private static EntityId ServiceTaskIdOf(PlannedShoppingTrip trip)
        {
            System.Reflection.FieldInfo field = typeof(PlannedShoppingTrip).GetField("ServiceTaskId",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            Assert.IsNotNull(field, "PlannedShoppingTrip.ServiceTaskId field missing");
            return (EntityId)field.GetValue(trip);
        }

        [Test]
        public void ClerkArrivalGate_ClerkAway_CustomerStaysQueuedAndNoServiceStarts()
        {
            var f = new ShoppingFixture();
            f.ClerkAtCounter = false;
            f.OpenNeed(HouseholdItemCatalog.FlourId, 10);
            PlannedShoppingTrip trip = f.DriveToArrival(out float arrival);

            Assert.AreEqual(PlannedShoppingTrip.TripState.AwaitingService, trip.State,
                "the customer waits in the counter queue when the clerk has not arrived");
            Assert.IsFalse(ServiceTaskIdOf(trip).IsValid, "no TendCustomer task starts for a clerk who is not at the counter");
            Assert.AreEqual(1, f.Queue.Waiting.Count, "the customer is still queued");

            // The sweep retries on later ticks and still finds no clerk.
            f.Step(arrival, 5f);
            f.Step(arrival + 5f * 60f, 5f);
            Assert.IsFalse(ServiceTaskIdOf(trip).IsValid, "retries do not start service while the clerk is away");
            Assert.AreEqual(PlannedShoppingTrip.TripState.AwaitingService, trip.State);
            Assert.AreEqual(0, f.Store.CashReceived, "no sale settles without counter service");
        }

        [Test]
        public void ClerkArrivalGate_ClerkArrives_ServiceStartsOnRetry()
        {
            var f = new ShoppingFixture();
            f.ClerkAtCounter = false;
            f.OpenNeed(HouseholdItemCatalog.FlourId, 10);
            PlannedShoppingTrip trip = f.DriveToArrival(out float arrival);
            Assert.AreEqual(PlannedShoppingTrip.TripState.AwaitingService, trip.State);
            Assert.IsFalse(ServiceTaskIdOf(trip).IsValid);

            f.ClerkAtCounter = true;
            f.Step(arrival, 5f);

            Assert.IsTrue(ServiceTaskIdOf(trip).IsValid,
                "once the clerk reaches the counter, the sweep retry starts TendCustomer");
        }
    }
}
