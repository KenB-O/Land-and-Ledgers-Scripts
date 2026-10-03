using LandLedgers.Directory;
using NUnit.Framework;

namespace LandLedgers.EditorTests.Core
{
    /// <summary>
    /// PKG-5 (PL-07): federated runtime entity directory (3A-D02). Typed cross-domain lookup,
    /// existence checks, reference diagnostics and registration validation - without moving
    /// domain authority into the directory.
    /// </summary>
    [TestFixture]
    public sealed class EntityDirectoryTests
    {
        private sealed class FakePerson
        {
            public int Id;
        }

        private sealed class FakeBusiness
        {
            public string InstanceId;
        }

        [Test]
        public void RegisterAndResolve_TypedLookup()
        {
            var directory = new RuntimeEntityDirectory();
            var personRef = EntityRef.ForPerson(7);
            var person = new FakePerson { Id = 7 };

            directory.RegisterEntity(personRef, person);

            Assert.IsTrue(directory.Exists(personRef));
            Assert.IsTrue(directory.TryResolve(personRef, out FakePerson resolved));
            Assert.AreSame(person, resolved);
        }

        [Test]
        public void Resolve_Unregistered_ReturnsFalse()
        {
            var directory = new RuntimeEntityDirectory();

            Assert.IsFalse(directory.Exists(EntityRef.ForPerson(999)));
            Assert.IsFalse(directory.TryResolve(EntityRef.ForPerson(999), out FakePerson _));
        }

        [Test]
        public void DuplicateRegistration_FirstWins_WithDiagnostic()
        {
            var directory = new RuntimeEntityDirectory();
            var personRef = EntityRef.ForPerson(7);
            var first = new FakePerson { Id = 7 };
            var second = new FakePerson { Id = 7 };

            directory.RegisterEntity(personRef, first);
            directory.RegisterEntity(personRef, second);

            Assert.IsTrue(directory.TryResolve(personRef, out FakePerson resolved));
            Assert.AreSame(first, resolved);
            Assert.AreEqual(1, directory.RegistrationDiagnostics.Count);
        }

        [Test]
        public void DomainRegistry_DuplicateRegistration_RejectedDeterministically()
        {
            var registry = new DomainEntityRegistry<int, FakePerson>("PopulationRegistry");

            Assert.IsNull(registry.Register(7, new FakePerson { Id = 7 }, "generator"));
            Assert.IsNotNull(registry.Register(7, new FakePerson { Id = 7 }, "migration"));
            Assert.AreEqual(1, registry.Count);
            Assert.AreEqual(1, registry.Diagnostics.Count);
        }

        [Test]
        public void RequiredReference_MissingTarget_IsInvalidQuarantined()
        {
            var directory = new RuntimeEntityDirectory();
            var employmentRef = EntityRef.ForEmployment("emp-1");
            // Employment -> employee is REQUIRED; the employee is not registered.

            directory.DeclareReference(employmentRef, EntityRef.ForPerson(7), ReferenceClass.Required, "Employment -> employee");

            var results = directory.ValidateReferences();

            Assert.AreEqual(1, results.Count);
            Assert.AreEqual(ReferenceValidationOutcome.InvalidQuarantined, results[0].Outcome);
            Assert.IsTrue(results[0].Details.Contains("BLOCK execution"));
        }

        [Test]
        public void RequiredReference_ResolvedTarget_IsValid()
        {
            var directory = new RuntimeEntityDirectory();
            var employmentRef = EntityRef.ForEmployment("emp-1");
            directory.RegisterEntity(EntityRef.ForPerson(7), new FakePerson { Id = 7 });

            directory.DeclareReference(employmentRef, EntityRef.ForPerson(7), ReferenceClass.Required, "Employment -> employee");

            var results = directory.ValidateReferences();

            Assert.AreEqual(ReferenceValidationOutcome.Valid, results[0].Outcome);
        }

        [Test]
        public void OptionalReference_MissingTarget_NormalizesAndContinues()
        {
            var directory = new RuntimeEntityDirectory();

            directory.DeclareReference(
                EntityRef.ForBusiness("biz-1"), EntityRef.ForPerson(7), ReferenceClass.Optional, "optional manager");

            var results = directory.ValidateReferences();

            Assert.AreEqual(ReferenceValidationOutcome.NormalizedUnresolved, results[0].Outcome);
        }

        [Test]
        public void SoftReference_MissingTarget_IsAllowed()
        {
            var directory = new RuntimeEntityDirectory();

            directory.DeclareReference(
                EntityRef.ForBusiness("biz-1"), EntityRef.ForBusiness("biz-2"), ReferenceClass.Soft, "preferred supplier");

            var results = directory.ValidateReferences();

            Assert.AreEqual(ReferenceValidationOutcome.SoftUnresolved, results[0].Outcome);
        }

        [Test]
        public void HistoricalReference_MissingTarget_IsRetained()
        {
            var directory = new RuntimeEntityDirectory();

            directory.DeclareReference(
                EntityRef.ForPerson(7), EntityRef.ForBusiness("dissolved-biz"), ReferenceClass.Historical, "former employer");

            var results = directory.ValidateReferences();

            Assert.AreEqual(ReferenceValidationOutcome.HistoricalRetained, results[0].Outcome);
        }

        [Test]
        public void DerivedReference_IsNeverAuthoritative()
        {
            var directory = new RuntimeEntityDirectory();
            directory.RegisterEntity(EntityRef.ForBusiness("biz-1"), new FakeBusiness { InstanceId = "biz-1" });

            directory.DeclareReference(
                EntityRef.ForBusiness("biz-1"), EntityRef.ForPerson(7), ReferenceClass.Derived, "employee index");

            var results = directory.ValidateReferences();

            Assert.AreEqual(ReferenceValidationOutcome.DerivedNotAuthoritative, results[0].Outcome);
        }
    }
}
