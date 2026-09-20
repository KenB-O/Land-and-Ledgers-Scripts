using LandLedgers.Pathing;
using NUnit.Framework;
using System.Reflection;
using UnityEngine;
using UnityEngine.TestTools;

namespace LandLedgers.EditorTests.Pathing
{
    public sealed class AgentMoverTests
    {
        [Test]
        public void HideImmediatelyDisablesRenderersAndColliders()
        {
            GameObject agentObject = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            try
            {
                AgentMover mover = agentObject.AddComponent<AgentMover>();
                Renderer renderer = agentObject.GetComponent<Renderer>();
                Collider collider = agentObject.GetComponent<Collider>();
                renderer.enabled = true;
                collider.enabled = true;

                mover.HideImmediately();

                Assert.IsTrue(mover.IsHidden);
                Assert.AreEqual(0, mover.CurrentPath.Count);
                Assert.IsFalse(renderer.enabled);
                Assert.IsFalse(collider.enabled);
            }
            finally
            {
                Object.DestroyImmediate(agentObject);
            }
        }

        [Test]
        public void HideImmediatelyDisablesConfiguredVisualChild()
        {
            GameObject agentRoot = new("Agent Root");
            GameObject visualChild = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            try
            {
                visualChild.transform.SetParent(agentRoot.transform, false);
                AgentMover mover = agentRoot.AddComponent<AgentMover>();
                mover.ConfigureVisualRoot(visualChild.transform);

                Renderer renderer = visualChild.GetComponent<Renderer>();
                Collider collider = visualChild.GetComponent<Collider>();
                renderer.enabled = true;
                collider.enabled = true;

                mover.HideImmediately();

                Assert.IsTrue(mover.IsHidden);
                Assert.AreEqual(0, mover.CurrentPath.Count);
                Assert.IsFalse(renderer.enabled);
                Assert.IsFalse(collider.enabled);
            }
            finally
            {
                Object.DestroyImmediate(agentRoot);
            }
        }

        [Test]
        public void OnDisableSkipsAnimatorBlendWriteWhenControllerIsMissing()
        {
            GameObject agentRoot = new("Agent Root");
            try
            {
                Animator animator = agentRoot.AddComponent<Animator>();
                AgentMover mover = agentRoot.AddComponent<AgentMover>();

                typeof(AgentMover)
                    .GetField("cachedAnimator", BindingFlags.Instance | BindingFlags.NonPublic)
                    .SetValue(mover, animator);
                typeof(AgentMover)
                    .GetField("animatorHasBlendParameter", BindingFlags.Instance | BindingFlags.NonPublic)
                    .SetValue(mover, true);

                agentRoot.SetActive(false);

                LogAssert.NoUnexpectedReceived();
            }
            finally
            {
                Object.DestroyImmediate(agentRoot);
            }
        }
    }
}
