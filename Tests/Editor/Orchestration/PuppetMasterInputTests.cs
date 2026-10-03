using LandLedgers.Primitives;
using NUnit.Framework;

namespace LandLedgers.EditorTests.Orchestration
{
    /// <summary>
    /// DEV-2: the puppet master's entity-id input parsing and dev lookup registry.
    /// Pure logic — no play mode needed.
    /// </summary>
    [TestFixture]
    public sealed class PuppetMasterInputTests
    {
        [Test]
        public void TryParse_FirstClassKinds_RoundTrips()
        {
            Assert.IsTrue(Scenarios.EntityIdInputParser.TryParse("P12", out EntityId person));
            Assert.AreEqual(EntityKind.Person, person.Kind);
            Assert.AreEqual(12, person.Id);

            Assert.IsTrue(Scenarios.EntityIdInputParser.TryParse("H3", out EntityId household));
            Assert.AreEqual(EntityKind.Household, household.Kind);
            Assert.AreEqual(3, household.Id);

            Assert.IsTrue(Scenarios.EntityIdInputParser.TryParse("A7", out EntityId animal));
            Assert.AreEqual(EntityKind.Animal, animal.Kind);
            Assert.AreEqual(7, animal.Id);

            Assert.IsTrue(Scenarios.EntityIdInputParser.TryParse("T99", out EntityId task));
            Assert.AreEqual(EntityKind.WorkTask, task.Kind);
            Assert.AreEqual(99, task.Id);
        }

        [Test]
        public void TryParse_MultiLetterPrefixes_LongestMatchWins()
        {
            // "PL12" must be Plot 12, not Person with garbage.
            Assert.IsTrue(Scenarios.EntityIdInputParser.TryParse("PL12", out EntityId plot));
            Assert.AreEqual(EntityKind.Plot, plot.Kind);
            Assert.AreEqual(12, plot.Id);

            Assert.IsTrue(Scenarios.EntityIdInputParser.TryParse("BLD7", out EntityId building));
            Assert.AreEqual(EntityKind.Building, building.Kind);
            Assert.AreEqual(7, building.Id);
        }

        [Test]
        public void TryParse_CustomKindFallbackForm()
        {
            Assert.IsTrue(Scenarios.EntityIdInputParser.TryParse("K1000:5", out EntityId custom));
            Assert.AreEqual(1000, (int)custom.Kind);
            Assert.AreEqual(5, custom.Id);
        }

        [Test]
        public void TryParse_ContractPrefix_NotConfusedWithCustomForm()
        {
            // "K" is the Contract prefix: "K12" is Contract 12, not a custom kind.
            Assert.IsTrue(Scenarios.EntityIdInputParser.TryParse("K12", out EntityId contract));
            Assert.AreEqual(EntityKind.Contract, contract.Kind);
            Assert.AreEqual(12, contract.Id);
        }

        [Test]
        public void TryParse_InvalidInput_Rejected()
        {
            Assert.IsFalse(Scenarios.EntityIdInputParser.TryParse("", out _));
            Assert.IsFalse(Scenarios.EntityIdInputParser.TryParse("   ", out _));
            Assert.IsFalse(Scenarios.EntityIdInputParser.TryParse("P", out _));
            Assert.IsFalse(Scenarios.EntityIdInputParser.TryParse("P-1", out _));
            Assert.IsFalse(Scenarios.EntityIdInputParser.TryParse("Z12", out _));
            Assert.IsFalse(Scenarios.EntityIdInputParser.TryParse("K1:2:3", out _));
            Assert.IsFalse(Scenarios.EntityIdInputParser.TryParse(null, out _));
        }

        [Test]
        public void TryParse_CaseInsensitive()
        {
            Assert.IsTrue(Scenarios.EntityIdInputParser.TryParse("p12", out EntityId id));
            Assert.AreEqual(EntityKind.Person, id.Kind);
            Assert.AreEqual(12, id.Id);
        }

        [Test]
        public void PuppetEntityLookup_RegisterResolveUnregister()
        {
            var lookup = new Scenarios.PuppetEntityLookup();
            var id = EntityId.For(EntityKind.Person, 12);
            var target = new object();

            Assert.AreEqual(0, lookup.Count);
            lookup.Register(id, target, "Test Person 12");
            Assert.AreEqual(1, lookup.Count);

            Assert.IsTrue(lookup.TryResolve(id, out object resolved, out string displayName));
            Assert.AreSame(target, resolved);
            Assert.AreEqual("Test Person 12", displayName);

            Assert.IsTrue(lookup.Unregister(id));
            Assert.AreEqual(0, lookup.Count);
            Assert.IsFalse(lookup.TryResolve(id, out _, out _));
        }

        [Test]
        public void PuppetEntityLookup_InvalidIdOrNullTarget_Ignored()
        {
            var lookup = new Scenarios.PuppetEntityLookup();

            lookup.Register(EntityId.Invalid, new object(), "bad");
            lookup.Register(EntityId.For(EntityKind.Person, 1), null, "null target");

            Assert.AreEqual(0, lookup.Count);
        }
    }
}
