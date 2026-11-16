using System.Collections.Generic;
using System.Reflection;
using LandLedgers.Economy.Blacksmith;
using LandLedgers.Population;
using LandLedgers.World;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

namespace LandLedgers.Editor.Population
{
    /// <summary>
    /// B6 (Agent B, expanded campaign 2026-11-16): acquisition / resale /
    /// furnishing decisions + the itemized property-contents contract for
    /// D. Proves the fixture/movable classification (the core deliverable
    /// D-6 and F6 consume), the contents listing + selection (All / No
    /// Movable / Custom, condition-weighted valuation, agreement validation
    /// and close re-validation, price == structure + contents with no
    /// double-counted fixture), and the furnishing evaluator (ranked wants
    /// from the B1/B4 need signals, affordability gates, secondhand as a
    /// first-class channel, resale candidates).
    ///
    /// Prices here are TEST-LOCAL calibration only (the price func is the
    /// caller's real price data in production — Agent F's prices; no
    /// production price is invented in this file). No test claims Unity
    /// compilation or PlayMode; the per-tick drivers are Agent G's wiring.
    /// </summary>
    [TestFixture]
    public sealed class FurnishingDecisionsB6Tests
    {
        private const int HouseholdId = 7;
        private const int OtherHouseholdId = 8;
        private const int BuildingInstanceId = 101;
        private const int OtherBuildingInstanceId = 102;
        private const int Day = 100;

        /// <summary>Test-local replacement costs (calibration only — see the class doc).</summary>
        private static int PriceForKind(string kind)
        {
            switch (kind)
            {
                case "furniture.bed": return 10000;
                case "furniture.table": return 8000;
                case "furniture.chair": return 3000;
                case "furniture.cabinet-small": return 5000;
                case "furniture.cabinet-large": return 7000;
                case "furniture.shelf": return 2000;
                case "furniture.crate": return 1500;
                case "furniture.bedroll": return 1000;
                case "furniture.cooking-stove": return 12000;
                case "furniture.kitchen-counter": return 6000;
                case "furniture.barrel": return 1000;
                default: return 0;
            }
        }

        private static HouseholdFurnishingRegister MakeRegister(int householdId = HouseholdId)
        {
            return new HouseholdFurnishingRegister { HouseholdId = householdId };
        }

        private static EquipmentAsset MakeAsset(string assetId, string kind, float condition = 1f, int ownerId = HouseholdId)
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

        private static FurnishingPlacementBinding InstallAsset(
            HouseholdFurnishingRegister register,
            BuildingFootprintAuthority building,
            string assetId,
            FurnishingCapabilityKind capability = FurnishingCapabilityKind.None,
            string capabilityReferenceId = "",
            int buildingInstanceId = BuildingInstanceId,
            Vector3 position = default)
        {
            BuildingEquipmentPlacement placement = MakePlacement(assetId, position);
            string error = register.TryInstall(assetId, placement, building, buildingInstanceId, 0,
                capability, capabilityReferenceId,
                FurnishingAcquisitionSource.ScenarioEndowment, "endowment-qa-town-1",
                out FurnishingPlacementBinding binding);
            Assert.IsNull(error, error);
            Assert.NotNull(binding);
            return binding;
        }

        private static HouseholdFurnishingRegister RegisterWith(string assetId, string kind, float condition = 1f)
        {
            var register = MakeRegister();
            Assert.IsNull(register.TryRegisterAsset(MakeAsset(assetId, kind, condition)));
            return register;
        }

        private sealed class ContentsFixture
        {
            public GameObject BuildingObject;
            public GameObject OtherBuildingObject;
            public List<GameObject> ItemObjects = new List<GameObject>();
            public HouseholdFurnishingRegister Register;
        }

        private static ContentsFixture MakeContentsFixture()
        {
            BuildingFootprintAuthority building = MakeBuilding("B6 contents building");
            BuildingFootprintAuthority other = MakeBuilding("B6 contents other building");
            var fixture = new ContentsFixture
            {
                BuildingObject = building.gameObject,
                OtherBuildingObject = other.gameObject,
                Register = MakeRegister(),
            };
            return fixture;
        }

        private static void DestroyFixture(ContentsFixture fixture)
        {
            if (fixture == null) return;
            foreach (GameObject item in fixture.ItemObjects)
            {
                if (item != null) Object.DestroyImmediate(item);
            }
            if (fixture.BuildingObject != null) Object.DestroyImmediate(fixture.BuildingObject);
            if (fixture.OtherBuildingObject != null) Object.DestroyImmediate(fixture.OtherBuildingObject);
        }

        private static void InstallInto(ContentsFixture fixture, string assetId, string kind, float condition = 1f,
            FurnishingCapabilityKind capability = FurnishingCapabilityKind.None, int buildingInstanceId = BuildingInstanceId)
        {
            Assert.IsNull(fixture.Register.TryRegisterAsset(MakeAsset(assetId, kind, condition)));
            BuildingFootprintAuthority building = buildingInstanceId == BuildingInstanceId
                ? fixture.BuildingObject.GetComponent<BuildingFootprintAuthority>()
                : fixture.OtherBuildingObject.GetComponent<BuildingFootprintAuthority>();
            BuildingEquipmentPlacement placement = MakePlacement(assetId, Vector3.zero);
            fixture.ItemObjects.Add(placement.gameObject);
            string error = fixture.Register.TryInstall(assetId, placement, building, buildingInstanceId, 0,
                capability, string.Empty,
                FurnishingAcquisitionSource.ScenarioEndowment, "endowment-qa-town-1",
                out FurnishingPlacementBinding binding);
            Assert.IsNull(error, error);
            Assert.NotNull(binding);
        }

        private static List<FurnishingContentsItem> StandardListing(ContentsFixture fixture)
        {
            InstallInto(fixture, "bed-1", "furniture.bed");
            InstallInto(fixture, "chair-1", "furniture.chair", 0.5f);
            InstallInto(fixture, "stove-1", "furniture.cooking-stove");
            return PropertyContentsSelection.ListEligibleContents(fixture.Register, BuildingInstanceId);
        }

        // ----------------------------------------------------------------
        // Classification
        // ----------------------------------------------------------------

        [Test]
        public void Classify_FirstWaveKinds()
        {
            Assert.AreEqual(FurnishingFixtureKind.Movable, FurnishingClassification.Classify("furniture.bed"));
            Assert.AreEqual(FurnishingFixtureKind.Movable, FurnishingClassification.Classify("furniture.table"));
            Assert.AreEqual(FurnishingFixtureKind.Movable, FurnishingClassification.Classify("furniture.chair"));
            Assert.AreEqual(FurnishingFixtureKind.Movable, FurnishingClassification.Classify("furniture.cabinet-small"));
            Assert.AreEqual(FurnishingFixtureKind.Movable, FurnishingClassification.Classify("furniture.cabinet-large"));
            Assert.AreEqual(FurnishingFixtureKind.Movable, FurnishingClassification.Classify("furniture.bedroll"));
            Assert.AreEqual(FurnishingFixtureKind.Movable, FurnishingClassification.Classify("furniture.crate"));
            Assert.AreEqual(FurnishingFixtureKind.Fixture, FurnishingClassification.Classify("furniture.shelf"));
        }

        [Test]
        public void Classify_StoveCounterBarrel()
        {
            Assert.AreEqual(FurnishingFixtureKind.Fixture, FurnishingClassification.Classify("furniture.cooking-stove"));
            Assert.AreEqual(FurnishingFixtureKind.Fixture, FurnishingClassification.Classify("furniture.kitchen-counter"));
            Assert.AreEqual(FurnishingFixtureKind.Movable, FurnishingClassification.Classify("furniture.barrel"));
        }

        [Test]
        public void Classify_UnknownKind_IsUnspecified()
        {
            Assert.AreEqual(FurnishingFixtureKind.Unspecified, FurnishingClassification.Classify("furniture.throne"));
            Assert.AreEqual(FurnishingFixtureKind.Unspecified, FurnishingClassification.Classify(""));
            Assert.AreEqual(FurnishingFixtureKind.Unspecified, FurnishingClassification.Classify(null));
            Assert.IsFalse(FurnishingClassification.IsKnownKind("furniture.throne"));
            Assert.IsTrue(FurnishingClassification.IsKnownKind("furniture.bed"));
        }

        [Test]
        public void Classify_FixtureSet_IsExactlyThreeOfEleven()
        {
            string[] known =
            {
                "furniture.bed", "furniture.table", "furniture.chair", "furniture.cabinet-small",
                "furniture.cabinet-large", "furniture.shelf", "furniture.crate", "furniture.bedroll",
                "furniture.cooking-stove", "furniture.kitchen-counter", "furniture.barrel",
            };
            int fixtures = 0;
            foreach (string kind in known)
            {
                if (FurnishingClassification.Classify(kind) == FurnishingFixtureKind.Fixture) fixtures++;
            }
            Assert.AreEqual(3, fixtures, "fixtures are exactly the stove, the counter and the shelf.");
        }

        [Test]
        public void FirstWaveKinds_HasEight()
        {
            Assert.AreEqual(8, FurnishingClassification.FirstWaveKinds.Length);
            Assert.AreEqual("furniture.bed", FurnishingClassification.FirstWaveKinds[0]);
            Assert.AreEqual("furniture.bedroll", FurnishingClassification.FirstWaveKinds[7]);
        }

        // ----------------------------------------------------------------
        // Contents listing
        // ----------------------------------------------------------------

        [Test]
        public void ListEligibleContents_ListsInstalledInBuildingOnly()
        {
            ContentsFixture fixture = null;
            try
            {
                fixture = MakeContentsFixture();
                InstallInto(fixture, "bed-1", "furniture.bed");
                InstallInto(fixture, "table-1", "furniture.table", 1f, FurnishingCapabilityKind.None, OtherBuildingInstanceId);
                Assert.IsNull(fixture.Register.TryRegisterAsset(MakeAsset("bedroll-1", "furniture.bedroll")));

                List<FurnishingContentsItem> items = PropertyContentsSelection.ListEligibleContents(fixture.Register, BuildingInstanceId);
                Assert.AreEqual(1, items.Count);
                Assert.AreEqual("bed-1", items[0].AssetId);
            }
            finally
            {
                DestroyFixture(fixture);
            }
        }

        [Test]
        public void ListEligibleContents_CarriesOwnerConditionKindAndLocation()
        {
            ContentsFixture fixture = null;
            try
            {
                fixture = MakeContentsFixture();
                InstallInto(fixture, "bed-1", "furniture.bed", 0.6f);
                InstallInto(fixture, "stove-1", "furniture.cooking-stove", 1f);

                List<FurnishingContentsItem> items = PropertyContentsSelection.ListEligibleContents(fixture.Register, BuildingInstanceId);
                Assert.AreEqual(2, items.Count);
                FurnishingContentsItem bed = items.Find(i => i.AssetId == "bed-1");
                FurnishingContentsItem stove = items.Find(i => i.AssetId == "stove-1");
                Assert.NotNull(bed);
                Assert.NotNull(stove);
                Assert.AreEqual(FurnishingFixtureKind.Movable, bed.Kind);
                Assert.AreEqual(FurnishingFixtureKind.Fixture, stove.Kind);
                Assert.AreEqual("household", bed.OwnerKind);
                Assert.AreEqual(HouseholdId.ToString(), bed.OwnerId);
                Assert.AreEqual(0.6f, bed.Condition01);
                Assert.AreEqual(BuildingInstanceId, bed.BuildingInstanceId);
                Assert.IsTrue(bed.Installed);
                Assert.IsTrue(bed.IsUsable);
                Assert.AreEqual("furniture.bed", bed.AssetKind);
            }
            finally
            {
                DestroyFixture(fixture);
            }
        }

        [Test]
        public void ListEligibleContents_NullRegister_IsEmpty()
        {
            Assert.AreEqual(0, PropertyContentsSelection.ListEligibleContents(null, BuildingInstanceId).Count);
        }

        // ----------------------------------------------------------------
        // Contents selection: build + valuation
        // ----------------------------------------------------------------

        [Test]
        public void BuildAll_IncludesEverything_PricesConditionWeighted()
        {
            ContentsFixture fixture = null;
            try
            {
                fixture = MakeContentsFixture();
                List<FurnishingContentsItem> items = StandardListing(fixture);

                PropertyContentsSelection selection = PropertyContentsSelection.BuildAll(
                    items, "household", HouseholdId.ToString(), BuildingInstanceId, Day, PriceForKind);
                Assert.AreEqual(ContentsSelectionMode.All, selection.Mode);
                Assert.AreEqual(3, selection.Lines.Count);
                Assert.AreEqual(0, selection.SkippedUnclassifiedAssetIds.Count);
                // Bed 10000 x 1.0 + chair 3000 x 0.5 + stove 12000 x 1.0 = 23500.
                Assert.AreEqual(23500, selection.TotalContentsValueCents);
                Assert.AreEqual(12000, selection.IncludedFixtureValueCents);
                Assert.AreEqual(11500, selection.IncludedMovableValueCents);
                Assert.AreEqual(0, selection.UnpricedIncludedAssetIds.Count);
            }
            finally
            {
                DestroyFixture(fixture);
            }
        }

        [Test]
        public void BuildNoMovable_IncludesFixturesOnly()
        {
            ContentsFixture fixture = null;
            try
            {
                fixture = MakeContentsFixture();
                StandardListing(fixture);
                InstallInto(fixture, "shelf-1", "furniture.shelf");
                List<FurnishingContentsItem> items = PropertyContentsSelection.ListEligibleContents(fixture.Register, BuildingInstanceId);

                PropertyContentsSelection selection = PropertyContentsSelection.BuildNoMovable(
                    items, "household", HouseholdId.ToString(), BuildingInstanceId, Day, PriceForKind);
                Assert.AreEqual(ContentsSelectionMode.NoMovable, selection.Mode);
                FurnishingContentsLine stove = selection.Lines.Find(l => l.AssetId == "stove-1");
                FurnishingContentsLine shelf = selection.Lines.Find(l => l.AssetId == "shelf-1");
                FurnishingContentsLine bed = selection.Lines.Find(l => l.AssetId == "bed-1");
                Assert.IsTrue(stove.Included);
                Assert.IsTrue(shelf.Included);
                Assert.IsFalse(bed.Included);
                Assert.AreEqual(ExcludedItemDisposition.RemovedBySeller, bed.Disposition);
                Assert.AreEqual(14000, selection.IncludedFixtureValueCents);
                Assert.AreEqual(0, selection.IncludedMovableValueCents);
                Assert.AreEqual(14000, selection.TotalContentsValueCents);
            }
            finally
            {
                DestroyFixture(fixture);
            }
        }

        [Test]
        public void BuildCustom_TogglesPerItem()
        {
            ContentsFixture fixture = null;
            try
            {
                fixture = MakeContentsFixture();
                List<FurnishingContentsItem> items = StandardListing(fixture);

                PropertyContentsSelection selection = PropertyContentsSelection.BuildCustom(
                    items, new[] { "bed-1" }, "household", HouseholdId.ToString(), BuildingInstanceId, Day, PriceForKind);
                Assert.AreEqual(ContentsSelectionMode.Custom, selection.Mode);
                FurnishingContentsLine bed = selection.Lines.Find(l => l.AssetId == "bed-1");
                FurnishingContentsLine stove = selection.Lines.Find(l => l.AssetId == "stove-1");
                FurnishingContentsLine chair = selection.Lines.Find(l => l.AssetId == "chair-1");
                Assert.IsTrue(bed.Included);
                Assert.AreEqual(10000, bed.AgreedValueCents);
                Assert.IsFalse(stove.Included);
                Assert.AreEqual(ExcludedItemDisposition.HeldInPlace, stove.Disposition, "a reserved fixture stays in place as the seller's property.");
                Assert.IsFalse(chair.Included);
                Assert.AreEqual(ExcludedItemDisposition.RemovedBySeller, chair.Disposition, "an excluded movable goes with the seller.");
                Assert.AreEqual(10000, selection.TotalContentsValueCents);
            }
            finally
            {
                DestroyFixture(fixture);
            }
        }

        [Test]
        public void Build_UnusableAsset_ContributesZero()
        {
            ContentsFixture fixture = null;
            try
            {
                fixture = MakeContentsFixture();
                InstallInto(fixture, "bed-1", "furniture.bed", 0.05f);
                List<FurnishingContentsItem> items = PropertyContentsSelection.ListEligibleContents(fixture.Register, BuildingInstanceId);

                PropertyContentsSelection selection = PropertyContentsSelection.BuildAll(
                    items, "household", HouseholdId.ToString(), BuildingInstanceId, Day, PriceForKind);
                Assert.AreEqual(0, selection.TotalContentsValueCents);
                Assert.AreEqual(0, selection.Lines[0].AgreedValueCents);
                Assert.IsTrue(selection.Lines[0].ConditionNote.Contains("unusable"));
            }
            finally
            {
                DestroyFixture(fixture);
            }
        }

        [Test]
        public void Build_UnpricedKind_RecordedNotGuessed()
        {
            ContentsFixture fixture = null;
            try
            {
                fixture = MakeContentsFixture();
                InstallInto(fixture, "bed-1", "furniture.bed");
                InstallInto(fixture, "chair-1", "furniture.chair");
                List<FurnishingContentsItem> items = PropertyContentsSelection.ListEligibleContents(fixture.Register, BuildingInstanceId);

                PropertyContentsSelection selection = PropertyContentsSelection.BuildAll(
                    items, "household", HouseholdId.ToString(), BuildingInstanceId, Day,
                    kind => kind == "furniture.bed" ? 10000 : 0);
                Assert.AreEqual(10000, selection.TotalContentsValueCents);
                Assert.AreEqual(1, selection.UnpricedIncludedAssetIds.Count);
                Assert.AreEqual("chair-1", selection.UnpricedIncludedAssetIds[0]);
                Assert.AreEqual(1, selection.Validate(fixture.Register, requirePriced: true).Count,
                    "agreement-time validation refuses an unpriced included item.");
                Assert.AreEqual(0, selection.Validate(fixture.Register, requirePriced: false).Count,
                    "a negotiation draft may carry an unpriced line.");
            }
            finally
            {
                DestroyFixture(fixture);
            }
        }

        [Test]
        public void Build_UnclassifiedItem_SkippedAndFlagged()
        {
            ContentsFixture fixture = null;
            try
            {
                fixture = MakeContentsFixture();
                InstallInto(fixture, "bed-1", "furniture.bed");
                InstallInto(fixture, "throne-1", "furniture.throne");
                List<FurnishingContentsItem> items = PropertyContentsSelection.ListEligibleContents(fixture.Register, BuildingInstanceId);

                PropertyContentsSelection selection = PropertyContentsSelection.BuildAll(
                    items, "household", HouseholdId.ToString(), BuildingInstanceId, Day, PriceForKind);
                Assert.AreEqual(1, selection.Lines.Count);
                Assert.AreEqual(1, selection.SkippedUnclassifiedAssetIds.Count);
                Assert.AreEqual("throne-1", selection.SkippedUnclassifiedAssetIds[0]);
                Assert.AreEqual(1, selection.Validate(fixture.Register, requirePriced: true).Count);
                Assert.IsTrue(selection.Validate(fixture.Register, requirePriced: true)[0].Contains("throne-1"));
            }
            finally
            {
                DestroyFixture(fixture);
            }
        }

        [Test]
        public void BuildCustom_UnknownIncludedId_RecordedNotInvented()
        {
            ContentsFixture fixture = null;
            try
            {
                fixture = MakeContentsFixture();
                List<FurnishingContentsItem> items = StandardListing(fixture);

                PropertyContentsSelection selection = PropertyContentsSelection.BuildCustom(
                    items, new[] { "bed-1", "ghost-1" }, "household", HouseholdId.ToString(), BuildingInstanceId, Day, PriceForKind);
                Assert.AreEqual(1, selection.SkippedUnknownAssetIds.Count);
                Assert.AreEqual("ghost-1", selection.SkippedUnknownAssetIds[0]);
                Assert.AreEqual(3, selection.Lines.Count, "no line is invented for the unknown id.");
                Assert.IsTrue(string.Join("; ", selection.Validate(fixture.Register, requirePriced: true)).Contains("ghost-1"));
            }
            finally
            {
                DestroyFixture(fixture);
            }
        }

        // ----------------------------------------------------------------
        // Contents selection: validation + re-validation + conservation
        // ----------------------------------------------------------------

        [Test]
        public void Validate_Agreement_PassesWhenComplete()
        {
            ContentsFixture fixture = null;
            try
            {
                fixture = MakeContentsFixture();
                List<FurnishingContentsItem> items = StandardListing(fixture);
                PropertyContentsSelection selection = PropertyContentsSelection.BuildAll(
                    items, "household", HouseholdId.ToString(), BuildingInstanceId, Day, PriceForKind);
                Assert.AreEqual(0, selection.Validate(fixture.Register, requirePriced: true).Count,
                    string.Join("; ", selection.Validate(fixture.Register, requirePriced: true)));
            }
            finally
            {
                DestroyFixture(fixture);
            }
        }

        [Test]
        public void Validate_WrongOwner_Refused()
        {
            ContentsFixture fixture = null;
            try
            {
                fixture = MakeContentsFixture();
                InstallInto(fixture, "bed-1", "furniture.bed");
                List<FurnishingContentsItem> items = PropertyContentsSelection.ListEligibleContents(fixture.Register, BuildingInstanceId);
                PropertyContentsSelection selection = PropertyContentsSelection.BuildAll(
                    items, "household", OtherHouseholdId.ToString(), BuildingInstanceId, Day, PriceForKind);
                List<string> problems = selection.Validate(fixture.Register, requirePriced: true);
                Assert.AreEqual(1, problems.Count);
                Assert.IsTrue(problems[0].Contains("no authority"));
            }
            finally
            {
                DestroyFixture(fixture);
            }
        }

        [Test]
        public void Validate_ExcludedWithoutDisposition_Refused()
        {
            ContentsFixture fixture = null;
            try
            {
                fixture = MakeContentsFixture();
                List<FurnishingContentsItem> items = StandardListing(fixture);
                PropertyContentsSelection selection = PropertyContentsSelection.BuildNoMovable(
                    items, "household", HouseholdId.ToString(), BuildingInstanceId, Day, PriceForKind);
                FurnishingContentsLine bed = selection.Lines.Find(l => l.AssetId == "bed-1");
                bed.Disposition = ExcludedItemDisposition.Unspecified;
                List<string> problems = selection.Validate(fixture.Register, requirePriced: true);
                Assert.AreEqual(1, problems.Count);
                Assert.IsTrue(problems[0].Contains("bed-1") && problems[0].Contains("disposition"));
            }
            finally
            {
                DestroyFixture(fixture);
            }
        }

        [Test]
        public void Validate_StaleSelection_ItemInstalledAfterBuild()
        {
            ContentsFixture fixture = null;
            try
            {
                fixture = MakeContentsFixture();
                List<FurnishingContentsItem> items = StandardListing(fixture);
                PropertyContentsSelection selection = PropertyContentsSelection.BuildAll(
                    items, "household", HouseholdId.ToString(), BuildingInstanceId, Day, PriceForKind);
                InstallInto(fixture, "table-1", "furniture.table");
                List<string> problems = selection.Validate(fixture.Register, requirePriced: true);
                Assert.AreEqual(1, problems.Count);
                Assert.IsTrue(problems[0].Contains("table-1") && problems[0].Contains("stale"));
            }
            finally
            {
                DestroyFixture(fixture);
            }
        }

        [Test]
        public void Revalidate_MissingItem_FailsLoudly()
        {
            ContentsFixture fixture = null;
            try
            {
                fixture = MakeContentsFixture();
                List<FurnishingContentsItem> items = StandardListing(fixture);
                PropertyContentsSelection selection = PropertyContentsSelection.BuildAll(
                    items, "household", HouseholdId.ToString(), BuildingInstanceId, Day, PriceForKind);
                EquipmentAsset bed = fixture.Register.FindAsset("bed-1");
                fixture.Register.Assets.Remove(bed);

                List<string> problems = selection.Revalidate(fixture.Register, out List<string> notes);
                Assert.AreEqual(1, problems.Count);
                Assert.IsTrue(problems[0].Contains("bed-1") && problems[0].Contains("no longer exists"));
                Assert.AreEqual(0, notes.Count);
            }
            finally
            {
                DestroyFixture(fixture);
            }
        }

        [Test]
        public void Revalidate_ConditionChange_FailsStale()
        {
            ContentsFixture fixture = null;
            try
            {
                fixture = MakeContentsFixture();
                List<FurnishingContentsItem> items = StandardListing(fixture);
                PropertyContentsSelection selection = PropertyContentsSelection.BuildAll(
                    items, "household", HouseholdId.ToString(), BuildingInstanceId, Day, PriceForKind);
                fixture.Register.FindAsset("bed-1").Condition01 = 0.5f;

                List<string> problems = selection.Revalidate(fixture.Register, out List<string> notes);
                Assert.AreEqual(1, problems.Count);
                Assert.IsTrue(problems[0].Contains("bed-1") && problems[0].Contains("stale"));
                Assert.AreEqual(0, notes.Count);
            }
            finally
            {
                DestroyFixture(fixture);
            }
        }

        [Test]
        public void Revalidate_ExcludedDrift_IsNoteNotFailure()
        {
            ContentsFixture fixture = null;
            try
            {
                fixture = MakeContentsFixture();
                List<FurnishingContentsItem> items = StandardListing(fixture);
                PropertyContentsSelection selection = PropertyContentsSelection.BuildNoMovable(
                    items, "household", HouseholdId.ToString(), BuildingInstanceId, Day, PriceForKind);
                Assert.IsNull(fixture.Register.TryRemove("bed-1"));

                List<string> problems = selection.Revalidate(fixture.Register, out List<string> notes);
                Assert.AreEqual(0, problems.Count, "an excluded item leaving is the seller's own property leaving, not a breach.");
                Assert.AreEqual(1, notes.Count);
                Assert.IsTrue(notes[0].Contains("bed-1"));
            }
            finally
            {
                DestroyFixture(fixture);
            }
        }

        [Test]
        public void CheckConservation_Balanced_Passes()
        {
            Assert.IsNull(PropertyContentsSelection.CheckConservation(8500, 11500, 20000));
        }

        [Test]
        public void CheckConservation_Mismatch_NamesAllThreeNumbers()
        {
            string problem = PropertyContentsSelection.CheckConservation(8500, 11500, 20001);
            Assert.IsNotNull(problem);
            Assert.IsTrue(problem.Contains("20001") && problem.Contains("8500") && problem.Contains("11500"));
            Assert.IsTrue(problem.Contains("1c") || problem.Contains("off by 1c"));
        }

        [Test]
        public void FixtureSplit_LetsStructureExcludeFixtures()
        {
            ContentsFixture fixture = null;
            try
            {
                fixture = MakeContentsFixture();
                InstallInto(fixture, "bed-1", "furniture.bed");
                InstallInto(fixture, "stove-1", "furniture.cooking-stove");
                List<FurnishingContentsItem> items = PropertyContentsSelection.ListEligibleContents(fixture.Register, BuildingInstanceId);
                PropertyContentsSelection selection = PropertyContentsSelection.BuildAll(
                    items, "household", HouseholdId.ToString(), BuildingInstanceId, Day, PriceForKind);
                Assert.AreEqual(12000, selection.IncludedFixtureValueCents);
                Assert.AreEqual(22000, selection.TotalContentsValueCents);
                // Structure value excludes the included fixtures (12000c):
                // 8500 structure + 22000 contents = 30500 agreed price.
                Assert.IsNull(PropertyContentsSelection.CheckConservation(8500, selection.TotalContentsValueCents, 30500));
                Assert.IsNotNull(PropertyContentsSelection.CheckConservation(8500 + 12000, selection.TotalContentsValueCents, 30500),
                    "counting the fixtures in the structure value too double-counts them.");
            }
            finally
            {
                DestroyFixture(fixture);
            }
        }

        // ----------------------------------------------------------------
        // Evaluator: wants
        // ----------------------------------------------------------------

        private static HouseholdFurnishingEvaluationInputs BaseInputs(
            HouseholdFurnishingRegister register, List<int> members, int cashCents)
        {
            return new HouseholdFurnishingEvaluationInputs
            {
                HouseholdId = HouseholdId,
                MemberIds = members,
                CashCents = cashCents,
                Furnishings = register,
                DayIndex = Day,
                ReplacementCostCentsForKind = PriceForKind,
            };
        }

        private static void InstallSleepingBed(ContentsFixture fixture, string assetId, float condition = 1f)
        {
            Assert.IsNull(fixture.Register.TryRegisterAsset(MakeAsset(assetId, "furniture.bed", condition)));
            BuildingFootprintAuthority building = fixture.BuildingObject.GetComponent<BuildingFootprintAuthority>();
            BuildingEquipmentPlacement placement = MakePlacement(assetId, Vector3.zero);
            fixture.ItemObjects.Add(placement.gameObject);
            string error = fixture.Register.TryInstall(assetId, placement, building, BuildingInstanceId, 0,
                FurnishingCapabilityKind.SleepingPlace, "bed-1",
                FurnishingAcquisitionSource.ScenarioEndowment, "endowment-qa-town-1",
                out FurnishingPlacementBinding binding);
            Assert.IsNull(error, error);
            Assert.NotNull(binding);
        }

        private static void InstallDiningSet(ContentsFixture fixture, int chairCount, bool withTable = true)
        {
            BuildingFootprintAuthority building = fixture.BuildingObject.GetComponent<BuildingFootprintAuthority>();
            if (withTable)
            {
                Assert.IsNull(fixture.Register.TryRegisterAsset(MakeAsset("table-1", "furniture.table")));
                BuildingEquipmentPlacement tablePlacement = MakePlacement("table-1", Vector3.zero);
                fixture.ItemObjects.Add(tablePlacement.gameObject);
                string error = fixture.Register.TryInstall("table-1", tablePlacement, building, BuildingInstanceId, 0,
                    FurnishingCapabilityKind.DiningTable, "dining",
                    FurnishingAcquisitionSource.ScenarioEndowment, "endowment-qa-town-1",
                    out FurnishingPlacementBinding tableBinding);
                Assert.IsNull(error, error);
                Assert.NotNull(tableBinding);
            }
            var positions = new[] { new Vector3(1f, 0f, 0f), new Vector3(-1f, 0f, 0f), new Vector3(0f, 0f, 1f) };
            for (int i = 0; i < chairCount; i++)
            {
                string assetId = "chair-" + (i + 1);
                Assert.IsNull(fixture.Register.TryRegisterAsset(MakeAsset(assetId, "furniture.chair")));
                BuildingEquipmentPlacement chairPlacement = MakePlacement(assetId, positions[i]);
                fixture.ItemObjects.Add(chairPlacement.gameObject);
                string error = fixture.Register.TryInstall(assetId, chairPlacement, building, BuildingInstanceId, 0,
                    FurnishingCapabilityKind.DiningSeat, "dining-chair-" + (i + 1),
                    FurnishingAcquisitionSource.ScenarioEndowment, "endowment-qa-town-1",
                    out FurnishingPlacementBinding chairBinding);
                Assert.IsNull(error, error);
                Assert.NotNull(chairBinding);
            }
        }

        private static void InstallStove(ContentsFixture fixture)
        {
            Assert.IsNull(fixture.Register.TryRegisterAsset(MakeAsset("stove-1", "furniture.cooking-stove")));
            BuildingFootprintAuthority building = fixture.BuildingObject.GetComponent<BuildingFootprintAuthority>();
            BuildingEquipmentPlacement placement = MakePlacement("stove-1", Vector3.zero);
            fixture.ItemObjects.Add(placement.gameObject);
            string error = fixture.Register.TryInstall("stove-1", placement, building, BuildingInstanceId, 0,
                FurnishingCapabilityKind.StoveWorkstation, "household-stove",
                FurnishingAcquisitionSource.ScenarioEndowment, "endowment-qa-town-1",
                out FurnishingPlacementBinding binding);
            Assert.IsNull(error, error);
            Assert.NotNull(binding);
        }

        [Test]
        public void Wants_FloorSleepers_RankBedFirst()
        {
            ContentsFixture fixture = null;
            try
            {
                fixture = MakeContentsFixture();
                var members = new List<int> { 1, 2 };
                var inputs = BaseInputs(fixture.Register, members, 20000);
                inputs.Sleeping = new SleepingPlaceRegistry
                {
                    HouseholdId = HouseholdId, Furnishings = fixture.Register, HomeBuildingInstanceId = BuildingInstanceId,
                };

                List<FurnishingWant> wants = HouseholdFurnishingEvaluator.EvaluateWants(inputs);
                Assert.AreEqual(2, wants.Count, "bed first, then the stove — no dining or storage signal was supplied.");
                Assert.AreEqual("furniture.bed", wants[0].ItemKind);
                Assert.AreEqual(FurnishingWantReason.BedForFloorSleeper, wants[0].Reason);
                Assert.AreEqual(1, wants[0].Priority);
                Assert.AreEqual(2, wants[0].PersonsAffected);
                Assert.AreEqual(10000, wants[0].EstimatedCostCents);
                Assert.IsTrue(wants[0].Affordable);
                Assert.AreEqual(FurnishingWantChannel.New, wants[0].Channel);
            }
            finally
            {
                DestroyFixture(fixture);
            }
        }

        [Test]
        public void Wants_UnaffordableBed_StaysWantSecondhand()
        {
            ContentsFixture fixture = null;
            try
            {
                fixture = MakeContentsFixture();
                var members = new List<int> { 1, 2 };
                var inputs = BaseInputs(fixture.Register, members, 5000);
                inputs.Sleeping = new SleepingPlaceRegistry
                {
                    HouseholdId = HouseholdId, Furnishings = fixture.Register, HomeBuildingInstanceId = BuildingInstanceId,
                };

                List<FurnishingWant> wants = HouseholdFurnishingEvaluator.EvaluateWants(inputs);
                FurnishingWant bed = wants.Find(w => w.ItemKind == "furniture.bed");
                Assert.NotNull(bed, "a want that cannot be afforded stays a want.");
                Assert.IsFalse(bed.Affordable);
                Assert.AreEqual(FurnishingWantChannel.Secondhand, bed.Channel);
                Assert.IsTrue(bed.Note.Contains("5000c") && bed.Note.Contains("10000c"));
            }
            finally
            {
                DestroyFixture(fixture);
            }
        }

        [Test]
        public void Wants_NoStove_RanksSecond()
        {
            ContentsFixture fixture = null;
            try
            {
                fixture = MakeContentsFixture();
                InstallSleepingBed(fixture, "bed-1");
                var members = new List<int> { 1 };
                var inputs = BaseInputs(fixture.Register, members, 20000);
                inputs.Sleeping = new SleepingPlaceRegistry
                {
                    HouseholdId = HouseholdId, Furnishings = fixture.Register, HomeBuildingInstanceId = BuildingInstanceId,
                };

                List<FurnishingWant> wants = HouseholdFurnishingEvaluator.EvaluateWants(inputs);
                Assert.AreEqual(1, wants.Count);
                Assert.AreEqual("furniture.cooking-stove", wants[0].ItemKind);
                Assert.AreEqual(FurnishingWantReason.StoveForNoStove, wants[0].Reason);
                Assert.AreEqual(2, wants[0].Priority);
                Assert.AreEqual(1, wants[0].PersonsAffected);
            }
            finally
            {
                DestroyFixture(fixture);
            }
        }

        [Test]
        public void Wants_ChairsWithoutTable_RanksTableBeforeChair()
        {
            ContentsFixture fixture = null;
            try
            {
                fixture = MakeContentsFixture();
                InstallSleepingBed(fixture, "bed-1");
                InstallSleepingBed(fixture, "bed-2");
                InstallStove(fixture);
                InstallDiningSet(fixture, chairCount: 2, withTable: false);
                var members = new List<int> { 1, 2 };
                var inputs = BaseInputs(fixture.Register, members, 20000);
                inputs.Sleeping = new SleepingPlaceRegistry
                {
                    HouseholdId = HouseholdId, Furnishings = fixture.Register, HomeBuildingInstanceId = BuildingInstanceId,
                };
                inputs.Dining = new DiningPlaceRegistry
                {
                    HouseholdId = HouseholdId, Furnishings = fixture.Register, HomeBuildingInstanceId = BuildingInstanceId,
                };

                List<FurnishingWant> wants = HouseholdFurnishingEvaluator.EvaluateWants(inputs);
                Assert.AreEqual(1, wants.Count, "the table is the root cause — two owned chairs need no third.");
                Assert.AreEqual("furniture.table", wants[0].ItemKind);
                Assert.AreEqual(FurnishingWantReason.TableForChairsWithoutTable, wants[0].Reason);
                Assert.AreEqual(3, wants[0].Priority);
            }
            finally
            {
                DestroyFixture(fixture);
            }
        }

        [Test]
        public void Wants_TableExistsFallbackDiners_RanksChair()
        {
            ContentsFixture fixture = null;
            try
            {
                fixture = MakeContentsFixture();
                InstallSleepingBed(fixture, "bed-1");
                InstallSleepingBed(fixture, "bed-2");
                InstallStove(fixture);
                InstallDiningSet(fixture, chairCount: 1, withTable: true);
                var members = new List<int> { 1, 2 };
                var inputs = BaseInputs(fixture.Register, members, 20000);
                inputs.Sleeping = new SleepingPlaceRegistry
                {
                    HouseholdId = HouseholdId, Furnishings = fixture.Register, HomeBuildingInstanceId = BuildingInstanceId,
                };
                inputs.Dining = new DiningPlaceRegistry
                {
                    HouseholdId = HouseholdId, Furnishings = fixture.Register, HomeBuildingInstanceId = BuildingInstanceId,
                };

                List<FurnishingWant> wants = HouseholdFurnishingEvaluator.EvaluateWants(inputs);
                Assert.AreEqual(1, wants.Count);
                Assert.AreEqual("furniture.chair", wants[0].ItemKind);
                Assert.AreEqual(FurnishingWantReason.ChairForFallbackDiner, wants[0].Reason);
                Assert.AreEqual(3, wants[0].Priority);
                Assert.AreEqual(1, wants[0].PersonsAffected, "one of two members falls back.");
            }
            finally
            {
                DestroyFixture(fixture);
            }
        }

        [Test]
        public void Wants_NoStorage_WhenLotsOnHand()
        {
            ContentsFixture fixture = null;
            try
            {
                fixture = MakeContentsFixture();
                InstallSleepingBed(fixture, "bed-1");
                InstallStove(fixture);
                var members = new List<int> { 1 };
                var inputs = BaseInputs(fixture.Register, members, 20000);
                inputs.Sleeping = new SleepingPlaceRegistry
                {
                    HouseholdId = HouseholdId, Furnishings = fixture.Register, HomeBuildingInstanceId = BuildingInstanceId,
                };
                inputs.HasStorageZone = false;
                inputs.StoredLotUnits = 5;

                List<FurnishingWant> wants = HouseholdFurnishingEvaluator.EvaluateWants(inputs);
                Assert.AreEqual(1, wants.Count);
                Assert.AreEqual("furniture.cabinet-small", wants[0].ItemKind);
                Assert.AreEqual(FurnishingWantReason.CabinetForNoStorage, wants[0].Reason);
                Assert.AreEqual(5, wants[0].Priority);

                inputs.StoredLotUnits = 0;
                Assert.AreEqual(0, HouseholdFurnishingEvaluator.EvaluateWants(inputs).Count,
                    "nothing stored, nothing to store it in — no storage want.");
            }
            finally
            {
                DestroyFixture(fixture);
            }
        }

        [Test]
        public void Wants_AlreadyOwned_NeverRebought()
        {
            ContentsFixture fixture = null;
            try
            {
                fixture = MakeContentsFixture();
                InstallSleepingBed(fixture, "bed-1");
                InstallSleepingBed(fixture, "bed-2");
                InstallStove(fixture);
                InstallDiningSet(fixture, chairCount: 2, withTable: true);
                var members = new List<int> { 1, 2 };
                var inputs = BaseInputs(fixture.Register, members, 20000);
                inputs.Sleeping = new SleepingPlaceRegistry
                {
                    HouseholdId = HouseholdId, Furnishings = fixture.Register, HomeBuildingInstanceId = BuildingInstanceId,
                };
                inputs.Dining = new DiningPlaceRegistry
                {
                    HouseholdId = HouseholdId, Furnishings = fixture.Register, HomeBuildingInstanceId = BuildingInstanceId,
                };
                inputs.HasStorageZone = true;

                Assert.AreEqual(0, HouseholdFurnishingEvaluator.EvaluateWants(inputs).Count);
            }
            finally
            {
                DestroyFixture(fixture);
            }
        }

        [Test]
        public void Wants_Unpriced_WhenNoPriceData()
        {
            ContentsFixture fixture = null;
            try
            {
                fixture = MakeContentsFixture();
                var members = new List<int> { 1, 2 };
                var inputs = BaseInputs(fixture.Register, members, 20000);
                inputs.ReplacementCostCentsForKind = null;
                inputs.Sleeping = new SleepingPlaceRegistry
                {
                    HouseholdId = HouseholdId, Furnishings = fixture.Register, HomeBuildingInstanceId = BuildingInstanceId,
                };

                List<FurnishingWant> wants = HouseholdFurnishingEvaluator.EvaluateWants(inputs);
                FurnishingWant bed = wants.Find(w => w.ItemKind == "furniture.bed");
                Assert.NotNull(bed);
                Assert.AreEqual(-1, bed.EstimatedCostCents);
                Assert.IsFalse(bed.Affordable);
                Assert.AreEqual(FurnishingWantChannel.Secondhand, bed.Channel);
            }
            finally
            {
                DestroyFixture(fixture);
            }
        }

        // ----------------------------------------------------------------
        // Evaluator: resale
        // ----------------------------------------------------------------

        [Test]
        public void Resale_SurplusBed_Listed()
        {
            ContentsFixture fixture = null;
            try
            {
                fixture = MakeContentsFixture();
                InstallSleepingBed(fixture, "bed-1");
                InstallSleepingBed(fixture, "bed-2");
                InstallSleepingBed(fixture, "bed-3");

                List<FurnishingResaleLine> lines = HouseholdFurnishingEvaluator.EvaluateResale(fixture.Register, 2, PriceForKind);
                Assert.AreEqual(1, lines.Count);
                Assert.AreEqual("bed-3", lines[0].AssetId);
                Assert.AreEqual(FurnishingResaleReason.Surplus, lines[0].Reason);
                Assert.AreEqual(10000, lines[0].GuidanceValueCents);
            }
            finally
            {
                DestroyFixture(fixture);
            }
        }

        [Test]
        public void Resale_Wrecked_ListedAsSalvage()
        {
            ContentsFixture fixture = null;
            try
            {
                fixture = MakeContentsFixture();
                InstallSleepingBed(fixture, "bed-1", 0.05f);

                List<FurnishingResaleLine> lines = HouseholdFurnishingEvaluator.EvaluateResale(fixture.Register, 2, PriceForKind);
                Assert.AreEqual(1, lines.Count);
                Assert.AreEqual("bed-1", lines[0].AssetId);
                Assert.AreEqual(FurnishingResaleReason.Wrecked, lines[0].Reason);
                Assert.AreEqual(0, lines[0].GuidanceValueCents, "a wrecked asset contributes nothing (Canon 11.6).");
            }
            finally
            {
                DestroyFixture(fixture);
            }
        }

        [Test]
        public void Resale_CustodySurplus_Listed()
        {
            ContentsFixture fixture = null;
            try
            {
                fixture = MakeContentsFixture();
                InstallSleepingBed(fixture, "bed-1");
                InstallSleepingBed(fixture, "bed-2");
                Assert.IsNull(fixture.Register.TryRegisterAsset(MakeAsset("bed-3", "furniture.bed")));

                List<FurnishingResaleLine> lines = HouseholdFurnishingEvaluator.EvaluateResale(fixture.Register, 2, PriceForKind);
                Assert.AreEqual(1, lines.Count);
                Assert.AreEqual("bed-3", lines[0].AssetId);
                Assert.AreEqual(FurnishingResaleReason.UnusedInCustody, lines[0].Reason);
            }
            finally
            {
                DestroyFixture(fixture);
            }
        }

        [Test]
        public void Resale_NothingSurplus_Empty()
        {
            ContentsFixture fixture = null;
            try
            {
                fixture = MakeContentsFixture();
                InstallSleepingBed(fixture, "bed-1");
                InstallSleepingBed(fixture, "bed-2");
                InstallDiningSet(fixture, chairCount: 2, withTable: true);

                Assert.AreEqual(0, HouseholdFurnishingEvaluator.EvaluateResale(fixture.Register, 2, PriceForKind).Count);
            }
            finally
            {
                DestroyFixture(fixture);
            }
        }

        [Test]
        public void Resale_Guidance_UsesConditionWeightedValue()
        {
            ContentsFixture fixture = null;
            try
            {
                fixture = MakeContentsFixture();
                InstallDiningSet(fixture, chairCount: 3, withTable: true);
                fixture.Register.FindAsset("chair-3").Condition01 = 0.5f;

                List<FurnishingResaleLine> lines = HouseholdFurnishingEvaluator.EvaluateResale(fixture.Register, 2, PriceForKind);
                Assert.AreEqual(1, lines.Count);
                Assert.AreEqual("chair-3", lines[0].AssetId);
                Assert.AreEqual(FurnishingResaleReason.Surplus, lines[0].Reason);
                Assert.AreEqual(1500, lines[0].GuidanceValueCents, "3000c x 0.5 condition.");
            }
            finally
            {
                DestroyFixture(fixture);
            }
        }
    }
}
