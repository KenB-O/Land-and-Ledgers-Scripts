using System.Linq;
using LandLedgers.Population;
using NUnit.Framework;

namespace LandLedgers.EditorTests.Population
{
    [TestFixture]
    public sealed class KinshipRegistryTests
    {
        [Test]
        public void ParentageRejectsDirectAndIndirectCyclesWithoutChangingExistingGraph()
        {
            var kinship = new KinshipRegistry();
            Assert.IsTrue(kinship.TryAddParentChild(10, 20, "fixture"));
            Assert.IsTrue(kinship.TryAddParentChild(20, 30, "fixture"));

            Assert.IsFalse(kinship.TryAddParentChild(30, 10, "bad import"));
            kinship.AddEdge(10, 30, KinshipRelation.Child, "bad inverse import");

            Assert.AreEqual(4, kinship.EdgeCount);
            CollectionAssert.AreEqual(new[] { 20 }, kinship.GetParents(30));
            CollectionAssert.AreEqual(new[] { 20, 30 }, kinship.GetDescendants(10).Select(item => item.PersonId).ToArray());
            Assert.IsTrue(kinship.Diagnostics.Count >= 2);
        }

        [Test]
        public void ExplicitSiblingIsSymmetricAndSupportsUnknownParents()
        {
            var kinship = new KinshipRegistry();
            kinship.AddSiblings(41, 42, "known family fact");

            CollectionAssert.AreEqual(new[] { 42 }, kinship.GetRelated(41, KinshipRelation.Sibling));
            CollectionAssert.AreEqual(new[] { 41 }, kinship.GetRelated(42, KinshipRelation.Sibling));
            Assert.AreEqual(DerivedKinshipRelation.Sibling, kinship.GetDerivedRelationship(41, 42).Relation);
            Assert.IsTrue(kinship.GetDerivedRelationship(41, 42).IsExplicit);
            Assert.AreEqual(2, kinship.EdgeCount);
        }

        [Test]
        public void DerivedQueriesResolveArbitraryDepthAncestryAndCollateralRelationships()
        {
            var kinship = new KinshipRegistry();
            kinship.AddParentChild(1, 2, "fixture");
            kinship.AddParentChild(2, 3, "fixture");
            kinship.AddParentChild(3, 4, "fixture");
            kinship.AddParentChild(2, 5, "fixture");
            kinship.AddParentChild(5, 6, "fixture");
            kinship.AddParentChild(6, 7, "fixture");

            DerivedKinshipResult distantAncestor = kinship.GetDerivedRelationship(4, 1);
            Assert.AreEqual(DerivedKinshipRelation.Ancestor, distantAncestor.Relation);
            Assert.AreEqual(3, distantAncestor.Degree);
            Assert.AreEqual(DerivedKinshipRelation.Descendant, kinship.GetDerivedRelationship(1, 4).Relation);
            Assert.AreEqual(DerivedKinshipRelation.Sibling, kinship.GetDerivedRelationship(3, 5).Relation);
            Assert.AreEqual(DerivedKinshipRelation.AuntOrUncle, kinship.GetDerivedRelationship(4, 5).Relation);

            DerivedKinshipResult cousin = kinship.GetDerivedRelationship(4, 6);
            Assert.AreEqual(DerivedKinshipRelation.Cousin, cousin.Relation);
            Assert.AreEqual(1, cousin.Degree);
            Assert.AreEqual(0, cousin.Removal);
            Assert.AreEqual(2, cousin.CommonAncestorPersonId);
            DerivedKinshipResult removedCousin = kinship.GetDerivedRelationship(4, 7);
            Assert.AreEqual(DerivedKinshipRelation.Cousin, removedCousin.Relation);
            Assert.AreEqual(1, removedCousin.Degree);
            Assert.AreEqual(1, removedCousin.Removal);
            Assert.AreEqual(3, kinship.GetAncestors(4).Count);
            Assert.AreEqual(5, kinship.GetDescendants(2).Count);
        }

        [Test]
        public void SaveRestorePreservesStableIdsNotesRelationshipsAndIsIdempotent()
        {
            var source = new KinshipRegistry();
            source.AddParentChild(101, 205, "authored record");
            source.AddSiblings(205, 309, "known sibling; parents not recorded");
            source.AddSpouses(309, 410, "marriage register");
            source.AllEdges.First(edge => edge.FromPersonId == 205 && edge.ToPersonId == 309).Notes = "no parent identities inferred";

            KinshipRegistrySaveDto saved = source.CaptureSaveDto();
            var restored = new KinshipRegistry();
            restored.LoadFromSaveDto(saved);
            restored.LoadFromSaveDto(saved);

            Assert.AreEqual(source.EdgeCount, restored.EdgeCount);
            CollectionAssert.AreEqual(source.AllEdges.Select(EdgeKey).OrderBy(value => value),
                restored.AllEdges.Select(EdgeKey).OrderBy(value => value));
            Assert.AreEqual("no parent identities inferred", restored.AllEdges
                .First(edge => edge.FromPersonId == 205 && edge.ToPersonId == 309).Notes);
            Assert.AreEqual(DerivedKinshipRelation.Parent, restored.GetDerivedRelationship(205, 101).Relation);
            Assert.AreEqual(DerivedKinshipRelation.Sibling, restored.GetDerivedRelationship(205, 309).Relation);
            Assert.AreEqual(DerivedKinshipRelation.Spouse, restored.GetDerivedRelationship(309, 410).Relation);
        }

        [Test]
        public void RestoreSkipsCyclicSavedParentageAndKeepsValidEdges()
        {
            var dto = new KinshipRegistrySaveDto();
            dto.edges.Add(new KinshipEdge { FromPersonId = 1, ToPersonId = 2, Relation = KinshipRelation.Parent, Source = "valid" });
            dto.edges.Add(new KinshipEdge { FromPersonId = 2, ToPersonId = 3, Relation = KinshipRelation.Parent, Source = "valid" });
            dto.edges.Add(new KinshipEdge { FromPersonId = 3, ToPersonId = 1, Relation = KinshipRelation.Parent, Source = "corrupt" });

            var kinship = new KinshipRegistry();
            kinship.LoadFromSaveDto(dto);

            Assert.AreEqual(4, kinship.EdgeCount);
            Assert.IsTrue(kinship.Diagnostics.Any(message => message.Contains("cyclic")));
            Assert.AreEqual(DerivedKinshipRelation.Grandparent, kinship.GetDerivedRelationship(3, 1).Relation);
        }

        private static string EdgeKey(KinshipEdge edge)
        {
            return $"{edge.FromPersonId}:{edge.ToPersonId}:{(int)edge.Relation}:{edge.Source}:{edge.Notes}";
        }
    }
}
