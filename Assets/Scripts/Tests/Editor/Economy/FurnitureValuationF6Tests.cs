using System.Collections.Generic;
using System.Reflection;
using LandLedgers.Economy.Blacksmith;
using LandLedgers.Economy.Equipment;
using LandLedgers.Population;
using LandLedgers.Primitives;
using LandLedgers.World;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

namespace LandLedgers.Editor.Economy
{
    /// <summary>
    /// F6 (Agent F, expanded campaign 2026-11-16): property-contents
    /// valuation + the fixture/structure split D-6/D-7 consume.
    ///
    /// Replacement values are F2's authored retail baselines (verified
    /// against GeneralStoreBusinessDefinition.asset on disk: bed 300,
    /// table 225, chair 125, cabinet-small 325, cabinet-large 425,
    /// shelf 80, crate 75, bedroll 60). Used value is condition-weighted
    /// replacement (Canon 11.6), implemented by delegating to B6's
    /// ValueCentsFor so the selection's agreed values and F6's used values
    /// agree by construction. The split returns structure-only = the
    /// as-standing structural estimate minus the agreed value of included
    /// fixtures; movables never enter the structural number; the result is
    /// a negotiation anchor, never an agreed price.
    ///
    /// Two plan corrections are pinned here, read against the real code:
    /// there is no computed salvage floor (UsedEquipmentSalvage records
    /// negotiated scrap prices only — a wrecked asset values at 0c, never
    /// negative), and there is no age adjustment (no depreciation schedule
    /// exists — two assets of the same kind and condition value alike
    /// whatever their MadeDayIndex).
    /// </summary>
    [TestFixture]
    public sealed class FurnitureValuationF6Tests
    {
        private const int SellerHouseholdId = 7;
        private const int BuildingInstanceId = 101;
        private const int Day = 100;
        private const int StructuralEstimateCents = 25600;

        private static HouseholdFurnishingRegister MakeRegister(int householdId = SellerHouseholdId)
        {
            return new HouseholdFurnishingRegister { HouseholdId = householdId };
        }

        private static EquipmentAsset MakeAsset(
            string assetId, string kind, float condition = 1f,
            int ownerId = SellerHouseholdId, int madeDayIndex = 0)
        {
            return new EquipmentAsset
            {
                AssetId = assetId,
                Kind = kind,
                DisplayName = kind,
                Condition01 = condition,
                OwnerKind = "household",
                OwnerId = ownerId.ToString(),
                MadeDayIndex = madeDayIndex,
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

        private static List<FurnishingContentsItem> Eligible(HouseholdFurnishingRegister register)
        {
            return PropertyContentsSelection.ListEligibleContents(register, BuildingInstanceId);
        }

        private static PropertyContentsSelection BuildAllFor(HouseholdFurnishingRegister register)
        {
            return PropertyContentsSelection.BuildAll(Eligible(register), "household", SellerHouseholdId.ToString(),
                BuildingInstanceId, Day, FurnitureValuation.ReplacementCostCentsForKind);
        }

        [Test]
        public void ReplacementCost_AllEightCarpentryKinds_MatchF2Baselines()
        {
            Assert.AreEqual(300, FurnitureValuation.ReplacementCostCentsForKind("furniture.bed"));
            Assert.AreEqual(225, FurnitureValuation.ReplacementCostCentsForKind("furniture.table"));
            Assert.AreEqual(125, FurnitureValuation.ReplacementCostCentsForKind("furniture.chair"));
            Assert.AreEqual(325, FurnitureValuation.ReplacementCostCentsForKind("furniture.cabinet-small"));
            Assert.AreEqual(425, FurnitureValuation.ReplacementCostCentsForKind("furniture.cabinet-large"));
            Assert.AreEqual(80, FurnitureValuation.ReplacementCostCentsForKind("furniture.shelf"));
            Assert.AreEqual(75, FurnitureValuation.ReplacementCostCentsForKind("furniture.crate"));
            Assert.AreEqual(60, FurnitureValuation.ReplacementCostCentsForKind("furniture.bedroll"));
        }

        [Test]
        public void ReplacementCost_StoveCounterBarrelUnknown_AreUnpriced_NeverGuessed()
        {
            // Iron, built-in and cooperage kinds have no legitimate price
            // yet (out of the carpentry wave) — unpriced, never invented.
            Assert.AreEqual(0, FurnitureValuation.ReplacementCostCentsForKind("furniture.cooking-stove"));
            Assert.AreEqual(0, FurnitureValuation.ReplacementCostCentsForKind("furniture.kitchen-counter"));
            Assert.AreEqual(0, FurnitureValuation.ReplacementCostCentsForKind("furniture.barrel"));
            Assert.AreEqual(0, FurnitureValuation.ReplacementCostCentsForKind("furniture.wardrobe"));
            Assert.AreEqual(0, FurnitureValuation.ReplacementCostCentsForKind(null));
            Assert.AreEqual(0, FurnitureValuation.ReplacementCostCentsForKind(string.Empty));
            Assert.AreEqual(0, FurnitureValuation.ReplacementValueCents(null));
        }

        [Test]
        public void UsedValue_BedAtFullCondition_ValuesAtReplacement()
        {
            EquipmentAsset bed = MakeAsset("bed-1", "furniture.bed", 1f);
            Assert.AreEqual(300, FurnitureValuation.ReplacementValueCents(bed));
            Assert.AreEqual(300, FurnitureValuation.UsedValueCents(bed));
            Assert.AreEqual(300, FurnitureValuation.UsedValueCents("furniture.bed", 1f));
        }

        [Test]
        public void UsedValue_BedAtHalfCondition_ValuesAtHalf()
        {
            EquipmentAsset bed = MakeAsset("bed-1", "furniture.bed", 0.5f);
            Assert.AreEqual(150, FurnitureValuation.UsedValueCents(bed));
            Assert.AreEqual(150, FurnitureValuation.UsedValueCents("furniture.bed", 0.5f));
        }

        [Test]
        public void UsedValue_HalfCondition_BankersRoundingMatchesB6()
        {
            // Math.Round is half-to-even (the same call B6's ValueCentsFor
            // makes): 225 x 0.5 = 112.5 rounds to 112, 125 x 0.5 = 62.5 to 62.
            Assert.AreEqual(112, FurnitureValuation.UsedValueCents("furniture.table", 0.5f));
            Assert.AreEqual(62, FurnitureValuation.UsedValueCents("furniture.chair", 0.5f));
        }

        [Test]
        public void UsedValue_WreckedAsset_ValuesZero_NeverNegative()
        {
            // No computed salvage floor exists (UsedEquipmentSalvage records
            // negotiated scrap prices only): a wrecked asset is 0c, and the
            // value never goes below zero for any condition.
            Assert.AreEqual(0, FurnitureValuation.UsedValueCents("furniture.bed", 0f));
            Assert.AreEqual(0, FurnitureValuation.UsedValueCents("furniture.bed", 0.05f));
            Assert.AreEqual(18, FurnitureValuation.UsedValueCents("furniture.bed", 0.06f));
            Assert.AreEqual(0, FurnitureValuation.UsedValueCents("furniture.bed", -0.5f));
            Assert.AreEqual(300, FurnitureValuation.UsedValueCents("furniture.bed", 1.5f));
            Assert.AreEqual(0, FurnitureValuation.UsedValueCents(MakeAsset("bed-1", "furniture.bed", 0f)));
            Assert.AreEqual(0, FurnitureValuation.UsedValueCents("furniture.cooking-stove", 1f));
        }

        [Test]
        public void UsedValue_NoAgeAdjustment_SameKindAndConditionValueAlike()
        {
            // No depreciation schedule exists in the codebase and none is
            // invented: age (MadeDayIndex) never moves the value.
            EquipmentAsset newBed = MakeAsset("bed-new", "furniture.bed", 1f, SellerHouseholdId, 0);
            EquipmentAsset oldBed = MakeAsset("bed-old", "furniture.bed", 1f, SellerHouseholdId, 500);
            Assert.AreEqual(FurnitureValuation.UsedValueCents(newBed), FurnitureValuation.UsedValueCents(oldBed));
            EquipmentAsset wornNew = MakeAsset("bed-new", "furniture.bed", 0.5f, SellerHouseholdId, 0);
            EquipmentAsset wornOld = MakeAsset("bed-old", "furniture.bed", 0.5f, SellerHouseholdId, 500);
            Assert.AreEqual(FurnitureValuation.UsedValueCents(wornNew), FurnitureValuation.UsedValueCents(wornOld));
        }

        [Test]
        public void UsedValue_MatchesB6ValueCentsFor_AtEveryKindAndCondition()
        {
            // F6 delegates to B6's ValueCentsFor with F6's price func, so the
            // selection's agreed values and F6's used values agree by
            // construction. This pins the agreement as a regression guard.
            string[] kinds =
            {
                "furniture.bed", "furniture.table", "furniture.chair",
                "furniture.cabinet-small", "furniture.cabinet-large",
                "furniture.shelf", "furniture.crate", "furniture.bedroll",
            };
            float[] conditions = { 0f, 0.25f, 0.5f, 0.75f, 1f };
            foreach (string kind in kinds)
            {
                foreach (float condition in conditions)
                {
                    int expected = PropertyContentsSelection.ValueCentsFor(
                        kind, condition, FurnitureValuation.ReplacementCostCentsForKind, out bool priced);
                    Assert.AreEqual(expected, FurnitureValuation.UsedValueCents(kind, condition),
                        $"F6 used value for {kind} at {condition} must equal B6's ValueCentsFor.");
                    Assert.IsTrue(priced || expected == 0, $"Unpriced kind {kind} must value 0c.");
                }
            }
        }

        [Test]
        public void Split_FixtureIncluded_PricesStructureLowerByExactlyTheFixtureValue()
        {
            HouseholdFurnishingRegister register = MakeFurnishedSellerRegister();
            PropertyContentsSelection all = BuildAllFor(register);
            // Bed 300 + table 225 + chair 125 + shelf 80 (fixture); the stove
            // is unpriced (0c) and recorded as unpriced, not valued.
            Assert.AreEqual(730, all.TotalContentsValueCents);
            Assert.AreEqual(80, all.IncludedFixtureValueCents);
            Assert.AreEqual(650, all.IncludedMovableValueCents);
            Assert.IsTrue(all.UnpricedIncludedAssetIds.Contains("stove-1"));

            StructureContentsSplit withFixture = FurnitureValuation.SplitStructureAndContents(StructuralEstimateCents, all);
            Assert.IsTrue(withFixture.IsValid, withFixture.Problem);
            Assert.AreEqual(25520, withFixture.StructureOnlyCents);

            // Same house, same estimate, the shelf excluded: the structure
            // prices exactly the fixture value higher.
            PropertyContentsSelection movablesOnly = PropertyContentsSelection.BuildCustom(
                Eligible(register), new[] { "bed-1", "table-1", "chair-1" },
                "household", SellerHouseholdId.ToString(), BuildingInstanceId, Day,
                FurnitureValuation.ReplacementCostCentsForKind);
            Assert.AreEqual(0, movablesOnly.IncludedFixtureValueCents);
            StructureContentsSplit withoutFixture = FurnitureValuation.SplitStructureAndContents(StructuralEstimateCents, movablesOnly);
            Assert.IsTrue(withoutFixture.IsValid, withoutFixture.Problem);
            Assert.AreEqual(25600, withoutFixture.StructureOnlyCents);
            Assert.AreEqual(
                withFixture.IncludedFixtureValueCents,
                withoutFixture.StructureOnlyCents - withFixture.StructureOnlyCents,
                "including an 80c fixture must price the structure exactly 80c lower.");

            // Nothing included at all: the structure is the estimate, untouched.
            PropertyContentsSelection nothing = PropertyContentsSelection.BuildCustom(
                Eligible(register), new string[0],
                "household", SellerHouseholdId.ToString(), BuildingInstanceId, Day,
                FurnitureValuation.ReplacementCostCentsForKind);
            StructureContentsSplit empty = FurnitureValuation.SplitStructureAndContents(StructuralEstimateCents, nothing);
            Assert.IsTrue(empty.IsValid, empty.Problem);
            Assert.AreEqual(25600, empty.StructureOnlyCents);
            Assert.AreEqual(0, empty.ContentsValueCents);
        }

        [Test]
        public void Split_MovablesNeverChangeTheStructuralNumber()
        {
            HouseholdFurnishingRegister register = MakeFurnishedSellerRegister();
            PropertyContentsSelection all = BuildAllFor(register);
            PropertyContentsSelection fixturesOnly = PropertyContentsSelection.BuildNoMovable(
                Eligible(register), "household", SellerHouseholdId.ToString(),
                BuildingInstanceId, Day, FurnitureValuation.ReplacementCostCentsForKind);
            // The two selections carry different contents totals (730c vs
            // 80c) but the same included fixtures (the 80c shelf).
            Assert.AreEqual(730, all.TotalContentsValueCents);
            Assert.AreEqual(80, fixturesOnly.TotalContentsValueCents);
            Assert.AreEqual(all.IncludedFixtureValueCents, fixturesOnly.IncludedFixtureValueCents);

            StructureContentsSplit withMovables = FurnitureValuation.SplitStructureAndContents(StructuralEstimateCents, all);
            StructureContentsSplit withoutMovables = FurnitureValuation.SplitStructureAndContents(StructuralEstimateCents, fixturesOnly);
            Assert.IsTrue(withMovables.IsValid, withMovables.Problem);
            Assert.IsTrue(withoutMovables.IsValid, withoutMovables.Problem);
            Assert.AreEqual(withoutMovables.StructureOnlyCents, withMovables.StructureOnlyCents,
                "movables must never move the structural number.");
            Assert.AreEqual(25520, withMovables.StructureOnlyCents);
        }

        [Test]
        public void Split_AnchorTotal_EqualsEstimatePlusIncludedMovables()
        {
            HouseholdFurnishingRegister register = MakeFurnishedSellerRegister();
            PropertyContentsSelection all = BuildAllFor(register);
            StructureContentsSplit split = FurnitureValuation.SplitStructureAndContents(StructuralEstimateCents, all);
            Assert.IsTrue(split.IsValid, split.Problem);
            Assert.AreEqual(split.StructureOnlyCents + split.ContentsValueCents, split.AnchorTotalCents);
            Assert.AreEqual(StructuralEstimateCents + split.IncludedMovableValueCents, split.AnchorTotalCents,
                "the anchor is the as-standing estimate plus the movables coming with the sale.");
            Assert.AreEqual(26250, split.AnchorTotalCents);
            Assert.IsTrue(split.Derivation.Contains("negotiation anchor"),
                "the derivation must say plainly that this is an anchor, not a price.");
        }

        [Test]
        public void Split_Conservation_PassesOnCorrectDeal_FailsOnMismatch()
        {
            // D-6's CheckConservation consumes the split's structure value:
            // price == structure-only + contents passes, a one-cent mismatch fails.
            HouseholdFurnishingRegister register = MakeFurnishedSellerRegister();
            PropertyContentsSelection all = BuildAllFor(register);
            StructureContentsSplit split = FurnitureValuation.SplitStructureAndContents(StructuralEstimateCents, all);
            Assert.IsTrue(split.IsValid, split.Problem);
            Assert.IsNull(PropertyContentsSelection.CheckConservation(
                split.StructureOnlyCents, split.ContentsValueCents, split.AnchorTotalCents));
            Assert.IsNotNull(PropertyContentsSelection.CheckConservation(
                split.StructureOnlyCents, split.ContentsValueCents, split.AnchorTotalCents + 1));
            Assert.IsNotNull(PropertyContentsSelection.CheckConservation(
                split.StructureOnlyCents, split.ContentsValueCents, split.AnchorTotalCents - 1));
            // The unsplit estimate does NOT conserve: it double-counts the fixture.
            Assert.IsNotNull(PropertyContentsSelection.CheckConservation(
                StructuralEstimateCents, split.ContentsValueCents, split.AnchorTotalCents));
        }

        [Test]
        public void Split_EstimateBelowFixtureValue_RefusedLoudly()
        {
            HouseholdFurnishingRegister register = MakeFurnishedSellerRegister();
            PropertyContentsSelection all = BuildAllFor(register);
            StructureContentsSplit split = FurnitureValuation.SplitStructureAndContents(50, all);
            Assert.IsFalse(split.IsValid);
            Assert.IsTrue(split.Problem.Contains("50"), "the refusal must name the estimate.");
            Assert.IsTrue(split.Problem.Contains("80"), "the refusal must name the fixture value.");
            Assert.AreEqual(0, split.StructureOnlyCents);
            Assert.AreEqual(0, split.AnchorTotalCents);
        }

        [Test]
        public void Split_NullSelection_AndNegativeEstimate_Refused()
        {
            StructureContentsSplit noSelection = FurnitureValuation.SplitStructureAndContents(StructuralEstimateCents, null);
            Assert.IsFalse(noSelection.IsValid);
            Assert.IsTrue(noSelection.Problem.Length > 0);
            HouseholdFurnishingRegister register = MakeFurnishedSellerRegister();
            StructureContentsSplit negative = FurnitureValuation.SplitStructureAndContents(-1, BuildAllFor(register));
            Assert.IsFalse(negative.IsValid);
            Assert.IsTrue(negative.Problem.Contains("-1"));
        }
    }
}
