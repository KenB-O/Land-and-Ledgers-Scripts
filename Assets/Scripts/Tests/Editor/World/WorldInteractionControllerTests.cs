using LandLedgers.World;
using NUnit.Framework;
using UnityEngine;

namespace LandLedgers.Tests.World
{
    public sealed class WorldInteractionControllerTests
    {
        [Test]
        public void RaycastResolvesChildColliderToAuthoritativeInspectableTarget()
        {
            GameObject controllerObject = new("World Interaction Controller Test");
            GameObject root = new("Inspectable Root");
            GameObject child = GameObject.CreatePrimitive(PrimitiveType.Cube);
            try
            {
                child.name = "Inspectable Child Collider";
                child.transform.SetParent(root.transform, false);
                root.transform.position = Vector3.zero;
                WorldInspectableTarget target = root.AddComponent<WorldInspectableTarget>();
                target.Configure(WorldInspectableTargetKind.Building, 12, 34);

                WorldInteractionController controller = controllerObject.AddComponent<WorldInteractionController>();
                Physics.SyncTransforms();

                Ray ray = new(new Vector3(0f, 0f, -8f), Vector3.forward);
                Assert.IsTrue(controller.TryResolveTarget(ray, out WorldInspectableTarget resolved));
                Assert.AreSame(target, resolved);
                Assert.AreEqual(WorldInspectableTargetKind.Building, resolved.Kind);
                Assert.AreEqual(12, resolved.PlotId);
                Assert.AreEqual(34, resolved.BuildingId);
            }
            finally
            {
                Object.DestroyImmediate(child);
                Object.DestroyImmediate(root);
                Object.DestroyImmediate(controllerObject);
            }
        }

        [TestCase(false, false)]
        [TestCase(true, false)]
        [TestCase(false, true)]
        public void VisiblePersonBeatsFootprintProxyButNotSolidWall(bool wall, bool crowded)
        {
            GameObject controllerObject = new("Selection Controller");
            GameObject proxy = GameObject.CreatePrimitive(PrimitiveType.Cube);
            GameObject person = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            GameObject obstruction = null;
            var extras = new System.Collections.Generic.List<GameObject>();
            try
            {
                proxy.transform.position = new Vector3(0, 0, 2);
                proxy.GetComponent<Collider>().isTrigger = true;
                var building = proxy.AddComponent<WorldInspectableTarget>();
                building.Configure(WorldInspectableTargetKind.Building, 1, 2);
                person.transform.position = new Vector3(0, 0, 4);
                person.GetComponent<Collider>().isTrigger = true;
                var npc = person.AddComponent<WorldInspectableTarget>();
                npc.ConfigurePerson(3);
                if (wall)
                {
                    obstruction = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    obstruction.transform.position = new Vector3(0, 0, 3);
                }
                if (crowded)
                {
                    for (int i = 0; i < 40; i++)
                    {
                        var extra = GameObject.CreatePrimitive(PrimitiveType.Cube);
                        extra.transform.position = new Vector3(0, 0, 6 + i);
                        extra.GetComponent<Collider>().isTrigger = true;
                        extras.Add(extra);
                    }
                }
                var controller = controllerObject.AddComponent<WorldInteractionController>();
                Physics.SyncTransforms();
                Assert.IsTrue(controller.TryResolveTarget(new Ray(Vector3.zero, Vector3.forward), out var selected));
                Assert.AreSame(wall ? building : npc, selected);
            }
            finally
            {
                foreach (var extra in extras) Object.DestroyImmediate(extra);
                if (obstruction != null) Object.DestroyImmediate(obstruction);
                Object.DestroyImmediate(person);
                Object.DestroyImmediate(proxy);
                Object.DestroyImmediate(controllerObject);
            }
        }

        [Test]
        public void PersonInspectableTargetRetainsStablePersonIdAndClearsItForOtherKinds()
        {
            GameObject targetObject = new("Person Inspectable Target Test");
            try
            {
                WorldInspectableTarget target = targetObject.AddComponent<WorldInspectableTarget>();
                target.ConfigurePerson(731);

                Assert.AreEqual(WorldInspectableTargetKind.Person, target.Kind);
                Assert.AreEqual(731, target.PersonId);
                Assert.AreEqual(-1, target.PlotId);
                Assert.AreEqual(-1, target.BuildingId);

                target.Configure(WorldInspectableTargetKind.Building, 12, 34);
                Assert.AreEqual(-1, target.PersonId, "Reusing a target for a non-person must not leave a stale identity.");
            }
            finally
            {
                Object.DestroyImmediate(targetObject);
            }
        }
    }
}
