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
    }
}
