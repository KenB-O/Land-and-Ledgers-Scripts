using System.Collections.Generic;
using LandLedgers.Orchestration.Bootstrap;
using NUnit.Framework;

namespace LandLedgers.EditorTests.Bootstrap
{
    /// <summary>
    /// Pass XII Worker A (LL-PLAY-002/003): the single-instance authority
    /// guard and the duplicate census audit. A second live drivers/hub/
    /// civic instance must never silently share economic authority: the
    /// claim registry refuses the second holder, and the census audit
    /// names every duplicate object for scene correction.
    /// </summary>
    [TestFixture]
    public sealed class Pass12BootstrapAuthorityTests
    {
        [SetUp]
        public void SetUp()
        {
            SingleInstanceAuthorityGuard.ResetAll();
        }

        [TearDown]
        public void TearDown()
        {
            SingleInstanceAuthorityGuard.ResetAll();
        }

        [Test]
        public void TryClaim_FirstHolderWins_SecondHolderRefused_Loudly()
        {
            var first = new object();
            var second = new object();

            Assert.IsTrue(SingleInstanceAuthorityGuard.TryClaim("SimulationDrivers", first, out string refusal));
            Assert.IsNull(refusal);
            Assert.IsTrue(SingleInstanceAuthorityGuard.IsClaimed("SimulationDrivers"));
            Assert.IsTrue(SingleInstanceAuthorityGuard.IsClaimedBy("SimulationDrivers", first));
            Assert.IsFalse(SingleInstanceAuthorityGuard.IsClaimedBy("SimulationDrivers", second));

            Assert.IsFalse(SingleInstanceAuthorityGuard.TryClaim("SimulationDrivers", second, out refusal));
            Assert.IsFalse(string.IsNullOrWhiteSpace(refusal), "a refused claim must say why");
            Assert.IsTrue(SingleInstanceAuthorityGuard.IsClaimedBy("SimulationDrivers", first),
                "the refused second holder must not displace the first");
        }

        [Test]
        public void TryClaim_SameHolderReclaim_Succeeds()
        {
            var holder = new object();
            Assert.IsTrue(SingleInstanceAuthorityGuard.TryClaim("SimulationSystemsHub", holder, out _));
            Assert.IsTrue(SingleInstanceAuthorityGuard.TryClaim("SimulationSystemsHub", holder, out _),
                "Awake followed by an OnEnable re-claim from the same instance must not self-refuse");
        }

        [Test]
        public void Release_OnlyOwnerReleases_StrangerReleaseIgnored()
        {
            var owner = new object();
            var stranger = new object();
            Assert.IsTrue(SingleInstanceAuthorityGuard.TryClaim("CivicFoundation", owner, out _));

            Assert.IsFalse(SingleInstanceAuthorityGuard.Release("CivicFoundation", stranger),
                "a stranger must never steal or free another authority's claim");
            Assert.IsTrue(SingleInstanceAuthorityGuard.IsClaimed("CivicFoundation"));

            Assert.IsTrue(SingleInstanceAuthorityGuard.Release("CivicFoundation", owner));
            Assert.IsFalse(SingleInstanceAuthorityGuard.IsClaimed("CivicFoundation"));

            Assert.IsTrue(SingleInstanceAuthorityGuard.TryClaim("CivicFoundation", stranger, out _),
                "after the owner releases, a new holder may claim");
        }

        [Test]
        public void Release_AndReclaim_ModelsDisableThenEnable()
        {
            var first = new object();
            var second = new object();
            Assert.IsTrue(SingleInstanceAuthorityGuard.TryClaim("SimulationDrivers", first, out _));
            Assert.IsTrue(SingleInstanceAuthorityGuard.Release("SimulationDrivers", first),
                "OnDisable releases the claim so a re-enabled instance can reclaim it");
            Assert.IsTrue(SingleInstanceAuthorityGuard.TryClaim("SimulationDrivers", second, out _));
            Assert.IsFalse(SingleInstanceAuthorityGuard.TryClaim("SimulationDrivers", first, out _),
                "the previously disabled instance must not reclaim while the new holder operates");
        }

        [Test]
        public void TryClaim_RejectsMissingAuthorityOrHolder()
        {
            Assert.IsFalse(SingleInstanceAuthorityGuard.TryClaim(null, new object(), out _));
            Assert.IsFalse(SingleInstanceAuthorityGuard.TryClaim("SimulationDrivers", null, out _));
            Assert.IsFalse(SingleInstanceAuthorityGuard.IsClaimed(null));
        }

        [Test]
        public void DescribeDuplicates_SingleInstance_IsSilent_TwoInstances_NameBoth()
        {
            var single = new List<BootstrapAuthorityAudit.Census>
            {
                new BootstrapAuthorityAudit.Census { Name = "SimulationDrivers", ObjectNames = new List<string> { "Drivers" } },
                new BootstrapAuthorityAudit.Census { Name = "SimulationSystemsHub", ObjectNames = new List<string>() },
            };
            Assert.AreEqual(0, BootstrapAuthorityAudit.DescribeDuplicates(single).Count,
                "one instance, or none (bootstrap fallback creates one), is not a duplicate error");

            var duplicated = new List<BootstrapAuthorityAudit.Census>
            {
                new BootstrapAuthorityAudit.Census
                {
                    Name = "CivicFoundation",
                    ObjectNames = new List<string> { "Civic Foundation", "Civic Foundation (1)" },
                },
            };
            List<string> errors = BootstrapAuthorityAudit.DescribeDuplicates(duplicated);
            Assert.AreEqual(1, errors.Count);
            Assert.IsTrue(errors[0].Contains("Civic Foundation"), "the error must name the duplicates for scene correction");
            Assert.IsTrue(errors[0].Contains("Civic Foundation (1)"));
        }

        [Test]
        public void DescribeDuplicates_NullAndEmptyInputs_NeverThrow()
        {
            Assert.AreEqual(0, BootstrapAuthorityAudit.DescribeDuplicates(null).Count);
            Assert.AreEqual(0, BootstrapAuthorityAudit.DescribeDuplicates(new List<BootstrapAuthorityAudit.Census>()).Count);
            Assert.AreEqual(0, BootstrapAuthorityAudit.DescribeDuplicates(
                new List<BootstrapAuthorityAudit.Census> { null, new BootstrapAuthorityAudit.Census { Name = "X", ObjectNames = null } }).Count);
        }
    }
}
