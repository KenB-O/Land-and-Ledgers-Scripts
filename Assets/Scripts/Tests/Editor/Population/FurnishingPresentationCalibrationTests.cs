using LandLedgers.Orchestration;
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
    }
}
