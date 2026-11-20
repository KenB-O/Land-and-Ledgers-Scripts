using System.Collections.Generic;
using System.Reflection;
using LandLedgers.Economy.Blacksmith;
using LandLedgers.Orchestration;
using LandLedgers.Orchestration.Systems;
using LandLedgers.Population;
using LandLedgers.World;
using NUnit.Framework;
using UnityEngine;

namespace LandLedgers.EditorTests.Population
{
    public sealed class FurnishingPresentationCalibrationTests
    {
        [Test]
        public void NeutralVisualMultiplier_PreservesPrefabAuthoredRootScale()
        {
            Vector3 authored = new(0.76347f, 0.76347f, 0.76347f);
            Vector3 result = FurnishingPresentationCalibration.ApplyScale(authored, Vector3.one);
            Assert.That(result.x, Is.EqualTo(authored.x).Within(0.00001f));
            Assert.That(result.y, Is.EqualTo(authored.y).Within(0.00001f));
            Assert.That(result.z, Is.EqualTo(authored.z).Within(0.00001f));
        }

        [Test]
        public void VisualMultiplier_IsAppliedExactlyOnceToEachPrefabAxis()
        {
            Vector3 result = FurnishingPresentationCalibration.ApplyScale(
                new Vector3(0.75f, 0.8f, 1f), new Vector3(1f, 0.5f, 2f));
            Assert.That(result, Is.EqualTo(new Vector3(0.75f, 0.4f, 2f)));
        }

        [Test]
        public void FloorAnchoredFurniture_UsesAuthoredFloorElevation()
        {
            Vector3 authored = new(1.8f, 1.49f, 0.4f);
            Vector3 grounded = FurnishingPresentationCalibration.AlignToFloor(authored, 0.8f, true);
            Assert.That(grounded.x, Is.EqualTo(1.8f));
            Assert.That(grounded.y, Is.EqualTo(0.8f));
            Assert.That(grounded.z, Is.EqualTo(0.4f));
            Assert.That(FurnishingPresentationCalibration.AlignToFloor(authored, 0.8f, false), Is.EqualTo(authored));
        }

        [Test]
        public void RenderedBottomAlignment_UsesMeshBoundsInsteadOfPrefabPivot()
        {
            GameObject building = new("rendered-floor-test-building");
            GameObject prop = GameObject.CreatePrimitive(PrimitiveType.Cube);
            try
            {
                prop.transform.SetParent(building.transform, false);
                prop.transform.localPosition = new Vector3(1f, -0.4f, 2f);
                prop.transform.localScale = new Vector3(1f, 0.5f, 1f);
                Assert.IsTrue(FurnishingPresentationCalibration.AlignRenderedBottomToFloor(prop.transform, 0.8f));
                Assert.That(prop.GetComponent<Renderer>().bounds.min.y, Is.EqualTo(0.8f).Within(0.001f));
            }
            finally
            {
                Object.DestroyImmediate(prop);
                Object.DestroyImmediate(building);
            }
        }

        [Test]
        public void ChairCalibration_FacesPrefabBackAxisTowardDiningTable()
        {
            Vector3 expected = Vector3.right;
            Quaternion rotation = FurnishingPresentationCalibration.FaceDirection(Vector3.back, expected, Quaternion.identity);
            Assert.That(Vector3.Dot(rotation * Vector3.back, expected), Is.GreaterThan(0.999f));
        }

        [Test]
        public void CalibratedFurnitureFootprint_BlocksOnlyItsRotatedLocalBounds()
        {
            GameObject buildingObject = new("furniture-footprint-test-building");
            GameObject fixtureObject = new("furniture-footprint-test-chair");
            try
            {
                BuildingFootprintAuthority building = buildingObject.AddComponent<BuildingFootprintAuthority>();
                fixtureObject.transform.SetParent(buildingObject.transform, false);
                fixtureObject.transform.localPosition = new Vector3(2f, 0f, 3f);
                fixtureObject.transform.localRotation = Quaternion.Euler(0f, 90f, 0f);
                BuildingEquipmentPlacement placement = fixtureObject.AddComponent<BuildingEquipmentPlacement>();
                placement.Configure("test-chair", "furniture.chair", "Test chair", 0);
                placement.ConfigureNavigationFootprint(true, Vector2.zero, new Vector2(2f, 1f));
                placement.AssociateWithBuilding(building);

                Vector3 inside = fixtureObject.transform.TransformPoint(new Vector3(0.9f, 0f, 0f));
                Vector3 outside = fixtureObject.transform.TransformPoint(new Vector3(1.25f, 0f, 0f));
                Assert.IsTrue(placement.BlocksInteriorPoint(building, inside, 0));
                Assert.IsFalse(placement.BlocksInteriorPoint(building, outside, 0));
                Assert.IsFalse(placement.BlocksInteriorPoint(building, inside, 1));
            }
            finally
            {
                Object.DestroyImmediate(fixtureObject);
                Object.DestroyImmediate(buildingObject);
            }
        }

        [TestCase("bed", FurnishingCapabilityKind.SleepingPlace, BuildingActivityLocationType.Bed)]
        [TestCase("dining-chair", FurnishingCapabilityKind.DiningSeat, BuildingActivityLocationType.Dining)]
        public void RestoringPresentationsAfterWorldRebuild_RecreatesReachableActivityWithoutDuplicatingOwnership(
            string furnishingKind, FurnishingCapabilityKind capability, BuildingActivityLocationType activityType)
        {
            const int householdId = 1;
            const int buildingId = 7;
            string assetId = "home-1-" + furnishingKind + "-a";
            string activityId = furnishingKind + "-activity-a";

            GameObject worldObject = new("furnishing-restore-world");
            GameObject populationObject = new("furnishing-restore-population");
            GameObject hubObject = new("furnishing-restore-systems");
            GameObject buildingObject = new("furnishing-restore-building");
            GameObject prefab = GameObject.CreatePrimitive(PrimitiveType.Cube);
            DevelopmentTownConfiguration configuration = ScriptableObject.CreateInstance<DevelopmentTownConfiguration>();
            TownGenerationSettings settings = ScriptableObject.CreateInstance<TownGenerationSettings>();
            try
            {
                TownWorldController townWorld = worldObject.AddComponent<TownWorldController>();
                settings.seed = 1886;
                SetPrivateField(townWorld, "settings", settings);

                BuildingFootprintAuthority authority = buildingObject.AddComponent<BuildingFootprintAuthority>();
                SetPrivateField(authority, "interiorFloors", new List<InteriorFloorAuthoring>
                {
                    new()
                    {
                        floorIndex = 0,
                        floorHeightLocal = 0f,
                        walkableAreas = new List<InteriorPolygonAuthoring>
                        {
                            new()
                            {
                                vertices = new List<Vector3>
                                {
                                    new(-2f, 0f, -2f), new(6f, 0f, -2f),
                                    new(6f, 0f, 6f), new(-2f, 0f, 6f),
                                },
                            },
                        },
                    },
                });
                SetPrivateField(authority, "entrancePairs", new List<BuildingEntrancePairAuthoring>
                {
                    new()
                    {
                        id = "main-entrance",
                        indoorPositionLocal = new Vector3(0f, 0f, -1f),
                        outdoorApproachLocal = new Vector3(0f, 0f, -2f),
                        floorIndex = 0,
                        clearWidthMeters = 1.1f,
                    },
                });

                FieldInfo buildingsField = typeof(TownWorldController).GetField("buildings", BindingFlags.Instance | BindingFlags.NonPublic);
                Assert.IsNotNull(buildingsField);
                ((List<PlacedBuilding>)buildingsField.GetValue(townWorld)).Add(
                    new PlacedBuilding { id = buildingId, runtimeFootprintAuthority = authority });

                PopulationManager population = populationObject.AddComponent<PopulationManager>();
                SetPrivateField(population, "state", new PopulationState
                {
                    households = new List<HouseholdState>
                    {
                        new() { id = householdId, homeBuildingId = buildingId },
                    },
                });

                SimulationSystemsHub systemsHub = hubObject.AddComponent<SimulationSystemsHub>();
                HouseholdFurnishingRegister register = systemsHub.FurnishingRegisterFor(householdId);
                register.Assets.Add(new EquipmentAsset
                {
                    AssetId = assetId,
                    Kind = furnishingKind,
                    DisplayName = furnishingKind,
                    OwnerKind = HouseholdFurnishingRegister.OwnerKindHousehold,
                    OwnerId = HouseholdFurnishingRegister.OwnerIdForHousehold(householdId),
                    LocationId = HouseholdFurnishingRegister.BuildingLocationId(buildingId),
                });
                register.Bindings.Add(new FurnishingPlacementBinding
                {
                    BindingId = "binding-" + assetId,
                    AssetId = assetId,
                    BuildingInstanceId = buildingId,
                    FloorIndex = 0,
                    LocalPosition = new Vector3(2f, 0f, 2f),
                    LocalRotation = Quaternion.identity,
                    DefinitionId = "furniture." + furnishingKind,
                    CapabilityKind = capability,
                    CapabilityReferenceId = activityId,
                    AcquisitionSource = FurnishingAcquisitionSource.ScenarioEndowment,
                    AcquisitionSourceId = "scenario-endowment-test",
                });

                var item = new DevelopmentTownConfiguration.HouseholdFurnishingItem
                {
                    assetId = assetId,
                    kind = furnishingKind,
                    displayName = furnishingKind,
                    prefab = prefab,
                    floorIndex = 0,
                    capability = capability,
                    activityLocationId = activityId,
                    activityPositionLocal = new Vector3(3f, 0f, 2f),
                    activityForwardLocal = Vector3.forward,
                    activityType = activityType,
                    registerRuntimeActivityLocation = true,
                };
                SetPrivateField(configuration, "households", new[]
                {
                    new DevelopmentTownConfiguration.HouseholdDefinition
                    {
                        key = "test-household",
                        runtimeHouseholdId = householdId,
                        openingFurnishingVariants = new[]
                        {
                            new DevelopmentTownConfiguration.HouseholdFurnishingVariant
                            {
                                variantId = "test",
                                items = new[] { item },
                            },
                        },
                    },
                });

                DevelopmentTownFurnishingInitializer.Result first = DevelopmentTownFurnishingInitializer.RestorePresentations(
                    configuration, population, townWorld, systemsHub);

                Assert.AreEqual(1, first.PresentationsRebuilt);
                Assert.IsTrue(authority.TryGetActivityLocation(activityId, buildingId,
                    out ResolvedBuildingActivityLocation restoredLocation, out string locationError), locationError);
                Assert.That(Vector3.Distance(restoredLocation.WorldPosition,
                    authority.transform.TransformPoint(item.activityPositionLocal)), Is.LessThan(0.001f));
                Assert.IsTrue(authority.TryBuildInteriorActivityRoute("main-entrance", activityId,
                    out List<Vector3> waypoints, out string routeError), routeError);
                Assert.IsNotEmpty(waypoints);
                Assert.AreEqual(1, register.Assets.Count);
                Assert.AreEqual(1, register.Bindings.Count);
                Assert.AreEqual(HouseholdFurnishingRegister.BuildingLocationId(buildingId), register.FindAsset(assetId).LocationId);

                DevelopmentTownFurnishingInitializer.Result second = DevelopmentTownFurnishingInitializer.RestorePresentations(
                    configuration, population, townWorld, systemsHub);

                Assert.AreEqual(0, second.PresentationsRebuilt, "A repeated rebuild callback must not duplicate the presentation.");
                Assert.AreEqual(1, authority.GetComponentsInChildren<HouseholdFurnishingRuntimePresentation>(true).Length);
                Assert.AreEqual(1, register.Assets.Count, "Restoration must preserve the single owned equipment identity.");
                Assert.AreEqual(1, register.Bindings.Count, "Restoration must preserve the single installation binding.");
                Assert.IsTrue(authority.TryBuildInteriorActivityRoute("main-entrance", activityId, out _, out routeError), routeError);

                // Runtime activity locations are intentionally nonserialized.
                // Simulate a domain/scene restoration that retains the
                // presentation marker but loses the authority's runtime map.
                Assert.IsTrue(authority.UnregisterRuntimeActivityLocation(activityId));
                DevelopmentTownFurnishingInitializer.Result rebound = DevelopmentTownFurnishingInitializer.RestorePresentations(
                    configuration, population, townWorld, systemsHub);
                Assert.AreEqual(0, rebound.PresentationsRebuilt,
                    "Rebinding an existing visual must not create a duplicate presentation.");
                Assert.IsTrue(authority.TryGetActivityLocation(activityId, buildingId,
                    out restoredLocation, out locationError), locationError);
                Assert.AreEqual(1, authority.GetComponentsInChildren<HouseholdFurnishingRuntimePresentation>(true).Length);
                Assert.AreEqual(1, register.Assets.Count, "Activity restoration must not duplicate owned equipment.");
                Assert.AreEqual(1, register.Bindings.Count, "Activity restoration must not duplicate installation bindings.");
                Assert.IsTrue(authority.TryBuildInteriorActivityRoute("main-entrance", activityId,
                    out waypoints, out routeError), routeError);
                Assert.IsNotEmpty(waypoints);
            }
            finally
            {
                Object.DestroyImmediate(worldObject);
                Object.DestroyImmediate(populationObject);
                Object.DestroyImmediate(hubObject);
                Object.DestroyImmediate(buildingObject);
                Object.DestroyImmediate(prefab);
                Object.DestroyImmediate(configuration);
                Object.DestroyImmediate(settings);
            }
        }

        private static void SetPrivateField(object target, string fieldName, object value)
        {
            FieldInfo field = target.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(field, $"Expected private field '{fieldName}' on {target.GetType().Name}.");
            field.SetValue(target, value);
        }
    }
}
