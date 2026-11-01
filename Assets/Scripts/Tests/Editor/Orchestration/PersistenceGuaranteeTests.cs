using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace LandLedgers.EditorTests.Orchestration
{
    /// <summary>
    /// DEV-3: the "nothing vanishes" guarantees — name cleaning, hidden-flag detection,
    /// and the asset guard. Pure logic; the scene-dependent registrar paths are verified
    /// on Kennedy's machine (they need a live scene).
    /// </summary>
    [TestFixture]
    public sealed class PersistenceGuaranteeTests
    {
        [Test]
        public void CleanSpawnName_StripsCloneSuffix_AndTrims()
        {
            Assert.AreEqual("Store Clerk", global::LandLedgers.Orchestration.Scenarios.RuntimeEntityRoot.CleanSpawnName("Store Clerk(Clone)", "fallback"));
            Assert.AreEqual("Store Clerk", global::LandLedgers.Orchestration.Scenarios.RuntimeEntityRoot.CleanSpawnName("  Store Clerk (Clone) ", "fallback"));
            Assert.AreEqual("Chicken", global::LandLedgers.Orchestration.Scenarios.RuntimeEntityRoot.CleanSpawnName("Chicken", "fallback"));
        }

        [Test]
        public void CleanSpawnName_EmptyName_UsesFallback()
        {
            Assert.AreEqual("fallback", global::LandLedgers.Orchestration.Scenarios.RuntimeEntityRoot.CleanSpawnName("", "fallback"));
            Assert.AreEqual("fallback", global::LandLedgers.Orchestration.Scenarios.RuntimeEntityRoot.CleanSpawnName("(Clone)", "fallback"));
            Assert.AreEqual("fallback", global::LandLedgers.Orchestration.Scenarios.RuntimeEntityRoot.CleanSpawnName(null, "fallback"));
            Assert.AreEqual(
                "Unnamed Runtime Entity",
                global::LandLedgers.Orchestration.Scenarios.RuntimeEntityRoot.CleanSpawnName("", ""));
        }

        [Test]
        public void HasHiddenFlags_DetectsHierarchyHidingFlags()
        {
            Assert.IsFalse(global::LandLedgers.Orchestration.Scenarios.RuntimeEntityRoot.HasHiddenFlags(HideFlags.None));
            Assert.IsTrue(global::LandLedgers.Orchestration.Scenarios.RuntimeEntityRoot.HasHiddenFlags(HideFlags.HideInHierarchy));
            Assert.IsTrue(global::LandLedgers.Orchestration.Scenarios.RuntimeEntityRoot.HasHiddenFlags(HideFlags.HideInInspector));
            Assert.IsTrue(global::LandLedgers.Orchestration.Scenarios.RuntimeEntityRoot.HasHiddenFlags(HideFlags.NotEditable));

            // Combined flags still detected.
            Assert.IsTrue(global::LandLedgers.Orchestration.Scenarios.RuntimeEntityRoot.HasHiddenFlags(
                HideFlags.HideInHierarchy | HideFlags.DontSaveInEditor));
        }

        [Test]
        public void RequireScenarioAsset_NullAsset_FailsLoudly()
        {
            // Logs an error (loud by design); returns false so callers cannot proceed silently.
            LogAssert.Expect(LogType.Error, new System.Text.RegularExpressions.Regex(".*no ScenarioAsset supplied.*"));
            Assert.IsFalse(global::LandLedgers.Orchestration.Scenarios.DevGuards.RequireScenarioAsset(null, null, "Test"));
        }

        [Test]
        public void RequireScenarioAsset_ValidAsset_Passes()
        {
            var asset = ScriptableObject.CreateInstance<global::LandLedgers.Orchestration.Scenarios.ScenarioAsset>();
            Assert.IsTrue(global::LandLedgers.Orchestration.Scenarios.DevGuards.RequireScenarioAsset(asset, null, "Test"));
            Object.DestroyImmediate(asset);
        }

        [Test]
        public void RootName_IsStableAndVisible()
        {
            // The root name is a contract: docs, tests and Kennedy's hierarchy all agree.
            Assert.AreEqual("Runtime Entities", global::LandLedgers.Orchestration.Scenarios.RuntimeEntityRoot.RootName);
        }
    }
}
